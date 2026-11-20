using CKAN;
using CKAN.Configuration;
using CKAN.IO;
using CKAN.Versioning;
using IUser = CKAN.IUser;
using System.Diagnostics;
using System.Net;
using System.Threading;

namespace Pcl.Ckan;

/// <summary>
/// 当前实例以只读方式附加（注册表被其他 CKAN 进程占用），安装 / 升级 / 卸载等写操作被拒绝。
/// </summary>
public sealed class InstanceReadOnlyException : InvalidOperationException
{
    public InstanceReadOnlyException()
        : base("KSP 实例注册表被其他 CKAN 客户端占用，当前为只读模式；请先关闭常驻 CKAN 客户端后再执行安装或卸载。")
    {
    }
}

/// <summary>
/// <see cref="IModService"/> implementation backed directly by CKAN.Core's public APIs.
/// The class is safe to construct even on a machine without KSP: all operations degrade to
/// empty/no-op results until an instance is attached.
/// </summary>
public sealed class ModService : IModService, IDisposable
{
    private const string UserAgent = "Pcl.Ckan/1.0";
    private readonly IUser _user;
    private readonly IConfiguration _config;
    private readonly RepositoryDataManager _repoData;
    private readonly GameInstanceManager _manager;

    private GameInstance? _instance;
    private RegistryManager? _registryManager;
    private Registry? _readonlyRegistry;
    private readonly SpaceDockClient _spaceDock = new();
    private readonly object _catalogLock = new();
    private readonly object _repoLock = new();
    private IReadOnlyList<ModSearchResult>? _cachedAll;
    private Task? _warmTask;
    private readonly SemaphoreSlim _catalogGate = new(1, 1);

    /// <summary>诊断埋点汇（如启动器 log4net）；为空时仅写入内存日志。</summary>
    public Action<string>? TraceSink { get; set; }

    private void Trace(string message)
    {
        try { Log.RaiseMessage(message); } catch { }
        try { TraceSink?.Invoke(message); } catch { }
    }

    /// <summary>
    /// Create the adapter with a self-contained data directory under the user's temp folder,
    /// so it never collides with a system-wide CKAN install.
    /// </summary>
    public ModService()
        : this(null, null)
    {
    }

    /// <param name="user">CKAN user sink (defaults to a StringBuilder-backed user so we can show logs).</param>
    /// <param name="dataDir">Where CKAN config and cached repo data live; defaults to a temp dir.</param>
    public ModService(IUser? user = null, string? dataDir = null)
    {
        Log = user as StringBuilderUser ?? new StringBuilderUser();
        _user = Log;
        dataDir ??= Path.Combine(Path.GetTempPath(), "Pcl.Ckan");
        Directory.CreateDirectory(dataDir);
        _config = new JsonConfiguration(Path.Combine(dataDir, "config.json"));
        _repoData = new RepositoryDataManager(Path.Combine(dataDir, "repo-data"));
        _manager = new GameInstanceManager(_user, _config);
    }

    /// <summary>
    /// Apply process-wide network settings: HTTP proxy, default connection limit, idle timeout.
    /// (NetAsyncDownloader does not expose these in its ctor.)
    /// </summary>
    public static void ApplyNetworkSettings(string? proxy, int concurrency, int timeoutSeconds)
    {
        if (!string.IsNullOrWhiteSpace(proxy))
        {
            try { WebRequest.DefaultWebProxy = new WebProxy(proxy, true); } catch { }
        }
        try { ServicePointManager.DefaultConnectionLimit = Math.Max(2, concurrency); } catch { }
        try { ServicePointManager.MaxServicePointIdleTime = Math.Max(5, timeoutSeconds) * 1000; } catch { }
    }

    /// <inheritdoc />
    public event Action<string, int>? ProgressReported
    {
        add { Log.ProgressReported += value; }
        remove { Log.ProgressReported -= value; }
    }

    /// <inheritdoc />
    public event Action<long>? SpeedReported
    {
        add { Log.SpeedReported += value; }
        remove { Log.SpeedReported -= value; }
    }

    /// <inheritdoc />
    public long CurrentSpeed => Log.LastBytesPerSecond;
    /// <summary>Accumulated log sink (for the "view log" window).</summary>
    public StringBuilderUser Log { get; }

    /// <summary>The underlying CKAN game-instance manager (advanced use).</summary>
    public GameInstanceManager Manager => _manager;

    /// <summary>The currently attached instance, if any.</summary>
    public GameInstance? CurrentInstance => _instance;

    /// <summary>The registry used for read paths (read-write manager registry or the read-only one).</summary>
    private Registry? Registry => _registryManager?.registry ?? _readonlyRegistry;

    /// <summary>
    /// Attach a KSP instance (e.g. produced by <see cref="KspInstanceService"/>) and open its
    /// CKAN registry read-write. Required for install / upgrade / remove operations.
    /// </summary>
    public void AttachInstance(GameInstance instance)
    {
        _instance = instance;
        _readonlyRegistry = null;
        _manager.SetCurrentInstance(instance);
        _registryManager = RegistryManager.Instance(instance, _repoData);
    }

    /// <summary>
    /// Attach a KSP instance and open its CKAN registry read-only (no lock, no writes into the
    /// game's CKAN folder). Suitable for listing / search / planning.
    /// </summary>
    public void AttachInstanceReadOnly(GameInstance instance)
    {
        _instance = instance;
        _registryManager?.Dispose();
        _registryManager = null;
        _readonlyRegistry = RegistryManager.ReadOnlyRegistry(instance, _repoData);
    }

    /// <summary>
    /// 附加实例；若注册表被其他 CKAN 进程占用（锁冲突），自动回退只读模式，
    /// 搜索/浏览仍可用；安装/升级/移除需要读写锁，只读模式下会拒绝执行。
    /// </summary>
    public void AttachInstanceRobust(GameInstance instance)
    {
        try
        {
            AttachInstance(instance);
        }
        catch (Exception ex) when (ex is RegistryInUseKraken or IOException)
        {
            AttachInstanceReadOnly(instance);
        }
    }

    /// <summary>Mirror URLs to use when strategy is MirrorOnly or OfficialFirst.</summary>
    public System.Collections.Generic.List<string> MirrorUrls { get; set; } = new();
    /// <summary>Default official CKAN repo URL (used as fallback when strategy != MirrorOnly).</summary>
    public string OfficialRepoUrl { get; set; } = "https://github.com/KSP-CKAN/CKAN-meta/archive/master.tar.gz";
    /// <summary>One of "MirrorOnly" / "OfficialFirst" / "OfficialOnly".</summary>
    public string RepoStrategy { get; set; } = "OfficialFirst";
    /// <summary>是否对 CKAN 模块的 GitHub 下载链接套用公共加速前缀。</summary>
    public bool GitHubAccelerationEnabled { get; set; }
    /// <summary>加速前缀，如 https://ghfast.top/，直接拼在原地址前。</summary>
    public string GitHubAccelerationPrefix { get; set; } = "";

    /// <summary>被加速改写前的原始下载地址，用于失败回退。</summary>
    private readonly System.Collections.Generic.Dictionary<CkanModule, System.Collections.Generic.List<Uri>> _accelOriginals = new();

    /// <inheritdoc />
    public async Task RefreshMasterRegistryAsync(CancellationToken cancellationToken = default)
    {
        if (_instance == null || _registryManager == null)
        {
            return;
        }

        // 段 1：短锁补齐仓库注册表并落盘（Repositories 字典写，与目录枚举互斥）
        await _catalogGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Trace("Refresh 开始");
            var registry = _registryManager.registry;
            var existing = registry.Repositories;

            void AddRepo(string name, string uri)
            {
                var uriTrim = uri.Trim();
                if (string.IsNullOrWhiteSpace(uriTrim) || existing.ContainsKey(name))
                    return;
                if (existing.Values.Any(r => r.uri != null &&
                    string.Equals(r.uri.ToString(), uriTrim, StringComparison.OrdinalIgnoreCase)))
                    return;
                registry.RepositoriesAdd(new CKAN.Repository(name, uriTrim));
            }

            if (RepoStrategy != "OfficialOnly")
            {
                var i = 0;
                foreach (var u in MirrorUrls)
                {
                    if (!string.IsNullOrWhiteSpace(u))
                        AddRepo($"PCL-KSP-Mirror-{++i}", u.Trim());
                }
            }
            if (RepoStrategy != "MirrorOnly")
            {
                AddRepo("CKAN-Official", OfficialRepoUrl);
            }
            _registryManager.Save();
            Trace("注册表 Save 完成");
        }
        finally
        {
            _catalogGate.Release();
        }

        // 段 2：仓库元数据网络下载在锁外进行，避免长时间占用门控阻塞目录枚举与搜索
        try
        {
            var repos = DistinctRepos(_registryManager.registry.Repositories.Values);
            var downloader = new NetAsyncDownloader(_user,
                () => (System.Security.Cryptography.HashAlgorithm?)null,
                UserAgent,
                cancellationToken);
            lock (_repoLock)
            {
                var updateResult = _repoData.Update(repos, _instance.Game, false, downloader, _user, UserAgent);
                Trace($"仓库 Update 完成: {updateResult}");
            }
        }
        catch (Exception ex)
        {
            try { _user.RaiseError("刷新仓库注册表失败: " + ex.Message); } catch { }
        }

        // 段 3：网络更新结束后短锁失效内存缓存
        await _catalogGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_catalogLock)
            {
                _cachedAll = null;
                _warmTask = null;
            }
        }
        finally
        {
            _catalogGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ModSearchResult>> SearchAsync(string? query, CancellationToken cancellationToken = default)
    {
        var isEmptyQuery = string.IsNullOrWhiteSpace(query);
        Task? warmTask = null;
        if (isEmptyQuery)
        {
            lock (_catalogLock)
            {
                if (_cachedAll is not null)
                    return _cachedAll;
                warmTask = _warmTask;
            }
            if (warmTask is not null)
            {
                Trace("Search waiting warmTask");
                try { await warmTask.ConfigureAwait(false); } catch { }
                lock (_catalogLock)
                {
                    if (_cachedAll is not null)
                        return _cachedAll;
                }
            }
        }
        return await BuildAllAsync(query, isEmptyQuery, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Catalog enumerate + project + cache write. Shared by warm-up and on-demand search; does not go through the warmTask reuse branch (avoids awaiting itself).</summary>
    private async Task<IReadOnlyList<ModSearchResult>> BuildAllAsync(string? query, bool isEmptyQuery, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var catalog = await EnumerateCatalogLockedAsync(cancellationToken).ConfigureAwait(false);
        Trace($"Catalog enumerate {catalog.Count} in {sw.ElapsedMilliseconds} ms");
        sw.Restart();
        var results = catalog.Where(m => MatchesQuery(m, query)).Select(ToResult).ToList();
        Trace($"Projected {results.Count} in {sw.ElapsedMilliseconds} ms");
        if (isEmptyQuery && results.Count > 0)
        {
            lock (_catalogLock)
            {
                _cachedAll = results;
            }
        }
        return results;
    }

    /// <inheritdoc />
    public Task WarmUpAsync(CancellationToken cancellationToken = default)
    {
        lock (_catalogLock)
        {
            if (_cachedAll is not null)
                return Task.CompletedTask;
            _warmTask ??= Task.Run(async () =>
            {
                try
                {
                    Trace("WarmUp enumerate start");
                    await BuildAllAsync(null, true, cancellationToken).ConfigureAwait(false);
                    Trace("WarmUp enumerate done");
                }
                catch
                {
                    // Warm-up failure must not block; the download page loads on demand.
                }
            }, cancellationToken);
            return _warmTask;
        }
    }

    /// <summary>对 results 的 [start, start+count) 区间做 SpaceDock 回填；并发限流，失败项保持原值。</summary>
    public async Task EnrichRangeAsync(IList<ModSearchResult> results, int start, int count, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(results);
        if (count <= 0 || start < 0 || start >= results.Count)
            return;
        count = Math.Min(count, results.Count - start);

        using var semaphore = new SemaphoreSlim(8);
        var tasks = new List<Task>();
        for (var i = 0; i < count; i++)
        {
            var idx = start + i;
            tasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var mod = LatestAvailable(results[idx].Identifier);
                    if (mod?.resources?.spacedock is not { } url)
                        return;
                    if (!SpaceDockClient.TryParseModId(url, out var modId))
                        return;

                    var sd = await _spaceDock.FetchAsync(modId, cancellationToken).ConfigureAwait(false);
                    if (sd is null)
                        return;

                    var r = results[idx];
                    results[idx] = r with
                    {
                        Downloads = sd.Downloads > 0 ? sd.Downloads : r.Downloads,
                        Author = string.IsNullOrEmpty(r.Author) ? sd.Author : r.Author,
                        IconUrl = string.IsNullOrEmpty(r.IconUrl) ? sd.Background : r.IconUrl,
                        Description = string.IsNullOrEmpty(r.Description) ? sd.ShortDescription : r.Description,
                        License = string.IsNullOrEmpty(r.License) ? sd.License : r.License,
                        Homepage = string.IsNullOrEmpty(r.Homepage) ? sd.Website : r.Homepage,
                        Version = string.IsNullOrEmpty(r.Version) ? sd.LatestVersion : r.Version
                    };
                }
                finally
                {
                    semaphore.Release();
                }
            }, cancellationToken));
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>
    /// 首屏用的快速列表：只读注册表里已安装的模组（标识、名称、版本），不枚举 CKAN 目录、
    /// 不解析依赖与更新状态，通常在一秒内返回。页面应先用它把列表渲染出来，
    /// 再调用 <see cref="ListInstalledAsync"/> 补齐可更新标记、依赖、冲突、下载量等信息。
    /// </summary>
    public Task<IReadOnlyList<ModSearchResult>> ListInstalledQuickAsync(CancellationToken cancellationToken = default)
    {
        var reg = Registry;
        if (_instance == null || reg == null)
        {
            return Task.FromResult<IReadOnlyList<ModSearchResult>>(Array.Empty<ModSearchResult>());
        }

        return Task.Run<IReadOnlyList<ModSearchResult>>(() =>
        {
            var list = new List<ModSearchResult>();
            foreach (var installed in reg.InstalledModules)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var identifier = installed.identifier;
                var displayName = installed.Module?.name;
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    displayName = identifier;
                }

                list.Add(new ModSearchResult(
                    identifier,
                    displayName,
                    null,
                    null,
                    null,
                    installed.Module?.version?.ToString(),
                    null,
                    IsInstalled: true,
                    HasUpdate: false));
            }

            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return list;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ModSearchResult>> ListInstalledAsync(CancellationToken cancellationToken = default)
    {
        var reg = Registry;
        if (_instance == null || reg == null)
        {
            return Array.Empty<ModSearchResult>();
        }

        var sw = Stopwatch.StartNew();
        var catalog = await EnumerateCatalogLockedAsync(cancellationToken).ConfigureAwait(false);
        var results = await Task.Run(() =>
        {
            var installed = reg.Installed();

            // 一次性遍历目录，构建 标识->显示名、反向依赖、冲突 三张映射，避免逐条模组重复全目录扫描。
            var nameById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var reverseMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var conflictMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in catalog)
            {
                nameById[m.identifier] = m.name ?? m.identifier;
                foreach (var r in m.depends ?? Enumerable.Empty<CKAN.RelationshipDescriptor>())
                {
                    var dep = (r as CKAN.ModuleRelationshipDescriptor)?.name;
                    if (!string.IsNullOrEmpty(dep) && !string.Equals(dep, m.identifier, StringComparison.OrdinalIgnoreCase))
                        AddRel(reverseMap, dep, m.name ?? m.identifier);
                }
                foreach (var r in m.conflicts ?? Enumerable.Empty<CKAN.RelationshipDescriptor>())
                {
                    var conflict = (r as CKAN.ModuleRelationshipDescriptor)?.name;
                    if (!string.IsNullOrEmpty(conflict) && !string.Equals(conflict, m.identifier, StringComparison.OrdinalIgnoreCase))
                        AddRel(conflictMap, conflict, m.name ?? m.identifier);
                }
            }

            string ResolveName(string ident) => nameById.TryGetValue(ident, out var n) ? n : ident;

            var list = new List<ModSearchResult>();
            foreach (var (identifier, _) in installed)
            {
                var mod = LatestAvailable(identifier);
                var result = ToResult(mod, identifier, installed);

                var depList = mod?.depends?
                    .Select(r => (r as CKAN.ModuleRelationshipDescriptor)?.name)
                    .Where(n => !string.IsNullOrEmpty(n))!
                    .Select(ResolveName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                IReadOnlyList<string> deps = depList ?? (IReadOnlyList<string>)Array.Empty<string>();

                reverseMap.TryGetValue(identifier, out var reverse);
                conflictMap.TryGetValue(identifier, out var conflicts);

                list.Add(result with
                {
                    Depends = deps,
                    ReverseDeps = reverse?.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    Conflicts = conflicts?.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                });
            }

            // CKAN 的 AD（autodetected）：GameData 里已扫描登记、但不由任何已装模组负责的 DLL。
            foreach (var dllIdentifier in reg.InstalledDlls)
            {
                if (installed.Keys.Any(k => string.Equals(k, dllIdentifier, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
                var dllModule = LatestAvailable(dllIdentifier);
                list.Add(ToResult(dllModule, dllIdentifier, installed) with
                {
                    IsInstalled = true,
                    IsAutodetected = true,
                    Name = dllModule?.name ?? dllIdentifier + "（AD）"
                });
            }
            return list;
        }, cancellationToken).ConfigureAwait(false);
        Trace($"已装投影 {results.Count} 条，{sw.ElapsedMilliseconds} ms");

        await EnrichRangeAsync(results, 0, results.Count, cancellationToken).ConfigureAwait(false);
        Trace($"已装回填完成，{sw.ElapsedMilliseconds} ms");
        return results;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ModVersionInfo>> GetVersionsAsync(string identifier, CancellationToken cancellationToken = default)
    {
        var reg = Registry;
        var list = new List<ModVersionInfo>();
        if (_instance == null || reg == null || string.IsNullOrWhiteSpace(identifier))
        {
            return Task.FromResult<IReadOnlyList<ModVersionInfo>>(list);
        }

        List<CkanModule> modules;
        try
        {
            modules = reg.AvailableByIdentifier(identifier).ToList();
        }
        catch
        {
            return Task.FromResult<IReadOnlyList<ModVersionInfo>>(list);
        }

        var criteria = _instance.VersionCriteria();
        foreach (var m in modules.OrderByDescending(m => m.version))
        {
            bool compatible;
            try { compatible = m.IsCompatible(criteria); } catch { compatible = true; }

            var urls = m.download?
                .Select(u => u.ToString())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList() ?? new List<string>();

            var gameVersions = new List<string>();
            if (m.ksp_version_min is not null) gameVersions.Add(m.ksp_version_min.ToString());
            if (m.ksp_version_max is not null) gameVersions.Add(m.ksp_version_max.ToString());
            else if (m.ksp_version is not null) gameVersions.Add(m.ksp_version.ToString());
            // 未声明兼容版本时兜底为不限版本，保证版本卡片能进入分组。
            if (gameVersions.Count == 0) gameVersions.Add("*");

            list.Add(new ModVersionInfo(
                m.identifier,
                m.version.ToString(),
                m.release_date?.ToString("yyyy-MM-dd"),
                urls,
                gameVersions,
                compatible));
        }

        return Task.FromResult<IReadOnlyList<ModVersionInfo>>(list);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ModContentEntry>> GetContentsAsync(string identifier, CancellationToken cancellationToken = default)
    {
        var mod = LatestAvailable(identifier);
        var list = new List<ModContentEntry>();
        if (mod == null)
        {
            return Task.FromResult<IReadOnlyList<ModContentEntry>>(list);
        }

        return Task.Run(() =>
        {
            string? cachedZip = null;
            if (_instance != null)
            {
                try
                {
                    using var cache = new NetModuleCache(_instance.DownloadCacheDir);
                    cachedZip = cache.GetCachedFilename(mod);
                }
                catch { /* 缓存不可读时退回描述符 */ }
            }

            // 已缓存：直接枚举安装包内的文件，这才是 CKAN Contents 页的真实内容。
            if (!string.IsNullOrEmpty(cachedZip) && File.Exists(cachedZip!))
            {
                try
                {
                    using var archive = System.IO.Compression.ZipFile.OpenRead(cachedZip!);
                    foreach (var entry in archive.Entries.Where(e => !e.FullName.EndsWith("/")).Take(400))
                    {
                        list.Add(new ModContentEntry(entry.FullName, null));
                    }
                }
                catch { /* 非 zip 或损坏，退回描述符 */ }
            }

            if (list.Count == 0)
            {
                foreach (var stanza in mod.install ?? Array.Empty<ModuleInstallDescriptor>())
                {
                    var source = stanza.file ?? stanza.find ?? "*";
                    list.Add(new ModContentEntry(source, stanza.install_to));
                }
            }
            return (IReadOnlyList<ModContentEntry>)list;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public bool IsCached(string identifier)
    {
        var mod = LatestAvailable(identifier);
        if (mod == null || _instance == null)
        {
            return false;
        }
        try
        {
            using var cache = new NetModuleCache(_instance.DownloadCacheDir);
            return cache.IsCached(mod);
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public Task<ModCacheStats?> GetCacheStatsAsync(CancellationToken cancellationToken = default)
    {
        if (_instance == null)
        {
            return Task.FromResult<ModCacheStats?>(null);
        }
        var cacheDir = _instance.DownloadCacheDir;
        return Task.Run(() =>
        {
            using var cache = new NetModuleCache(cacheDir);
            cache.GetSizeInfo(out var files, out var bytes, out _);
            return (ModCacheStats?)new ModCacheStats(files, bytes, cacheDir);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task ClearCacheAsync(CancellationToken cancellationToken = default)
    {
        if (_instance == null)
        {
            throw new InvalidOperationException("尚未附加 KSP 实例。");
        }
        var cacheDir = _instance.DownloadCacheDir;
        return Task.Run(() =>
        {
            using var cache = new NetModuleCache(cacheDir);
            cache.RemoveAll();
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ModCacheImportResult> ImportCacheFilesAsync(IReadOnlyList<string> filePaths, CancellationToken cancellationToken = default)
    {
        if (_instance == null)
        {
            throw new InvalidOperationException("尚未附加 KSP 实例。");
        }
        var reg = Registry;
        if (reg == null)
        {
            throw new InvalidOperationException("CKAN 注册表尚未就绪。");
        }
        var cacheDir = _instance.DownloadCacheDir;
        return Task.Run(() =>
        {
            var files = new HashSet<FileInfo>();
            var failed = new List<string>();
            foreach (var path in filePaths)
            {
                if (File.Exists(path))
                {
                    files.Add(new FileInfo(path));
                }
                else
                {
                    failed.Add(path);
                }
            }
            if (files.Count == 0)
            {
                return new ModCacheImportResult(0, failed);
            }

            using var cache = new NetModuleCache(cacheDir);
            var ok = ModuleImporter.ImportFiles(files, _user, _ => { }, reg, _instance, cache, allowDelete: false);
            return new ModCacheImportResult(ok ? files.Count : 0, failed);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> ScanUnmanagedAsync(CancellationToken cancellationToken = default)
    {
        if (_instance == null)
        {
            throw new InvalidOperationException("尚未附加 KSP 实例。");
        }
        if (_registryManager == null)
        {
            throw new InstanceReadOnlyException();
        }

        return Task.Run(() => _registryManager.ScanUnmanagedFiles(), cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ModUnmanagedItem>> GetUnmanagedAsync(CancellationToken cancellationToken = default)
    {
        var reg = Registry;
        var list = new List<ModUnmanagedItem>();
        if (reg == null)
        {
            return Task.FromResult<IReadOnlyList<ModUnmanagedItem>>(list);
        }

        return Task.Run(() =>
        {
            foreach (var dllIdentifier in reg.InstalledDlls)
            {
                list.Add(new ModUnmanagedItem(dllIdentifier, reg.DllPath(dllIdentifier), true));
            }
            return (IReadOnlyList<ModUnmanagedItem>)list;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ModOptionalRelations?> GetOptionalRelationsAsync(string identifier, CancellationToken cancellationToken = default)
    {
        var mod = LatestAvailable(identifier);
        if (mod == null)
        {
            return Task.FromResult<ModOptionalRelations?>(null);
        }

        IReadOnlyList<string> Names(IEnumerable<RelationshipDescriptor>? rels)
        {
            if (rels == null)
            {
                return Array.Empty<string>();
            }
            var list = new List<string>();
            foreach (var rel in rels)
            {
                var depId = (rel as ModuleRelationshipDescriptor)?.name;
                if (string.IsNullOrEmpty(depId))
                {
                    continue;
                }
                var display = depId!;
                try
                {
                    var candidate = Registry?.AvailableByIdentifier(display).FirstOrDefault();
                    if (!string.IsNullOrEmpty(candidate?.name))
                    {
                        display = candidate!.name;
                    }
                }
                catch { /* 元数据缺失时保留标识 */ }
                if (!list.Contains(display, StringComparer.OrdinalIgnoreCase))
                {
                    list.Add(display);
                }
            }
            return list;
        }

        return Task.FromResult<ModOptionalRelations?>(new ModOptionalRelations(
            Names(mod.recommends),
            Names(mod.suggests)));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ModAuditItem>> GetAuditRecommendationsAsync(CancellationToken cancellationToken = default)
    {
        var reg = Registry;
        var list = new List<ModAuditItem>();
        if (_instance == null || reg == null)
        {
            return Task.FromResult<IReadOnlyList<ModAuditItem>>(list);
        }

        return Task.Run(() =>
        {
            var installed = reg.Installed();
            var crit = _instance.VersionCriteria();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var installedId in installed.Keys)
            {
                var mod = LatestAvailable(installedId);
                if (mod?.recommends == null)
                {
                    continue;
                }
                foreach (var rel in mod.recommends)
                {
                    var depId = (rel as ModuleRelationshipDescriptor)?.name;
                    if (string.IsNullOrEmpty(depId)
                        || installed.Keys.Any(k => string.Equals(k, depId, StringComparison.OrdinalIgnoreCase))
                        || !seen.Add(depId!))
                    {
                        continue;
                    }
                    var candidate = LatestAvailable(depId!);
                    if (candidate == null)
                    {
                        continue;
                    }
                    bool compatible;
                    try { compatible = candidate.IsCompatible(crit); }
                    catch { compatible = true; }
                    if (!compatible)
                    {
                        continue;
                    }
                    list.Add(new ModAuditItem(depId!, candidate.name ?? depId!, mod.name ?? installedId, false));
                }
            }
            return (IReadOnlyList<ModAuditItem>)list;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ModInstallPlan?> GetInstallPlanAsync(string identifier, bool allowIncompatible = false, IReadOnlyList<string>? providerPicks = null, bool withRecommendations = false, CancellationToken cancellationToken = default)
    {
        if (_instance == null || Registry == null)
        {
            return Task.FromResult<ModInstallPlan?>(null);
        }

        var target = LatestAvailable(identifier);
        if (target == null)
        {
            return Task.FromResult<ModInstallPlan?>(null);
        }

        // 用户已为多提供者依赖做出选择：把所选模块一并作为明确目标代入，
        // 并关闭 TooManyModsProvide 抛错，让解析器按已选模块的 stanza 归一化到唯一候选。
        var picks = ResolvePicks(providerPicks);
        return BuildPlanAsync(target.identifier, new[] { target }.Concat(picks).ToList(), allowIncompatible, picks.Count > 0, withRecommendations);
    }

    /// <inheritdoc />
    public Task<ModInstallPlan?> GetMultiInstallPlanAsync(IReadOnlyList<string> identifiers, bool allowIncompatible = false, IReadOnlyList<string>? providerPicks = null, bool withRecommendations = false, CancellationToken cancellationToken = default)
    {
        if (_instance == null || Registry == null || identifiers is not { Count: > 0 })
        {
            return Task.FromResult<ModInstallPlan?>(null);
        }

        var targets = new List<CkanModule>();
        foreach (var identifier in identifiers)
        {
            var module = LatestAvailable(identifier);
            if (module != null)
            {
                targets.Add(module);
            }
        }
        if (targets.Count == 0)
        {
            return Task.FromResult<ModInstallPlan?>(null);
        }

        var picks = ResolvePicks(providerPicks);
        var label = string.Join(" + ", targets.Select(m => m.identifier));
        return BuildPlanAsync(label, targets.Concat(picks).ToList(), allowIncompatible, picks.Count > 0, withRecommendations);
    }

    /// <summary>把多提供者选择解析为目录中的模块对象。</summary>
    private List<CkanModule> ResolvePicks(IReadOnlyList<string>? providerPicks)
    {
        var picks = new List<CkanModule>();
        if (providerPicks is { Count: > 0 })
        {
            foreach (var pickIdentifier in providerPicks)
            {
                var pick = LatestAvailable(pickIdentifier);
                if (pick != null)
                {
                    picks.Add(pick);
                }
            }
        }
        return picks;
    }

    /// <summary>
    /// 对一组目标模组一次性解析依赖，得到整体的安装计划（CKAN Changeset 的解析口径）。
    /// </summary>
    private Task<ModInstallPlan?> BuildPlanAsync(string label, IReadOnlyList<CkanModule> targets, bool allowIncompatible, bool hasProviderPicks, bool withRecommendations)
    {
        var reg = Registry!;
        try
        {
            var opts = RelationshipResolverOptions.DependsOnlyOpts(_instance.StabilityToleranceConfig);
            if (allowIncompatible)
            {
                opts.allow_incompatible = true;
            }
            if (hasProviderPicks)
            {
                opts.without_toomanyprovides_kraken = true;
            }
            // CKAN 默认把推荐的模组一并安装；仅在用户确认后开启，避免静默多装。
            opts.with_recommends = withRecommendations;
            var resolver = new RelationshipResolver(
                modulesToInstall: targets.ToArray(),
                modulesToRemove: null,
                options: opts,
                registry: reg,
                game: _instance.Game,
                versionCrit: _instance.VersionCriteria());

            var installed = reg.Installed();
            var toInstall = new List<string>();
            var toUpgrade = new List<string>();
            var resolvedIds = new List<string>();

            foreach (var mod in resolver.ModList())
            {
                resolvedIds.Add(mod.identifier);
                if (installed.TryGetValue(mod.identifier, out var current))
                {
                    if (mod.version.CompareTo(current) > 0)
                    {
                        toUpgrade.Add(mod.identifier);
                    }
                }
                else
                {
                    toInstall.Add(mod.identifier);
                }
            }

            // CKAN 的 replaced_by 语义：已装模组声明被本次计划中的某个模组替换时，安装前先卸掉旧模组。
            var toRemove = new List<string>();
            foreach (var installedId in installed.Keys)
            {
                if (resolvedIds.Contains(installedId, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }
                try
                {
                    var replacement = reg.GetReplacement(installedId, _instance.StabilityToleranceConfig, _instance.VersionCriteria());
                    if (replacement?.ReplaceWith != null &&
                        resolvedIds.Contains(replacement.ReplaceWith.identifier, StringComparer.OrdinalIgnoreCase))
                    {
                        toRemove.Add(installedId);
                    }
                }
                catch { /* 缺元数据的已装项跳过 */ }
            }

            return Task.FromResult<ModInstallPlan?>(new ModInstallPlan(
                label,
                toInstall,
                toUpgrade,
                toRemove,
                AllowIncompatible: allowIncompatible));
        }
        catch (TooManyModsProvideKraken tooMany)
        {
            var options = tooMany.modules
                .Select(m => new ModProviderOption(m.identifier, m.name ?? m.identifier, m.version?.ToString()))
                .ToList();
            return Task.FromResult<ModInstallPlan?>(new ModInstallPlan(
                label,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                AllowIncompatible: allowIncompatible,
                ProviderChoices: new[]
                {
                    new ModProviderChoice(tooMany.requested, tooMany.requester?.name, tooMany.choice_help_text, options)
                }));
        }
        catch (Exception ex)
        {
            var reason = ex switch
            {
                InconsistentKraken => $"与已安装模组冲突：{ex.Message}",
                DependenciesNotSatisfiedKraken => $"依赖在当前 KSP 版本无可安装版本：{ex.Message}",
                _ => $"无法生成安装计划：{ex.Message}"
            };
            return Task.FromResult<ModInstallPlan?>(new ModInstallPlan(
                label,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                reason,
                AllowIncompatible: allowIncompatible));
        }
    }

    /// <inheritdoc />
    public Task ExecutePlanAsync(ModInstallPlan plan, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (_instance == null)
        {
            throw new InvalidOperationException("尚未附加 KSP 实例。");
        }
        if (_registryManager == null)
        {
            throw new InstanceReadOnlyException();
        }

        // CKAN 的安装/卸载是同步阻塞调用，必须移到后台线程，否则批量更新会卡死 UI 线程。
        return Task.Run(() =>
        {
            var opts = RelationshipResolverOptions.DependsOnlyOpts(_instance.StabilityToleranceConfig);
            opts.allow_incompatible = plan.AllowIncompatible;
            Directory.CreateDirectory(_instance.DownloadCacheDir);
            var cache = new NetModuleCache(_instance.DownloadCacheDir);
            var installer = new ModuleInstaller(_instance, cache, _config, _user, cancellationToken);

            var desired = plan.ToInstall.Concat(plan.ToUpgrade)
                .Select(LatestAvailable)
                .Where(m => m != null)
                .Select(m => m!)
                .ToList();

            if (desired.Count > 0)
            {
                HashSet<string>? possibleConfigOnlyDirs = null;

                // 部分模组元数据缺 download_size / install_size，CKAN 的进度分母会是 0（百分比恒 0），
                // 这里按目录里的模块大小估一个总量，交给 StringBuilderUser 换算百分比。
                Log.ExpectedBytes = desired.Sum(m => Math.Max(0L, m.download_size) + Math.Max(0L, m.install_size));
                var accelerated = ApplyGitHubAcceleration(desired);
                try
                {
                    try
                    {
                        installer.InstallList(desired, opts, _registryManager, ref possibleConfigOnlyDirs,
                            userAgent: UserAgent, ConfirmPrompt: false);
                    }
                    catch when (accelerated)
                    {
                        // 加速前缀失效时回退到原始地址重试一次，避免卡在下载失败。
                        RestoreGitHubAcceleration();
                        installer.InstallList(desired, opts, _registryManager, ref possibleConfigOnlyDirs,
                            userAgent: UserAgent, ConfirmPrompt: false);
                    }
                }
                finally
                {
                    RestoreGitHubAcceleration();
                    Log.ExpectedBytes = 0;
                }
            }

            if (plan.ToRemove.Count > 0)
            {
                // replaced_by 替换场景：必须先卸载旧模组，再装新模组，避免同一批文件被覆盖后残留。
                HashSet<string>? possibleConfigOnlyDirs = null;
                installer.UninstallList(plan.ToRemove, ref possibleConfigOnlyDirs, _registryManager, ConfirmPrompt: false);
                progress?.Report($"已卸载被替换的 {plan.ToRemove.Count} 个模组。");
            }

            _registryManager.Save();

            progress?.Report($"Plan for '{plan.Target}' applied.");
        }, cancellationToken);
    }

    /// <summary>把 github.com 的下载地址改写成「加速前缀 + 原地址」，返回是否改写了任何模组。</summary>
    private bool ApplyGitHubAcceleration(System.Collections.Generic.IEnumerable<CkanModule> mods)
    {
        _accelOriginals.Clear();
        if (!GitHubAccelerationEnabled)
        {
            return false;
        }
        var prefix = GitHubAccelerationPrefix?.Trim() ?? "";
        if (prefix.Length == 0)
        {
            return false;
        }
        if (!prefix.EndsWith("/", System.StringComparison.Ordinal))
        {
            prefix += "/";
        }
        foreach (var mod in mods)
        {
            if (mod.download == null || mod.download.Count == 0)
            {
                continue;
            }
            var copy = new System.Collections.Generic.List<Uri>(mod.download.Count);
            var changed = false;
            foreach (var u in mod.download)
            {
                if (string.Equals(u.Scheme, "https", System.StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(u.Host, "github.com", System.StringComparison.OrdinalIgnoreCase))
                {
                    copy.Add(new Uri(prefix + u.AbsoluteUri));
                    changed = true;
                }
                else
                {
                    copy.Add(u);
                }
            }
            if (!changed)
            {
                continue;
            }
            _accelOriginals[mod] = mod.download;
            mod.download = copy;
        }
        return _accelOriginals.Count > 0;
    }

    /// <summary>恢复被改写的下载地址。</summary>
    private void RestoreGitHubAcceleration()
    {
        foreach (var pair in _accelOriginals)
        {
            pair.Key.download = pair.Value;
        }
        _accelOriginals.Clear();
    }

    /// <inheritdoc />
    public Task UninstallAsync(string identifier, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (_instance == null)
        {
            throw new InvalidOperationException("尚未附加 KSP 实例。");
        }
        if (_registryManager == null)
        {
            throw new InstanceReadOnlyException();
        }

        return Task.Run(() =>
        {
            Directory.CreateDirectory(_instance.DownloadCacheDir);
            var cache = new NetModuleCache(_instance.DownloadCacheDir);
            var installer = new ModuleInstaller(_instance, cache, _config, _user, cancellationToken);

            HashSet<string>? possibleConfigOnlyDirs = null;
            installer.UninstallList(new[] { identifier }, ref possibleConfigOnlyDirs, _registryManager, ConfirmPrompt: false);

            _registryManager.Save();
            progress?.Report($"Uninstalled '{identifier}'.");
        }, cancellationToken);
    }

    // ---- internals -----------------------------------------------------------

    private static void AddRel(Dictionary<string, List<string>> map, string key, string display)
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = new List<string>();
            map[key] = list;
        }
        if (!list.Contains(display, StringComparer.OrdinalIgnoreCase))
            list.Add(display);
    }

    /// <summary>按仓库地址去重：Repository 字典以 uri 为判重键，重复地址会导致仓库数据缓存字典撞 key。</summary>
    private static List<CKAN.Repository> DistinctRepos(IEnumerable<CKAN.Repository>? repos)
        => repos?.GroupBy(r => r.uri?.ToString() ?? r.name)
                 .Select(g => g.First())
                 .ToList() ?? new List<CKAN.Repository>();

    /// <summary>在门控内枚举全量目录，串行访问 CKAN Core，避免并发刷新/搜索撞内部仓库字典。</summary>
    private async Task<List<CkanModule>> EnumerateCatalogLockedAsync(CancellationToken cancellationToken)
    {
        Trace("Enum enter (waiting gate)");
        await _catalogGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var enumList = await Task.Run(() =>
            {
                lock (_repoLock)
                {
                    var list = EnumerateCatalog().ToList();
                    Trace("Enum ToList done");
                    return list;
                }
            }, cancellationToken).ConfigureAwait(false);
            return enumList;
        }
        finally
        {
            _catalogGate.Release();
        }
    }

    private IEnumerable<CkanModule> EnumerateCatalog()
    {
        var reg = Registry;
        if (_instance == null || reg == null)
        {
            yield break;
        }

        var repos = DistinctRepos(reg.Repositories.Values);
        var byId = new Dictionary<string, CkanModule>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var avail in _repoData.GetAllAvailableModules(repos))
        {
            CkanModule? best = null;
            foreach (var m in avail.AllAvailable())
            {
                if (best == null || m.version.CompareTo(best.version) > 0)
                {
                    best = m;
                }
            }

            if (best == null)
            {
                continue;
            }

            if (!byId.TryGetValue(best.identifier, out var existing) ||
                best.version.CompareTo(existing.version) > 0)
            {
                byId[best.identifier] = best;
            }
        }

        foreach (var mod in byId.Values)
        {
            yield return mod;
        }
    }

    private CkanModule? LatestAvailable(string identifier)
    {
        var reg = Registry;
        if (reg == null)
        {
            return null;
        }

        try
        {
            CkanModule? best = null;
            foreach (var m in reg.AvailableByIdentifier(identifier))
            {
                if (best == null || m.version.CompareTo(best.version) > 0)
                {
                    best = m;
                }
            }

            return best;
        }
        catch
        {
            // Installed entries without repo metadata (DLL-provided, redistributables, etc.)
            // make CKAN throw ModuleNotFoundKraken; treat them as "not in catalog".
            return null;
        }
    }

    private static bool MatchesQuery(CkanModule mod, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var q = query.Trim();
        return mod.identifier.Contains(q, StringComparison.OrdinalIgnoreCase)
            || mod.name.Contains(q, StringComparison.OrdinalIgnoreCase)
            || (mod.author != null && mod.author.Any(a => a.Contains(q, StringComparison.OrdinalIgnoreCase)));
    }

    private ModSearchResult ToResult(CkanModule? mod)
    {
        var installed = Registry?.Installed() ?? new Dictionary<string, ModuleVersion>();
        return ToResult(mod, mod?.identifier ?? string.Empty, installed);
    }

    private ModSearchResult ToResult(CkanModule? mod, string identifier, IReadOnlyDictionary<string, ModuleVersion> installed)
    {
        bool isInstalled = installed.TryGetValue(identifier, out var installedVersion);
        bool hasUpdate = isInstalled && mod != null && mod.version.CompareTo(installedVersion) > 0;

        bool compatible = true;
        if (mod != null && _instance != null)
        {
            try { compatible = mod.IsCompatible(_instance.VersionCriteria()); }
            catch { /* leave default */ }
        }

        // CKAN 模块元数据不含图标 URL（ResourcesDescriptor 无 icon 字段），图标留待 SpaceDock 兜底。
        string? iconUrl = null;

        string? maxKsp = null;
        if (mod != null)
        {
            try { maxKsp = mod.LatestCompatibleGameVersion().ToString(); }
            catch { /* 元数据缺版本范围时不展示 */ }
        }

        // CKAN 的 Replaceable 状态：已装模组声明 replaced_by 且替换者与当前 KSP 兼容时，提示可替换。
        string? replacedById = null;
        if (isInstalled && _instance != null && Registry != null)
        {
            try
            {
                replacedById = Registry.GetReplacement(identifier, _instance.StabilityToleranceConfig, _instance.VersionCriteria())
                    ?.ReplaceWith?.identifier;
            }
            catch { /* 缺元数据时不展示 */ }
        }

        bool isCached = false;
        if (mod != null && _instance != null)
        {
            try
            {
                using var cache = new NetModuleCache(_instance.DownloadCacheDir);
                isCached = cache.IsCached(mod);
            }
            catch { /* 缓存目录不可用时视为未缓存 */ }
        }

        return new ModSearchResult(
            identifier,
            mod?.name ?? identifier,
            mod?.description ?? mod?.@abstract,
            mod?.author is { Count: > 0 } a ? string.Join(", ", a) : null,
            mod?.license is { Count: > 0 } l ? string.Join(", ", l) : null,
            mod?.version?.ToString() ?? installedVersion?.ToString(),
            mod?.resources?.homepage?.ToString(),
            isInstalled,
            hasUpdate,
            compatible,
            iconUrl,
            ReleaseDate: mod?.release_date,
            MaxKsp: maxKsp,
            ReplacedById: replacedById,
            IsCached: isCached,
            Tags: mod?.Tags?.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <inheritdoc />
    public (IReadOnlyList<string> reverseDeps, IReadOnlyList<string> conflicts) GetReverseAndConflicts(string identifier)
    {
        var deps = new List<string>();
        var conflicts = new List<string>();
        if (_instance == null) return (deps, conflicts);
        try
        {
            var repos = DistinctRepos(Registry?.Repositories?.Values ?? Enumerable.Empty<CKAN.Repository>());
            foreach (var am in _repoData.GetAllAvailableModules(repos))
            {
                foreach (var mod in am.AllAvailable())
                {
                    if (mod.identifier == identifier) continue;
                    if (mod.depends?.Any(r => (r as CKAN.ModuleRelationshipDescriptor)?.name == identifier) == true)
                        deps.Add(mod.name ?? mod.identifier);
                    if (mod.conflicts?.Any(r => (r as CKAN.ModuleRelationshipDescriptor)?.name == identifier) == true)
                        conflicts.Add(mod.name ?? mod.identifier);
                }
            }
        }
        catch { /* ignore */ }
        return (deps.Distinct().ToList(), conflicts.Distinct().ToList());
    }

    public void Dispose()
    {
        try { _registryManager?.Dispose(); } catch { /* best effort */ }
        try { _manager.Dispose(); } catch { /* best effort */ }
    }
}
