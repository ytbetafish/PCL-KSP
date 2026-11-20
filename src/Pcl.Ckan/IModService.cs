namespace Pcl.Ckan;

/// <summary>
/// High-level mod-management surface built on top of CKAN.Core.
/// </summary>
public interface IModService
{
    /// <summary>
    /// Refresh the master mod registry from the configured CKAN repositories (network).
    /// Safe to call even when offline: failures are swallowed and the cached registry is kept.
    /// </summary>
    Task RefreshMasterRegistryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Search the available mod catalog by identifier / name / author substring.
    /// </summary>
    Task<IReadOnlyList<ModSearchResult>> SearchAsync(string? query, CancellationToken cancellationToken = default);

    /// <summary>
    /// 后台预热全量目录（空查询搜索结果）并缓存；重复调用复用同一任务。
    /// 失败静默忽略，不阻断启动；下载页打开时优先复用该缓存。
    /// </summary>
    Task WarmUpAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 对结果列表 [start, start+count) 区间做 SpaceDock 联网回填（图标、下载量、作者等），
    /// 原地写回列表；失败项保持原值。调用方可缓存已回填区间，避免重复网络请求。
    /// </summary>
    Task EnrichRangeAsync(IList<ModSearchResult> results, int start, int count, CancellationToken cancellationToken = default);

    /// <summary>
    /// CKAN 报告的下载/安装进度（消息原文 + 0-100 百分比），供任务管理页展示。
    /// 没有进度时不会触发。
    /// </summary>
    event Action<string, int>? ProgressReported;

    /// <summary>CKAN 报告的下载速率（字节/秒），供任务管理页左边栏显示。</summary>
    event Action<long>? SpeedReported;

    /// <summary>最近一次 CKAN 下载速率（字节/秒）。</summary>
    long CurrentSpeed { get; }
    /// <summary>
    /// List mods currently installed in the attached KSP instance.
    /// </summary>
    Task<IReadOnlyList<ModSearchResult>> ListInstalledAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// List every published version of a mod, newest first.
    /// </summary>
    Task<IReadOnlyList<ModVersionInfo>> GetVersionsAsync(string identifier, CancellationToken cancellationToken = default);

    /// <summary>
    /// Use CKAN's dependency resolver to compute the install/upgrade/remove plan for a mod.
    /// Returns null if the instance has no registry or the identifier is unknown.
    /// </summary>
    /// <param name="providerPicks">用户为多提供者依赖选择的候选模组标识；解析结果 <see cref="ModInstallPlan.ProviderChoices"/> 非空时带上选择重试。</param>
    /// <param name="withRecommendations">是否把模组声明的 recommends 一并纳入计划（CKAN 默认行为）。</param>
    Task<ModInstallPlan?> GetInstallPlanAsync(string identifier, bool allowIncompatible = false, IReadOnlyList<string>? providerPicks = null, bool withRecommendations = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// 扫描 GameData 中的未托管 DLL 并更新注册表（CKAN 的 scan）；返回是否发现变化。
    /// 只读实例会抛 <see cref="InstanceReadOnlyException"/>。
    /// </summary>
    Task<bool> ScanUnmanagedAsync(CancellationToken cancellationToken = default);

    /// <summary>列出已扫描登记的未托管 DLL（AD，autodetected）。</summary>
    Task<IReadOnlyList<ModUnmanagedItem>> GetUnmanagedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 模组会安装哪些内容（CKAN 的 Contents 页）：已缓存时枚举安装包内的文件列表，
    /// 未缓存时退回元数据里的安装描述符。
    /// </summary>
    Task<IReadOnlyList<ModContentEntry>> GetContentsAsync(string identifier, CancellationToken cancellationToken = default);

    /// <summary>该模组的最新版本安装包是否已在下载缓存中（CKAN 的 Cached 状态）。</summary>
    bool IsCached(string identifier);

    /// <summary>下载缓存统计（文件数、总字节数、缓存目录）。</summary>
    Task<ModCacheStats?> GetCacheStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>清空下载缓存（CKAN 的 Clear cache）。</summary>
    Task ClearCacheAsync(CancellationToken cancellationToken = default);

    /// <summary>把用户选择的 .zip / .ckan 文件导入下载缓存（CKAN 的 Import downloaded mods）。</summary>
    Task<ModCacheImportResult> ImportCacheFilesAsync(IReadOnlyList<string> filePaths, CancellationToken cancellationToken = default);

    /// <summary>
    /// 对多个模组一次性解析依赖，得到一个整体计划（CKAN Changeset 语义：
    /// 勾选一批模组 → 统一解析与冲突检查 → 一次性执行）。
    /// </summary>
    Task<ModInstallPlan?> GetMultiInstallPlanAsync(IReadOnlyList<string> identifiers, bool allowIncompatible = false, IReadOnlyList<string>? providerPicks = null, bool withRecommendations = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取模组的 recommends / suggests 关系（显示名列表）；标识未知时返回 null。
    /// </summary>
    Task<ModOptionalRelations?> GetOptionalRelationsAsync(string identifier, CancellationToken cancellationToken = default);

    /// <summary>
    /// Audit recommendations：汇总已装模组推荐但尚未安装的模组（仅列与当前 KSP 兼容者）。
    /// </summary>
    Task<IReadOnlyList<ModAuditItem>> GetAuditRecommendationsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute a previously computed plan (downloads + installs/upgrades/removes).
    /// </summary>
    Task ExecutePlanAsync(ModInstallPlan plan, IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Uninstall an installed mod (plus any orphaned dependencies).
    /// </summary>
    Task UninstallAsync(string identifier, IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find mods that depend on (or conflict with) the given identifier.
    /// Returns (reverseDeps, conflicts) lists of mod identifiers.
    /// </summary>
    (IReadOnlyList<string> reverseDeps, IReadOnlyList<string> conflicts) GetReverseAndConflicts(string identifier);
}
