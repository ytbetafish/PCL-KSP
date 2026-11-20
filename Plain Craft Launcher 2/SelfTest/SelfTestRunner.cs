using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using PCL.Translation;
using Pcl.Ckan;

namespace PCL.SelfTest;

/// <summary>
///     启动器内置自测引擎：由环境变量 PCL_SELFTEST=&lt;分组&gt; 触发，按分组跑功能链路并写运行日志。
///     每个用例输出 `[SelfTest] PASS/FAIL/SKIP 名称 | 详情 | 耗时`，最后输出汇总行。
///     分组：translate（翻译）、instance（实例探测）、settings（设置读写）。
/// </summary>
public static class SelfTestRunner
{
    private static readonly StringBuilder Report = new();
    private static int _pass;
    private static int _fail;
    private static int _skip;

    public static async Task RunAsync(string groups)
    {
        var want = (groups ?? "").Trim().ToLowerInvariant();
        Report.Clear();
        _pass = _fail = _skip = 0;
        Write($"========== 自测开始 分组=[{want}] {DateTime.Now:HH:mm:ss} ==========");

        if (want is "all" or "translate" or "") await GroupTranslateAsync();
        if (want is "all" or "instance" or "") await GroupInstanceAsync();
        if (want is "all" or "settings" or "") await GroupSettingsAsync();
        if (want is "all" or "ckan") await GroupCkanAsync();
        if (want is "all" or "misc") await GroupMiscAsync();
        if (want is "all" or "live") await GroupLiveAsync();

        Write($"========== 自测结束 PASS={_pass} FAIL={_fail} SKIP={_skip} {DateTime.Now:HH:mm:ss} ==========");
        Flush();
    }

    #region 用例执行框架

    /// <summary>单用例默认超时（秒）：到点直接判 FAIL，避免任一挂起点拖死整轮回归。可用 PCL_SELFTEST_TIMEOUT 覆盖。</summary>
    private static int CaseTimeoutSeconds
    {
        get
        {
            var raw = Environment.GetEnvironmentVariable("PCL_SELFTEST_TIMEOUT");
            return int.TryParse(raw, out var v) && v >= 5 ? v : 120;
        }
    }

    private static async Task CaseAsync(string name, Func<Task<string?>> body)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var work = Task.Run(body);
            var timeout = CaseTimeoutSeconds;
            var first = await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(timeout))).ConfigureAwait(false);
            if (first != work)
            {
                _fail++;
                Write($"FAIL {name} | Timeout: {timeout}s 未返回（疑似阻塞，已跳过继续后续用例） | {sw.ElapsedMilliseconds}ms");
                return;
            }
            var detail = await work.ConfigureAwait(false);
            sw.Stop();
            _pass++;
            Write($"PASS {name} | {detail ?? "ok"} | {sw.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            sw.Stop();
            _fail++;
            Write($"FAIL {name} | {ex.GetType().Name}: {ex.Message} | {sw.ElapsedMilliseconds}ms");
        }
    }

    private static Task CaseAsync(string name, Func<string?> body)
    {
        return CaseAsync(name, () => Task.FromResult(body()));
    }

    private static async Task CaseSkipAsync(string name, string reason)
    {
        _skip++;
        Write($"SKIP {name} | {reason}");
        await Task.CompletedTask;
    }

    private static void Write(string line)
    {
        Report.AppendLine(line);
        Common.Log("[SelfTest] " + line);
    }

    private static void Flush()
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCL-KSP");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "selftest-last.log"), Report.ToString(), new UTF8Encoding(false));
        }
        catch
        {
            // 写盘失败不影响运行日志里的结论
        }
    }

    #endregion

    #region 翻译

    private const string EnSample =
        "This mod adds a suite of science experiments and a new probe core for early-game exploration.";

    private static async Task GroupTranslateAsync()
    {
        Write("---------- 分组：翻译 ----------");
        var free = new FreeTranslationService();

        await CaseAsync("translate/free-en-zh", async () =>
        {
            var r = await free.TranslateAsync(EnSample, "zh-CN");
            if (string.IsNullOrWhiteSpace(r)) throw new InvalidOperationException("返回空译文");
            if (!HasCjk(r)) throw new InvalidOperationException("译文不含中文：" + r);
            return "→ " + r;
        });

        await CaseAsync("translate/free-zh-en", async () =>
        {
            var r = await free.TranslateAsync("这个模组增加了一套科学实验和一个新的探测核心。", "en");
            if (string.IsNullOrWhiteSpace(r)) throw new InvalidOperationException("返回空译文");
            if (HasCjk(r)) throw new InvalidOperationException("译文仍是中文：" + r);
            return "→ " + r;
        });

        await CaseAsync("translate/long-text-chunking", async () =>
        {
            var sb = new StringBuilder();
            for (var i = 1; i <= 12; i++)
                sb.Append($"Paragraph {i}: this mod adds science experiments, new parts and contracts for early game. ");
            var text = sb.ToString();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var r = await free.TranslateAsync(text, "zh-CN");
            sw.Stop();
            if (string.IsNullOrWhiteSpace(r)) throw new InvalidOperationException("长文本返回空译文");
            if (!HasCjk(r)) throw new InvalidOperationException("长文本译文不含中文");
            return $"输入 {text.Length} 字符，输出 {r.Length} 字符，{sw.ElapsedMilliseconds}ms";
        });

        await CaseAsync("translate/factory-empty-key-uses-free", () =>
        {
            var svc = TranslationServiceFactory.Create(new TranslationOptions { ApiKey = "" });
            if (svc is not FreeTranslationService)
                throw new InvalidOperationException("未填密钥时未走免费实现：" + svc.GetType().Name);
            return "类型 FreeTranslationService";
        });

        await CaseAsync("translate/factory-with-key-uses-openai", () =>
        {
            var svc = TranslationServiceFactory.Create(new TranslationOptions { ApiKey = "sk-selftest-fake" });
            if (svc is not OpenAiTranslationService)
                throw new InvalidOperationException("填了密钥却未走 OpenAI 兼容实现：" + svc.GetType().Name);
            return "类型 OpenAiTranslationService";
        });

        await CaseAsync("translate/openai-empty-key-returns-null", async () =>
        {
            var svc = new OpenAiTranslationService(new TranslationOptions { ApiKey = "" });
            var r = await svc.TranslateAsync(EnSample, "zh-CN");
            if (r is not null) throw new InvalidOperationException("空密钥本应返回 null，实际：" + r);
            return "按预期返回 null";
        });
    }

    private static bool HasCjk(string s)
    {
        foreach (var ch in s)
            if (ch >= '\u4e00' && ch <= '\u9fff')
                return true;
        return false;
    }

    #endregion

    #region 实例

    private static async Task GroupInstanceAsync()
    {
        Write("---------- 分组：实例 ----------");
        var probe = new KspInstanceService();
        var configured = KspSettings.Current.KspRootPath;

        await CaseAsync("instance/find-candidates", () =>
        {
            var roots = probe.FindCandidateRoots();
            var joined = string.Join("; ", roots);
            return Task.FromResult($"候选 {roots.Count} 个：{joined}");
        });

        await CaseAsync("instance/probe-configured-root", () =>
        {
            if (string.IsNullOrWhiteSpace(configured))
                throw new InvalidOperationException("设置里没有配置 KSP 根目录");
            var info = probe.Probe(configured);
            if (info is null) throw new InvalidOperationException("已配置的目录探测失败：" + configured);
            if (string.IsNullOrWhiteSpace(info.Version))
                throw new InvalidOperationException("探测到实例但版本为空");
            return Task.FromResult($"{info.RootPath} 版本={info.Version}");
        });

        await CaseAsync("instance/probe-non-ksp-returns-null", () =>
        {
            var info = probe.Probe(@"C:\Windows");
            if (info is not null) throw new InvalidOperationException("非 KSP 目录竟被识别为实例：" + info.RootPath);
            return Task.FromResult(@"C:\Windows 正确判定为非实例");
        });

        await CaseAsync("instance/buildid64-readable", () =>
        {
            var file = Path.Combine(configured, "buildID64.txt");
            if (!File.Exists(file)) throw new InvalidOperationException("buildID64.txt 不存在：" + file);
            var line = "";
            foreach (var l in File.ReadLines(file))
            {
                if (l.IndexOf("build id =", StringComparison.OrdinalIgnoreCase) >= 0) { line = l.Trim(); break; }
            }
            if (line.Length == 0) throw new InvalidOperationException("buildID64.txt 里没有 build id 行");
            return Task.FromResult(line);
        });

        await CaseAsync("instance/display-name-roundtrip", () =>
        {
            const string fake = @"X:\__selftest__\Kerbal Space Program";
            var before = KspSettings.GetDisplayName(fake);
            KspSettings.SetDisplayName(fake, "自测实例名");
            var renamed = KspSettings.GetDisplayName(fake);
            if (renamed != "自测实例名") throw new InvalidOperationException("改名未生效：" + renamed);
            KspSettings.SetDisplayName(fake, null);
            var restored = KspSettings.GetDisplayName(fake);
            if (restored != "Kerbal Space Program") throw new InvalidOperationException("恢复目录名失败：" + restored);
            return Task.FromResult($"首次={before}，改名={renamed}，复原={restored}");
        });
    }

    #endregion

    #region 设置

    private static async Task GroupSettingsAsync()
    {
        Write("---------- 分组：设置 ----------");

        await CaseAsync("settings/read-current", () =>
            Task.FromResult(
                $"KspRoot={KspSettings.Current.KspRootPath}；策略={KspSettings.Current.CkanRepoStrategy}；" +
                $"镜像={KspSettings.Current.CkanMirrorUrls.Count} 个；主页模式={KspSettings.Current.HomePageMode}；" +
                $"API密钥={(string.IsNullOrWhiteSpace(KspSettings.Current.TranslateApiKey) ? "未填（走免费）" : "已填")}"));

        await CaseAsync("settings/save-and-parse-back", () =>
            Task.FromResult(ProbeSettingsFile()));
    }

    private static string ProbeSettingsFile()
    {
        KspSettings.Save();
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PCL-KSP", "pclksp-settings.json");
        if (!File.Exists(path)) throw new InvalidOperationException("保存后文件不存在：" + path);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        var savedRoot = root.TryGetProperty("KspRootPath", out var r) ? r.GetString() : null;
        if (savedRoot != KspSettings.Current.KspRootPath)
            throw new InvalidOperationException($"写盘内容与内存不一致：{savedRoot} ≠ {KspSettings.Current.KspRootPath}");
        var size = new FileInfo(path).Length;
        return $"已写盘 {size} 字节，KspRootPath 一致：{savedRoot}";
    }

    #endregion

    #region CKAN

    private static async Task GroupCkanAsync()
    {
        Write("---------- 分组：CKAN（只读） ----------");
        var svc = PCL.Launcher.Ckan;
        string sampleId = "";
        string secondId = "";

        await CaseAsync("ckan/attach-instance", () =>
        {
            if (svc is not ModService ms) throw new InvalidOperationException("服务类型不是 ModService");
            var inst = ms.CurrentInstance;
            if (inst is null) throw new InvalidOperationException("未附加任何 KSP 实例");
            return $"实例={inst.GameDir}，版本={inst.Version()}，名称={inst.Name}";
        });

        await CaseAsync("ckan/catalog-full-count", async () =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var all = await svc.SearchAsync(null);
            sw.Stop();
            if (all.Count < 500) throw new InvalidOperationException("目录条目过少：" + all.Count);
            var compatible = all.Count(m => m.IsCompatible);
            var tagged = all.Count(m => m.Tags is { Count: > 0 });
            var replaced = all.Count(m => !string.IsNullOrEmpty(m.ReplacedById));
            var installed = all.Count(m => m.IsInstalled);
            var cached = all.Count(m => m.IsCached);
            sampleId = (all.FirstOrDefault(m => m.IsCompatible && m.Tags is { Count: > 0 })?.Identifier)
                       ?? all[0].Identifier;
            secondId = all.FirstOrDefault(m => m.IsCompatible && m.Identifier != sampleId)?.Identifier ?? "";
            return $"共 {all.Count} 条（{sw.ElapsedMilliseconds}ms）；兼容 {compatible}；带标签 {tagged}；" +
                   $"被替换 {replaced}；已装 {installed}；已缓存 {cached}";
        });

        await CaseAsync("ckan/search-keyword", async () =>
        {
            var hit = await svc.SearchAsync("science");
            if (hit.Count == 0) throw new InvalidOperationException("搜索 science 无结果");
            return $"命中 {hit.Count} 条，首条={hit[0].Name}（{hit[0].Identifier}）";
        });

        await CaseAsync("ckan/search-case-insensitive", async () =>
        {
            var lower = await svc.SearchAsync("science");
            var upper = await svc.SearchAsync("SCIENCE");
            if (upper.Count != lower.Count)
                throw new InvalidOperationException($"大小写结果不一致：{lower.Count} vs {upper.Count}");
            return $"大小写均 {upper.Count} 条";
        });

        await CaseAsync("ckan/installed-list", async () =>
        {
            var list = await svc.ListInstalledAsync();
            var managed = list.Count(m => !m.IsAutodetected);
            var auto = list.Count(m => m.IsAutodetected);
            var names = string.Join(", ", list.Take(5).Select(m => m.Identifier));
            return $"已装 {list.Count} 个（CKAN 管理 {managed}，扫描识别 {auto}）；样例：{names}";
        });

        await CaseAsync("ckan/versions", async () =>
        {
            if (sampleId.Length == 0) throw new InvalidOperationException("没有取到样例模组标识");
            var vs = await svc.GetVersionsAsync(sampleId);
            if (vs.Count == 0) throw new InvalidOperationException($"{sampleId} 无版本列表");
            var compat = vs.Count(v => v.IsCompatible);
            return $"{sampleId} 共 {vs.Count} 个版本（兼容 {compat}），最新={vs[0].Version}";
        });

        await CaseAsync("ckan/install-plan", async () =>
        {
            if (sampleId.Length == 0) throw new InvalidOperationException("没有取到样例模组标识");
            var plan = await svc.GetInstallPlanAsync(sampleId);
            if (plan is null) throw new InvalidOperationException("解析器返回 null（实例无注册表或标识未知）");
            if (plan.FailureReason is not null)
                throw new InvalidOperationException("解析失败：" + plan.FailureReason);
            if (plan.ProviderChoices is { Count: > 0 })
                return $"需选提供者 {plan.ProviderChoices.Count} 个（{plan.ProviderChoices[0].Requested}）";
            return $"{sampleId} → 安装 {plan.ToInstall.Count}、升级 {plan.ToUpgrade.Count}、移除 {plan.ToRemove.Count}";
        });

        await CaseAsync("ckan/plan-with-recommendations", async () =>
        {
            var plain = await svc.GetInstallPlanAsync(sampleId, false, null, false);
            var withRec = await svc.GetInstallPlanAsync(sampleId, false, null, true);
            if (plain is null || withRec is null) throw new InvalidOperationException("计划为空");
            return $"普通={plain.ToInstall.Count} 项；含推荐={withRec.ToInstall.Count} 项" +
                   (withRec.ToInstall.Count >= plain.ToInstall.Count ? "（推荐未减少项数）" : "（推荐反而减少，异常）");
        });

        await CaseAsync("ckan/contents", async () =>
        {
            var c = await svc.GetContentsAsync(sampleId);
            return $"{sampleId} 内容条目 {c.Count} 个" +
                   (c.Count > 0 ? "，首条=" + c[0].Path : "（未缓存安装包，退回元数据描述符为空）");
        });

        await CaseAsync("ckan/cache-stats", async () =>
        {
            var stats = await svc.GetCacheStatsAsync();
            if (stats is null) throw new InvalidOperationException("缓存统计返回 null");
            return $"{stats.FileCount} 个文件，{stats.TotalBytes / 1024} KB，目录={stats.CachePath}";
        });

        await CaseAsync("ckan/is-cached", () =>
        {
            var cached = svc.IsCached(sampleId);
            return $"{sampleId} 缓存命中={cached}";
        });

        await CaseAsync("ckan/orphan-dependencies", async () =>
        {
            var orphans = await svc.GetOrphanDependenciesAsync();
            return $"可清理孤儿依赖 {orphans.Count} 个" +
                   (orphans.Count > 0 ? "：" + string.Join(", ", orphans.Take(5).Select(o => o.Identifier)) : "");
        });

        await CaseAsync("ckan/conflict-pairs", async () =>
        {
            var pairs = await svc.GetConflictPairsAsync();
            return $"已装模组内部冲突 {pairs.Count} 对" +
                   (pairs.Count > 0 ? "：" + pairs[0].Name + " ↔ " + pairs[0].ConflictsWithName : "");
        });

        await CaseAsync("ckan/audit-recommendations", async () =>
        {
            var rec = await svc.GetAuditRecommendationsAsync();
            return $"推荐未装 {rec.Count} 条" +
                   (rec.Count > 0 ? "：" + string.Join(", ", rec.Take(4).Select(r => r.Name)) : "");
        });

        await CaseAsync("ckan/optional-relations", async () =>
        {
            var rel = await svc.GetOptionalRelationsAsync(sampleId);
            if (rel is null) throw new InvalidOperationException($"{sampleId} 关系读取返回 null");
            return $"recommends {rel.Recommends.Count} 个，suggests {rel.Suggests.Count} 个";
        });

        await CaseAsync("ckan/reverse-deps-and-conflicts", () =>
        {
            var (rev, conf) = svc.GetReverseAndConflicts(sampleId);
            return $"依赖它的已装模组 {rev.Count} 个，与它冲突 {conf.Count} 个";
        });

        await CaseAsync("ckan/unmanaged-list", async () =>
        {
            var list = await svc.GetUnmanagedAsync();
            return $"扫描登记未托管项 {list.Count} 个" +
                   (list.Count > 0 ? "：" + string.Join(", ", list.Take(5).Select(u => u.Identifier)) : "");
        });

        await CaseAsync("ckan/multi-install-plan", async () =>
        {
            if (secondId.Length == 0) throw new InvalidOperationException("没有第二个样例标识");
            var plan = await svc.GetMultiInstallPlanAsync(new[] { sampleId, secondId });
            if (plan is null) throw new InvalidOperationException("批量解析返回 null");
            if (plan.FailureReason is not null)
                throw new InvalidOperationException("批量解析失败：" + plan.FailureReason);
            return $"批量 {sampleId}+{secondId} → 安装 {plan.ToInstall.Count}、升级 {plan.ToUpgrade.Count}、移除 {plan.ToRemove.Count}";
        });

        await CaseSkipAsync("ckan/scan-unmanaged", "会写注册表（非只读），本轮不执行，避免改动你的实例状态");
    }

    #endregion

    #region 其他（收藏 / 图标回填 / 主页预设）

    private static async Task GroupMiscAsync()
    {
        Write("---------- 分组：收藏 / 图标回填 / 主页 ----------");
        var svc = PCL.Launcher.Ckan;

        await CaseAsync("favorites/list-and-lookup", () =>
        {
            var list = Comp.CompFavorites.FavoritesList;
            if (list is null) throw new InvalidOperationException("收藏夹列表为 null");
            var total = list.Sum(f => f.Favs.Count);
            if (total > 0)
            {
                var sample = list.SelectMany(f => f.Favs).First();
                if (!Comp.CompFavorites.IsFavourite(sample))
                    throw new InvalidOperationException("收藏夹里的 ID 反查失败：" + sample);
                return $"{list.Count} 个收藏夹，共 {total} 项，抽样命中 {sample}";
            }
            return $"{list.Count} 个收藏夹，共 0 项（尚无收藏）";
        });

        await CaseAsync("favorites/share-code-roundtrip", () =>
        {
            var ids = new System.Collections.Generic.HashSet<string> { "A-mod", "B-mod", "C-mod" };
            var code = Comp.CompFavorites.GetShareCode(ids);
            if (string.IsNullOrWhiteSpace(code)) throw new InvalidOperationException("分享码为空");
            var back = Comp.CompFavorites.GetIdsByShareCode(code);
            if (back.Count != ids.Count)
                throw new InvalidOperationException($"分享码往返丢失：{ids.Count} → {back.Count}");
            return $"分享码 {code.Length} 字符，往返 {back.Count} 项一致";
        });

        await CaseAsync("spacedock/enrich-range", async () =>
        {
            var all = await svc.SearchAsync(null);
            var list = all.Take(5).ToList();
            await svc.EnrichRangeAsync(list, 0, list.Count);
            var withIcon = list.Count(m => !string.IsNullOrWhiteSpace(m.IconUrl));
            var withDownloads = list.Count(m => m.Downloads > 0);
            var withAuthor = list.Count(m => !string.IsNullOrWhiteSpace(m.Author));
            if (withIcon + withDownloads == 0)
                throw new InvalidOperationException("5 条样例全部未回填到图标/下载量（SpaceDock 可能不可达）");
            return $"5 条中：图标 {withIcon}、下载量 {withDownloads}、作者 {withAuthor}；样例={list[0].Name}";
        });

        await CaseAsync("homepage/preset-list", () =>
        {
            var all = HomePagePreset.All;
            if (all.Length == 0) throw new InvalidOperationException("主页预设列表为空");
            var titles = string.Join(" / ", all.Select(p => p.Id + ":" + p.Title));
            var resolved = HomePagePreset.Resolve(999) == 0 ? "999→0（回退内置）" : "Resolve 异常";
            return $"{all.Length} 个预设：{titles}；{resolved}";
        });

        await CaseAsync("homepage/mode-value", () =>
        {
            var mode = KspSettings.Current.HomePageMode;
            if (mode is < 0 or > 3) throw new InvalidOperationException("主页模式越界：" + mode);
            var preset = KspSettings.Current.HomePagePreset;
            if (HomePagePreset.Find(preset) is null) throw new InvalidOperationException("预设编号不存在：" + preset);
            return $"模式={mode}，预设={preset}（{HomePagePreset.Find(preset)!.Title}）";
        });

        await CaseAsync("wiki/clean-markup", () =>
        {
            const string html = "<p>天体的 [[atmosphere|大气]] 会让穿过它的物体减速，"
                                + "参见 [[Kerbin]] 与 [https://wiki.kerbalspaceprogram.com/wiki/Duna Duna]，"
                                + "另有残留 [atmosphere|大气] 与 [[File:Rock.png]] 图片。</p>";
            var text = PCL.Pages.PageHome.CleanWikiText(html, "大气");
            if (text.Contains("[[") || text.Contains("]]"))
                throw new InvalidOperationException("正文仍残留双方括号：" + text);
            if (text.Contains("|"))
                throw new InvalidOperationException("正文仍残留竖线：" + text);
            if (text.Contains("https://"))
                throw new InvalidOperationException("正文仍残留裸链接：" + text);
            if (!text.Contains("大气") || !text.Contains("Kerbin") || !text.Contains("Duna"))
                throw new InvalidOperationException("链接文字被吞掉：" + text);
            return "清洗结果：" + text;
        });

        await CaseAsync("settings/ckan-datadir-migrated", () =>
        {
            var dir = KspSettings.Current.CkanDataDir ?? "";
            var legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PKL", "Ckan");
            if (dir.TrimEnd('\\', '/').Equals(legacy.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("CKAN 数据目录仍是旧路径：" + dir);
            return $"CKAN 数据目录={dir}（已不在旧 PKL 目录下）";
        });
    }

    #endregion

    #region 实例实改（破坏性，用户已授权）

    private sealed class LogProgress : IProgress<string>
    {
        public void Report(string value) => Common.Log("[SelfTest]   进度 " + value);
    }

    private static async Task GroupLiveAsync()
    {
        Write("---------- 分组：实例实改（会改 D:\\Kerbal Space Program） ----------");
        var svc = PCL.Launcher.Ckan;
        string target = "";
        string beforeVersion = "";

        await CaseAsync("live/scan-unmanaged", async () =>
        {
            var before = (await svc.GetUnmanagedAsync()).Count;
            var changed = await svc.ScanUnmanagedAsync();
            var after = (await svc.GetUnmanagedAsync()).Count;
            return $"扫描前登记 {before} 个，本次有变化={changed}，扫描后 {after} 个";
        });

        await CaseAsync("live/choose-safe-target", async () =>
        {
            var installed = (await svc.ListInstalledAsync()).Where(m => !m.IsAutodetected).ToList();
            Common.Log($"[SelfTest]    已装管理项 {installed.Count} 个，逐个评估可安全往返的目标");
            var rejected = new List<string>();
            foreach (var m in installed)
            {
                if (string.IsNullOrWhiteSpace(m.Identifier))
                {
                    rejected.Add("(空标识)");
                    continue;
                }
                var (rev, _) = svc.GetReverseAndConflicts(m.Identifier);
                if (rev.Count > 0)
                {
                    rejected.Add($"{m.Identifier}:被{rev.Count}个依赖");
                    continue;
                }
                var plan = await svc.GetInstallPlanAsync(m.Identifier);
                if (plan is null)
                {
                    rejected.Add($"{m.Identifier}:拿不到计划");
                    continue;
                }
                if (plan.FailureReason is not null)
                {
                    rejected.Add($"{m.Identifier}:{plan.FailureReason}");
                    continue;
                }
                if (plan.ToRemove.Count > 0)
                {
                    rejected.Add($"{m.Identifier}:会先移除{plan.ToRemove.Count}项");
                    continue;
                }
                if (plan.ToInstall.Count > 3)
                {
                    rejected.Add($"{m.Identifier}:要装{plan.ToInstall.Count}项");
                    continue;
                }
                target = m.Identifier;
                beforeVersion = m.Version ?? "";
                return $"{target}（{beforeVersion}）反向依赖 0、计划 装{plan.ToInstall.Count}/移除 0，适合往返";
            }
            Common.Log("[SelfTest]    被排除的候选：" + string.Join("; ", rejected.Take(30)));
            throw new InvalidOperationException($"没有可安全往返的目标（共评估 {installed.Count} 项，排除原因已写入日志）");
        });

        await CaseAsync("live/uninstall-then-reinstall", async () =>
        {
            if (target.Length == 0) throw new InvalidOperationException("没有选定目标");
            Common.Log($"[SelfTest]    准备卸载 {target}（版本 {beforeVersion}）");
            await svc.UninstallAsync(target, new LogProgress());
            var afterRemove = await svc.ListInstalledAsync();
            var gone = afterRemove.All(m => !string.Equals(m.Identifier, target, StringComparison.OrdinalIgnoreCase));
            if (!gone) throw new InvalidOperationException("卸载后仍出现在已装列表：" + target);

            var plan = await svc.GetInstallPlanAsync(target);
            if (plan is null || plan.FailureReason is not null)
                throw new InvalidOperationException("卸载后重新解析失败：" + (plan?.FailureReason ?? "null"));
            await svc.ExecutePlanAsync(plan, new LogProgress());

            var afterInstall = await svc.ListInstalledAsync();
            var back = afterInstall.FirstOrDefault(m => string.Equals(m.Identifier, target, StringComparison.OrdinalIgnoreCase));
            if (back is null) throw new InvalidOperationException("重装失败，列表里没有：" + target);
            return $"{target} 卸载→重装成功，版本 {back.Version}（原 {beforeVersion}），计划含 {plan.ToInstall.Count} 项";
        });

        await CaseAsync("live/cache-clear", async () =>
        {
            var before = await svc.GetCacheStatsAsync();
            await svc.ClearCacheAsync();
            var after = await svc.GetCacheStatsAsync();
            if (after is { FileCount: > 0 })
                throw new InvalidOperationException($"清理后仍有 {after.FileCount} 个缓存文件");
            return $"清理前 {before?.FileCount} 个/{before?.TotalBytes / 1024} KB，清理后 {after?.FileCount} 个";
        });

        await CaseAsync("live/import-invalid-file", async () =>
        {
            var tmp = Path.Combine(Path.GetTempPath(), "pcl-selftest-not-a-mod.txt");
            File.WriteAllText(tmp, "this is not a mod package");
            var result = await svc.ImportCacheFilesAsync(new[] { tmp });
            File.Delete(tmp);
            if (result.Imported != 0) throw new InvalidOperationException("垃圾文件竟被导入：" + result.Imported);
            return $"拒绝导入 {result.Failed.Count} 个（Imported={result.Imported}）";
        });

        await CaseAsync("live/modpack-build-parse", async () =>
        {
            var installed = await svc.ListInstalledAsync();
            var kspVersion = KspSettings.Current.KspRootPath is { Length: > 0 } root
                ? new KspInstanceService().Probe(root)?.Version
                : null;
            var json = CkanModpack.Build(installed, kspVersion, "pclksp.selftest");
            var ids = CkanModpack.Parse(json);
            var managed = installed.Count(m => m.IsInstalled && !m.IsAutodetected && !string.IsNullOrWhiteSpace(m.Identifier));
            if (ids.Count == 0) throw new InvalidOperationException("导出的清单解析回来是空");
            if (ids.Count < managed - 1)
                throw new InvalidOperationException($"导出 {managed} 个 → 解析 {ids.Count} 个，丢失过多");
            return $"生成 {json.Length} 字符 metapackage，解析回 {ids.Count} 个依赖（已装管理项 {managed}）";
        });

        await CaseAsync("live/launch-ksp-process", async () =>
        {
            var root = KspSettings.Current.KspRootPath;
            if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("未配置 KSP 路径");
            var exe = Path.Combine(root, "KSP_x64.exe");
            if (!File.Exists(exe)) exe = Path.Combine(root, "KSP.exe");
            if (!File.Exists(exe)) throw new InvalidOperationException("找不到 KSP 可执行文件");
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = root,
                UseShellExecute = true
            });
            if (p is null) throw new InvalidOperationException("Process.Start 返回 null");
            var pid = p.Id;
            await Task.Delay(8000);
            var alive = !p.HasExited;
            try { if (alive) p.Kill(true); } catch { }
            if (!alive) throw new InvalidOperationException("KSP 启动后 8 秒内就退出了，可能启动崩溃");
            return $"{Path.GetFileName(exe)} 启动成功并存活 8 秒（PID {pid}），已结束";
        });
    }

    #endregion
}
