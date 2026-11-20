using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace PCL.Pages;

public partial class PageHome
{
    private const string UpdateLogText =
        "2026-11-20\n" +
        "· 主页「KSP Wiki 随机条目」改为多卡片 + 配图版式：说明横幅卡、带大图的主条目卡、三张相关条目卡。\n" +
        "· 随机条目改为后台预取 + 配图预热，点「换一条」不再等网络；被反爬拦截时保留当前条目并提示。\n" +
        "· 随机条目改为从中文条目池抽取：抽走的不再放回，抽完一轮自动重新洗牌。\n" +
        "· 日志文件列表补齐 lucide/file-text 图标资源，条目左侧图标恢复显示。\n" +
        "· 设置页「主页」新增「自定义网址」档：从远端 XAML 地址拉取主页内容，带本地缓存与失败重试。\n" +
        "2026-11-19\n" +
        "· 主页按原版结构恢复：左栏启动面板（含启动中动画、进度、阶段与取消），右栏公告 / 社区 / 更新日志卡片。\n" +
        "· 设置页「主页」可选择显示方式：空白、预设、本地 XAML 文件。\n" +
        "· 新增「KSP Wiki 随机条目」预设：页面本地渲染，内容从 KSP Wiki 随机拉取。\n" +
        "· 预设主页可选项接入原版中与 Minecraft 无关的第三方预设（Bangumi 番剧 / 今日新闻热点），联网拉取并缓存。\n" +
        "· 切页进入动画补充透明度兜底，修复控件需要悬停才显示的问题。\n" +
        "· 启动页显示 KSP 版本与安装路径，启动失败给出明确提示。\n" +
        "· 日志文件列表补齐文件图标；历史提交清理本机路径并更名 PCL-KSP。";

    /// <summary>「你知道吗」预设的贴士池（与原版一样为本地内容，不依赖网络）。</summary>
    private static readonly string[] TriviaTips =
    {
        "KSP 的物理按刚体计算，一枚 300 吨的火箭和纸飞机用的是同一套牛顿方程，所以小零件多反而更卡。",
        "Δv 是规划机动的核心：近地轨道（约 80 km）到 Mun 转移窗口大约需要 860 m/s。",
        "大气层内上升要「重力转向」：先垂直爬到 1–2 km，再逐渐压到 45°，最后接近水平。",
        "固推（SRB）点火后既不能关机也不能节流，装上一级之前先想好什么时候抛掉它。",
        "分级时先关发动机、再分离，否则分离瞬间的推力会把上一级顶回箭体。",
        "再入时让返回舱底部朝前：气动遮挡按部件质量分布计算，细长箭体很容易翻滚失控。",
        "通信链路要覆盖整条轨道，离开 Kerbin 系统后基本需要中继卫星或更大的天线（RA-15 / RA-100）。",
        "太阳能板只有太阳直射时才是满功率，飞到 Jool 附近功率大约只剩地球轨道的 4%。",
        "飞行中物理加速最高 4 倍，想开 100000 倍得先回到航天中心。",
        "在 VAB 里按住 Shift 可以复制整个部件树，Ctrl+Z 撤销；这两条建议越早记住越好。",
        "燃料管线会反向流动，给油箱接燃料管时注意方向，否则会出现「发动机喝不到油」。",
        "用 CKAN 装模组要看清 KSP 版本：1.12.5 与 1.8.x 的模组不一定互相兼容。",
        "对接想稳，先让两个飞行器的相对速度降到 0.1 m/s 以内，再处理横向偏差。",
        "Mun 的转移窗口不像现实里那么苛刻：先把圆轨道铺到 80 km，再点火基本都能到。"
    };

    private static readonly HttpClient Http = CreateHttp();

    private int _triviaIndex = -1;
    private HomePagePreset _currentPreset = HomePagePreset.All[0];
    private CancellationTokenSource? _presetCts;
    private CancellationTokenSource? _wikiCts;
    private string _wikiUrl = string.Empty;
    private string _customUrl = string.Empty;
    private Func<Task>? _retry;

    public PageHome()
    {
        InitializeComponent();
        Unloaded += (_, _) =>
        {
            _presetCts?.Cancel();
            _wikiCts?.Cancel();
        };

        ApplyHomeMode();

    }

    private static HttpClient CreateHttp()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PCL-KSP/1.0");
        return client;
    }

    private static string CacheDir
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCL-KSP",
            "homepage-presets");

    private void ApplyHomeMode()
    {
        var mode = KspSettings.Current.HomePageMode;
        // 本地文件档未选择文件时，按原版语义回落到空白
        if (mode == 2 && string.IsNullOrWhiteSpace(KspSettings.Current.HomePageLocalFile)) mode = 0;
        // 自定义网址档没填有效地址时，同样按原版语义回落到空白
        if (mode == 3 && !IsUsableHomeUrl(KspSettings.Current.HomePageUrl)) mode = 0;
        if (mode == 1)
        {
            LoadPreset(KspSettings.Current.HomePagePreset);
            return;
        }
        HideAllViews();
        _customUrl = string.Empty;
        _retry = null;
        PanCustomHost.Content = null;
        PanCustomHost.Visibility = Visibility.Collapsed;
        if (mode == 2) LoadCustom(KspSettings.Current.HomePageLocalFile);
        if (mode == 3) LoadCustomUrl(KspSettings.Current.HomePageUrl);
    }

    private void HideAllViews()
    {
        PanPreset.Visibility = Visibility.Collapsed;
        CardTrivia.Visibility = Visibility.Collapsed;
        CardPresetLoading.Visibility = Visibility.Collapsed;
        PanWiki.Visibility = Visibility.Collapsed;
        CardCustomError.Visibility = Visibility.Collapsed;
    }

    private void LoadPreset(int id)
    {
        var preset = HomePagePreset.Find(HomePagePreset.Resolve(id));
        _currentPreset = preset;
        _retry = null;
        _customUrl = string.Empty;
        _presetCts?.Cancel();
        _wikiCts?.Cancel();
        HideAllViews();
        PanCustomHost.Content = null;
        PanCustomHost.Visibility = Visibility.Collapsed;

        if (preset.Id == 6)
        {
            PanWiki.Visibility = Visibility.Visible;
            _ = LoadWikiEntryAsync();
            return;
        }

        if (preset.Url is not null)
        {
            _ = LoadNetworkPresetAsync(preset);
            return;
        }

        if (preset.Id == 1)
        {
            CardTrivia.Visibility = Visibility.Visible;
            ShowNextTrivia();
            return;
        }

        PanPreset.Visibility = Visibility.Visible;
        LoadBuiltinPreset();
    }

    /// <summary>联网预设：先取缓存立即显示，再拉远端覆盖（与原版 LoadFromNetwork 的做法一致）。</summary>
    private Task LoadNetworkPresetAsync(HomePagePreset preset)
    {
        return LoadNetworkPageAsync(preset.Url!, preset.Title, "preset-" + preset.Id + ".xaml", "预设主页");
    }

    /// <summary>自定义网址主页：与联网预设同一条通路，缓存文件名按地址哈希区分。</summary>
    private void LoadCustomUrl(string url)
    {
        var target = url.Trim();
        _customUrl = target;
        _ = LoadNetworkPageAsync(target, "自定义主页", "preset-url-" + ShortHash(target) + ".xaml", "自定义主页");
    }

    private async Task LoadNetworkPageAsync(string url, string title, string cacheFileName, string label)
    {
        var cts = new CancellationTokenSource();
        _presetCts = cts;
        var token = cts.Token;
        _retry = () => LoadNetworkPageAsync(url, title, cacheFileName, label);

        CardCustomError.Visibility = Visibility.Collapsed;
        LabPresetLoading.Text = "正在加载「" + title + "」…";
        CardPresetLoading.Visibility = Visibility.Visible;

        var cache = Path.Combine(CacheDir, cacheFileName);
        var hasCache = false;
        if (File.Exists(cache))
        {
            try
            {
                if (TryShowFragment(File.ReadAllText(cache, Encoding.UTF8), out _))
                    hasCache = true;
            }
            catch
            {
                // 缓存损坏，忽略，等待网络
            }
        }
        if (hasCache) CardPresetLoading.Visibility = Visibility.Collapsed;

        try
        {
            var text = await Http.GetStringAsync(url, token).ConfigureAwait(true);
            if (token.IsCancellationRequested) return;
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("服务器返回内容为空。");
            try
            {
                Directory.CreateDirectory(CacheDir);
                File.WriteAllText(cache, text, new UTF8Encoding(false));
            }
            catch
            {
                // 缓存写失败不影响显示
            }
            if (TryShowFragment(text, out var parseError))
            {
                CardPresetLoading.Visibility = Visibility.Collapsed;
                CardCustomError.Visibility = Visibility.Collapsed;
            }
            else
            {
                CardPresetLoading.Visibility = Visibility.Collapsed;
                if (!hasCache) ShowError(label + "解析失败：" + parseError);
            }
        }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested) return;
            CardPresetLoading.Visibility = Visibility.Collapsed;
            if (!hasCache)
                ShowError(label + "加载失败：" + ex.Message);
        }
    }

    /// <summary>判断填写的地址是否可直接当作远端主页拉取。</summary>
    private static bool IsUsableHomeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        try
        {
            var uri = new Uri(url.Trim());
            return (uri.Scheme == "http" || uri.Scheme == "https") && !string.IsNullOrWhiteSpace(uri.Host);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>取地址的短哈希，用作单个网址主页的缓存文件名，避免换地址后读到旧缓存。</summary>
    private static string ShortHash(string text)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).Substring(0, 12).ToLowerInvariant();
    }
    /// <summary>将上游 XAML 片段清洗、包裹成可解析页面并挂到宿主。</summary>
    private bool TryShowFragment(string raw, out string error)
    {
        try
        {
            var element = XamlReader.Parse(PrepareFragment(raw)) as UIElement;
            if (element is null)
            {
                error = "预设内容不是可显示的 XAML。";
                return false;
            }
            if (element is FrameworkElement frameworkElement) frameworkElement.Margin = new Thickness(0);
            WireButtons(element);
            PanCustomHost.Content = element;
            PanCustomHost.Visibility = Visibility.Visible;
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// 上游预设是片段而非完整页面：剥掉 xmlns 声明后包进 StackPanel；
    /// 同时把 EventType="打开网页" 的按钮降级为 Tag 携带链接，其余自定义事件属性直接剥除，
    /// 否则 XamlReader 会因未知属性解析失败。
    /// </summary>
    private static string PrepareFragment(string raw)
    {
        // PCL CE 公告栏里「打开帮助」这类按钮指向 Minecraft 帮助页，KSP 里没有对应内容，整段移除（含跨行写法）。
        raw = Regex.Replace(raw, @"<local:MyButton\b[^>]*?EventData=""Minecraft/[^""]*""[^>]*?/>", string.Empty,
            RegexOptions.Singleline);
        raw = Regex.Replace(raw, @"<local:MyButton\b[^>]*?EventData=""Minecraft/[^""]*""[^>]*?>(?:(?!</local:MyButton).)*</local:MyButton>",
            string.Empty, RegexOptions.Singleline);        raw = raw.Replace("\uFEFF", string.Empty);
        raw = Regex.Replace(raw, @"\sxmlns(?::[A-Za-z0-9_]+)?=""[^""]*""", string.Empty);
        raw = Regex.Replace(raw, @"\sEventType=""打开网页""\s+EventData=""([^""]+)""", @" Tag=""$1""");
        raw = Regex.Replace(raw, @"\sEventData=""([^""]+)""\s+EventType=""打开网页""", @" Tag=""$1""");
        raw = Regex.Replace(raw, @"\sEvent(Type|Data)=""[^""]*""", string.Empty);
        var assembly = typeof(PageHome).Assembly.GetName().Name;
        return "<StackPanel xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"" +
               " xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"" +
               " xmlns:sys=\"clr-namespace:System;assembly=mscorlib\"" +
               " xmlns:local=\"clr-namespace:PCL;assembly=" + assembly + "\">" + raw + "</StackPanel>";
    }

    /// <summary>预设里的按钮只保留了 Tag 里的网址，这里统一接上「打开网页」。</summary>
    private static void WireButtons(DependencyObject root)
    {
        if (root is MyButton button && button.Tag is string url &&
            url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            button.Click += (_, _) => PCL.Core.App.Basics.OpenPath(url);
        foreach (var child in LogicalTreeHelper.GetChildren(root))
            if (child is DependencyObject childObject)
                WireButtons(childObject);
    }

    private void LoadBuiltinPreset()
    {
        LabWelcome.Text = "PCL KSP v" + PCL.Core.App.Basics.VersionName;
        LabWelcomeSub.Text = "基于 PCL-CE 改造的《坎巴拉太空计划》启动器，使用 CKAN 管理模组。";
        LabWelcomeNotice.Text =
            "当前为开发中版本，功能仍在补齐。启动前请先在设置里确认 KSP 安装路径；" +
            "模组安装与更新走 CKAN，公共索引默认使用坎喵加速镜像。";
        LabLog.Text = UpdateLogText;
    }

    private void ShowNextTrivia()
    {
        var index = Random.Shared.Next(TriviaTips.Length);
        if (index == _triviaIndex) index = (index + 1) % TriviaTips.Length;
        _triviaIndex = index;
        LabTrivia.Text = TriviaTips[index];
    }

    private void BtnTriviaRefresh_Click(object sender, EventArgs e) => ShowNextTrivia();

    private void LoadCustom(string path)
    {
        CardTrivia.Visibility = Visibility.Collapsed;
        if (string.IsNullOrWhiteSpace(path))
        {
            ShowError("尚未选择本地主页文件。");
            return;
        }
        if (!File.Exists(path))
        {
            ShowError("找不到主页文件：" + path);
            return;
        }
        try
        {
            var text = File.ReadAllText(path);
            var element = XamlReader.Parse(text) as UIElement;
            if (element is null)
            {
                ShowError("该文件不是可显示的 XAML 页面：" + path);
                return;
            }
            if (element is FrameworkElement frameworkElement)
                frameworkElement.Margin = new Thickness(0);
            PanCustomHost.Content = element;
            PanCustomHost.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            ShowError("主页文件解析失败：" + ex.Message);
        }
    }

    private void ShowError(string message)
    {
        LabCustomError.Text = message;
        BtnPresetRetry.Visibility = _retry is null ? Visibility.Collapsed : Visibility.Visible;
        CardCustomError.Visibility = Visibility.Visible;
    }

    private void BtnPresetRetry_Click(object sender, MouseButtonEventArgs e)
    {
        if (_retry is null) return;
        var retry = _retry;
        CardCustomError.Visibility = Visibility.Collapsed;
        _ = retry();
    }

    private const string WikiApi = "https://wiki.kerbalspaceprogram.com/api.php";

    /// <summary>一条已经抓齐的随机条目：标题、正文与大图地址，渲染时不用再等网络。</summary>
    private sealed record WikiEntry(string SourceTitle, string BaseTitle, string? UsedTitle, string? Plain, string? ImageUrl);

    /// <summary>中文条目池的本地缓存：全站带 /zh-cn 的页面标题。中文页只有两百来个，扫一次存下来复用一周。</summary>
    private sealed record WikiPoolCache(DateTime FetchedAt, string[] Titles);

    // 随机条目每次都是新页面，缓存正文没有意义；改成提前抓 1–2 条放内存队列，
    // 点「换一条」直接取队列，网络往返藏在上一条展示的时间里。
    private readonly ConcurrentQueue<WikiEntry> _wikiQueue = new();
    private const int WikiQueueTarget = 2;
    private bool _wikiPrefetching;
    private WikiEntry? _wikiLastEntry;
    private int _wikiHeroLoadToken;

    // 中文条目池：洗牌后逐个抽走，抽完一轮再重新洗牌（抽奖模式，不重复抽同一条）
    private static readonly string WikiPoolPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCL-KSP", "wiki-zh-pool.json");
    private readonly object _wikiPoolLock = new();
    private string[] _wikiPool = Array.Empty<string>();
    private readonly Queue<string> _wikiDrawQueue = new();
    private Task? _wikiPoolTask;

    /// <summary>确保中文池就绪：优先读 7 天内的本地缓存，没有就扫一遍全站页面标题。</summary>
    private Task EnsureWikiPoolAsync()
    {
        lock (_wikiPoolLock)
        {
            if (_wikiPool.Length > 0) return Task.CompletedTask;
            return _wikiPoolTask ??= LoadOrScanWikiPoolAsync();
        }
    }

    private async Task LoadOrScanWikiPoolAsync()
    {
        try
        {
            if (TryLoadWikiPoolCache()) return;
            Common.Log("KSP Wiki 中文条目池缓存缺失或过期，开始扫描全站页面标题");
            var titles = await ScanWikiPoolAsync().ConfigureAwait(false);
            if (titles.Length == 0) throw new InvalidOperationException("没能从 Wiki 取到中文条目清单");
            lock (_wikiPoolLock)
            {
                _wikiPool = titles;
                _wikiDrawQueue.Clear();
            }
            SaveWikiPoolCache(titles);
            Common.Log($"KSP Wiki 中文条目池已更新：{titles.Length} 条");
        }
        catch (Exception ex)
        {
            lock (_wikiPoolLock) { _wikiPoolTask = null; } // 下次点击重新尝试
            Common.Log(ex, "KSP Wiki 中文条目池扫描失败，暂时退回全站随机", Common.LogLevel.Developer);
        }
    }

    private bool TryLoadWikiPoolCache()
    {
        try
        {
            if (!File.Exists(WikiPoolPath)) return false;
            var cache = JsonSerializer.Deserialize<WikiPoolCache>(File.ReadAllText(WikiPoolPath, Encoding.UTF8));
            if (cache?.Titles is not { Length: > 0 }) return false;
            if (DateTime.Now - cache.FetchedAt > TimeSpan.FromDays(7)) return false;
            lock (_wikiPoolLock)
            {
                _wikiPool = cache.Titles;
                _wikiDrawQueue.Clear();
            }
            return true;
        }
        catch (Exception ex)
        {
            Common.Log(ex, "读取 KSP Wiki 中文条目池缓存失败", Common.LogLevel.Developer);
            return false;
        }
    }

    private static void SaveWikiPoolCache(string[] titles)
    {
        try
        {
            var dir = Path.GetDirectoryName(WikiPoolPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(WikiPoolPath,
                JsonSerializer.Serialize(new WikiPoolCache(DateTime.Now, titles)), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Common.Log(ex, "写入 KSP Wiki 中文条目池缓存失败", Common.LogLevel.Developer);
        }
    }

    /// <summary>扫描主命名空间的所有页面标题，留下带 /zh-cn 的中文页。</summary>
    private static async Task<string[]> ScanWikiPoolAsync()
    {
        var titles = new List<string>();
        string? continueToken = null;
        for (var round = 0; round < 40; round++)
        {
            var url = WikiApi + "?action=query&list=allpages&apnamespace=0&aplimit=500&format=json&formatversion=2" +
                      (continueToken is null ? "" : "&apcontinue=" + Uri.EscapeDataString(continueToken));
            var json = await Http.GetStringAsync(url, CancellationToken.None).ConfigureAwait(false);
            using var doc = ParseWikiJson(json);
            foreach (var page in doc.RootElement.GetProperty("query").GetProperty("allpages").EnumerateArray())
            {
                var title = page.GetProperty("title").GetString();
                if (title is null || !title.EndsWith("/zh-cn", StringComparison.OrdinalIgnoreCase)) continue;
                if (IsUsablePoolTitle(title[..^"/zh-cn".Length])) titles.Add(title);
            }
            if (!doc.RootElement.TryGetProperty("continue", out var cont) ||
                !cont.TryGetProperty("apcontinue", out var next)) break;
            continueToken = next.GetString();
        }
        return titles.Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>粗略判断正文是不是中文：汉字占全部字母的比例够高才算，挡掉俄文/英文原文。</summary>
    private static bool IsMostlyChinese(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var cjk = 0;
        var letters = 0;
        foreach (var c in text)
        {
            if (c is >= '\u4e00' and <= '\u9fff')
            {
                cjk++;
                letters++;
            }
            else if (char.IsLetter(c))
            {
                letters++;
            }
        }
        return letters > 0 && cjk * 10 >= letters * 3;
    }
    /// <summary>中文池过滤：跳过版本号页、嵌套子页、命名空间页和首页。</summary>
    private static bool IsUsablePoolTitle(string baseTitle)
    {
        if (baseTitle.Length is 0 or > 60) return false;
        if (baseTitle is "Main Page" or "KSP Wiki") return false;
        if (baseTitle.Contains('/') || baseTitle.Contains(':')) return false;
        if (Regex.IsMatch(baseTitle, @"^[0-9]+([.][0-9]+)*$")) return false;
        return true;
    }
    /// <summary>从池里抽一条并移出本轮；这一轮抽完就重新洗牌。</summary>
    private string? TakeFromWikiPool()
    {
        lock (_wikiPoolLock)
        {
            if (_wikiPool.Length == 0) return null;
            if (_wikiDrawQueue.Count == 0) RefillWikiDrawQueueUnlocked();
            return _wikiDrawQueue.Count == 0 ? null : _wikiDrawQueue.Dequeue();
        }
    }

    private void RefillWikiDrawQueueUnlocked()
    {
        var shuffled = _wikiPool.OrderBy(_ => Random.Shared.Next()).ToArray();
        // 新一轮的第一条别和刚展示的重复
        if (shuffled.Length > 1 && _wikiLastEntry is not null && shuffled[0] == _wikiLastEntry.SourceTitle)
            (shuffled[0], shuffled[^1]) = (shuffled[^1], shuffled[0]);
        foreach (var title in shuffled) _wikiDrawQueue.Enqueue(title);
    }

    /// <summary>中文池不可用时的退路：照旧走全站随机。</summary>
    private static async Task<string?> PickRandomWikiTitleAsync(CancellationToken token)
    {
        var randomUrl = WikiApi +
                        "?action=query&list=random&rnlimit=1&rnnamespace=0&format=json&formatversion=2";
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var randomJson = await Http.GetStringAsync(randomUrl, token).ConfigureAwait(false);
            using var randomDoc = ParseWikiJson(randomJson);
            var candidate = randomDoc.RootElement.GetProperty("query").GetProperty("random")[0]
                .GetProperty("title").GetString() ?? string.Empty;
            // 语言子页（/ru、/ko 等）、命名空间页、版本号页都不要，否则会把外文正文顶到主页上
            if (candidate.Contains('/') || candidate.Contains(':')) continue;
            if (Regex.IsMatch(candidate, @"^[0-9]+([.][0-9]+)*$")) continue;
            if (candidate is "Main Page" or "Wiki Home" or "KSP Wiki") continue;
            return candidate;
        }
        return null;
    }
    /// <summary>拉取 KSP Wiki 的随机条目并在本地渲染（页面本身是本地 XAML，只有内容联网）。</summary>
    private async Task LoadWikiEntryAsync()
    {
        _wikiCts?.Cancel();
        var cts = new CancellationTokenSource();
        _wikiCts = cts;
        var token = cts.Token;

        ShowWikiLoading();
        try
        {
            var entry = _wikiQueue.TryDequeue(out var queued)
                ? queued
                : await FetchWikiEntryAsync(token).ConfigureAwait(true);

            if (token.IsCancellationRequested) return; // 连点换条时，旧请求不许覆盖新结果
            if (entry is null) throw new InvalidOperationException("Wiki 没有返回可用的条目。");
            RenderWikiEntry(entry);

            EnsureWikiQueue(); // 展示完立刻补队列，下一次点击就不用等网络
        }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested) return;
            Common.Log(ex, $"KSP Wiki 条目切换失败（已有内容：{_wikiLastEntry is not null}）");
            if (_wikiLastEntry is not null)
            {
                // 反爬拦截或网络抖动时不清空已有内容，只提示这一条没换成功
                RenderWikiEntry(_wikiLastEntry);
                LabWikiStatus.Text = "这条没换成（" + DescribeWikiFailure(ex) + "），已保留当前条目。";
                LabWikiStatus.Visibility = Visibility.Visible;
            }
            else
            {
                LabWikiTitle.Text = "KSP Wiki 条目加载失败";
                LabWikiText.Text = ex.Message + "\n点「换一条」重试。";
                ClearWikiHeroImage();
                PanWikiHeroImage.MinHeight = 0;
                PanWikiHeroImage.Visibility = Visibility.Collapsed;
            }
        }
        finally
        {
            if (!token.IsCancellationRequested) BtnWikiNext.IsEnabled = true;
        }
    }

    /// <summary>抓一条随机条目：标题、正文、配图地址。纯网络与解析，不碰 UI，可在后台线程跑。</summary>
    private async Task<WikiEntry?> FetchWikiEntryAsync(CancellationToken token)
    {
        // 池子还没建好就在后台去扫，这一条先用全站随机顶上，不卡住页面
        _ = EnsureWikiPoolAsync();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var poolTitle = TakeFromWikiPool();
            if (poolTitle is null) break;
            var poolEntry = await BuildWikiEntryAsync(poolTitle, token).ConfigureAwait(false);
            // 个别 /zh-cn 页面其实还是外文（机翻/复制原文），正文不像中文就换下一条
            if (poolEntry is not null && IsMostlyChinese(poolEntry.Plain)) return poolEntry;
        }

        // 池还没建好或抽不到中文正文时的临时退路：只允许主命名空间的英文/中文条目，绝不碰 /ru、/ko 这类语言子页
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var randomTitle = await PickRandomWikiTitleAsync(token).ConfigureAwait(false);
            if (randomTitle is null) break;
            var randomEntry = await BuildWikiEntryAsync(randomTitle, token).ConfigureAwait(false);
            if (randomEntry is not null) return randomEntry;
        }
        return null;
    }

    /// <summary>按标题抓正文与配图；中文子页优先用它自己的正文，取不到再退回英文主条目。</summary>
    private async Task<WikiEntry?> BuildWikiEntryAsync(string title, CancellationToken token)
    {
        var baseTitle = StripLanguageSuffix(title);

        // 分类页、Parts/xxx 这类条目首页常常没有正文，这时改抓整页再压成文本。
        string? plain = null;
        string? usedTitle = null;
        string? usedHtml = null;
        foreach (var candidate in new[] { title, baseTitle })
        {
            if (plain is { Length: > 0 }) break;
            foreach (var section in new[] { 0, -1 })
            {
                var html = await TryFetchSectionTextAsync(candidate, token, section).ConfigureAwait(false);
                if (html is null) continue;
                var text = CleanWikiText(html, StripLanguageSuffix(candidate));
                if (text.Length == 0) continue;
                plain = text;
                usedTitle = candidate;
                usedHtml = html;
                break;
            }
        }

        return new WikiEntry(title, baseTitle, usedTitle, plain, ExtractWikiImage(usedHtml));
    }

    /// <summary>把一条已经抓好的条目画到主卡上（这一步不联网）。</summary>
    private void RenderWikiEntry(WikiEntry entry)
    {
        _wikiLastEntry = entry;
        LabWikiStatus.Visibility = Visibility.Collapsed;
        LabWikiTitle.Text = entry.BaseTitle;
        LabWikiText.Text = entry.Plain is { Length: > 0 }
            ? entry.Plain
            : "这个条目暂时取不到正文，点「在 Wiki 上打开」查看完整页面。";
        _wikiUrl = BuildWikiPageUrl(entry.UsedTitle ?? entry.BaseTitle);
        BtnWikiOpen.IsEnabled = true;
        ShowWikiHeroImage(entry.BaseTitle, entry.ImageUrl);
    }

    /// <summary>后台预取：队列不足时补抓新条目，配图也一并下进图片缓存。</summary>
    private void EnsureWikiQueue()
    {
        if (_wikiPrefetching || _wikiQueue.Count >= WikiQueueTarget) return;
        _wikiPrefetching = true;
        _ = Task.Run(async () =>
        {
            try
            {
                while (_wikiQueue.Count < WikiQueueTarget)
                {
                    var entry = await FetchWikiEntryAsync(CancellationToken.None).ConfigureAwait(false);
                    if (entry is null) break;
                    if (_wikiQueue.Any(item => item.BaseTitle == entry.BaseTitle)) break;
                    // 先把配图落到 MyImage 的缓存文件里，切过去时直接命中本地文件
                    if (entry.ImageUrl is not null) _ = MyImage.DownloadImageAsync(entry.ImageUrl);
                    _wikiQueue.Enqueue(entry);
                    Common.Log($"KSP Wiki 已预取「{entry.BaseTitle}」（{entry.SourceTitle}，队列 {_wikiQueue.Count}）");
                }
            }
            catch (Exception ex)
            {
                Common.Log(ex, "KSP Wiki 随机条目预取失败", Common.LogLevel.Developer);
            }
            finally
            {
                _wikiPrefetching = false;
            }
        });
    }

    /// <summary>把切换失败的异常翻成一句人话，不把线程/解析原文贴到界面上。</summary>
    private static string DescribeWikiFailure(Exception ex)
    {
        if (ex is JsonException) return "Wiki 返回内容解析不了";
        if (ex.Message.Contains("反爬", StringComparison.Ordinal)) return "Wiki 正在反爬验证";
        if (ex.Message.Contains("取消", StringComparison.Ordinal)) return "请求被中断";
        return "网络没打通";
    }
    /// <summary>解析 Wiki 的 JSON；若拿到的是 Anubis 反爬挑战页，给可读错误而不是 JsonException。</summary>
    private static JsonDocument ParseWikiJson(string json)
    {
        if (json.TrimStart().StartsWith('<'))
            throw new InvalidOperationException("Wiki 返回了反爬验证页，请稍后重试");
        return JsonDocument.Parse(json);
    }

    /// <summary>把页面恢复成「加载中」的状态。</summary>
    private void ShowWikiLoading()
    {
        LabWikiTitle.Text = "正在拉取随机条目…";
        LabWikiText.Text = "";
        LabWikiHeroTag.Text = "";
        BorWikiHeroTag.Visibility = Visibility.Collapsed;
        ClearWikiHeroImage();
        PanWikiHeroImage.MinHeight = 0;
        PanWikiHeroImage.Visibility = Visibility.Collapsed;

        LabWikiStatus.Visibility = Visibility.Collapsed;
        BtnWikiOpen.IsEnabled = false;
        BtnWikiNext.IsEnabled = false;
    }

    private MyImage? _wikiHeroImage;

    /// <summary>设置主卡的大横幅图；没有配图时整块收起，不显示任何占位。</summary>
    private void ShowWikiHeroImage(string title, string? imageUrl)
    {
        LabWikiHeroTag.Text = title;
        _wikiHeroLoadToken++;
        ClearWikiHeroImage();
        PanWikiHeroImage.MinHeight = 0;
        PanWikiHeroImage.Visibility = Visibility.Collapsed;
        if (imageUrl is null)
        {
            BorWikiHeroTag.Visibility = Visibility.Collapsed;
            return;
        }

        BorWikiHeroTag.Visibility = Visibility.Visible;
        // 原版占位图先顶上，真图下载完成后换成图片
        PanWikiHeroImage.MinHeight = 230;
        PanWikiHeroImage.Visibility = Visibility.Visible;
        ImgWikiHeroPlaceholder.Visibility = Visibility.Visible;
        _ = ShowWikiHeroImageAsync(imageUrl, _wikiHeroLoadToken);
    }

    /// <summary>先把配图落到本地缓存，再挂一个全新的 MyImage；换条太快时旧图解码完成也只会落在已被移除的控件上。</summary>
    private async Task ShowWikiHeroImageAsync(string imageUrl, int loadToken)
    {
        try
        {
            var path = MyImage.GetTempPath(imageUrl);
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
            {
                if (File.Exists(path)) File.Delete(path); // 上次留下的 0 字节文件不算缓存
                path = await MyImage.DownloadImageAsync(imageUrl).ConfigureAwait(true);
            }
            if (loadToken != _wikiHeroLoadToken) return;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || new FileInfo(path).Length == 0)
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path);
                CollapseWikiHeroImage();
                return;
            }

            AttachWikiHeroImage(path, loadToken);
        }
        catch (Exception ex)
        {
            Common.Log(ex, "KSP Wiki 配图下载失败", Common.LogLevel.Developer);
            if (loadToken == _wikiHeroLoadToken) CollapseWikiHeroImage();
        }
    }

    /// <summary>每次换条都用新的 MyImage 实例，彻底断开上一条图片的异步回调。</summary>
    private void AttachWikiHeroImage(string path, int loadToken)
    {
        if (loadToken != _wikiHeroLoadToken) return;
        ClearWikiHeroImage();
        var image = new MyImage
        {
            Source = path,
            Stretch = Stretch.Uniform,
            MaxHeight = 230,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(4),
            Visibility = Visibility.Collapsed
        };
        image.ImageLoaded += () =>
        {
            if (loadToken != _wikiHeroLoadToken || !ReferenceEquals(_wikiHeroImage, image)) return;
            PanWikiHeroImage.MinHeight = 0; // 真图到位后按图片自身高度收放
            PanWikiHeroImage.Visibility = Visibility.Visible;
            ImgWikiHeroPlaceholder.Visibility = Visibility.Collapsed;
            image.Visibility = Visibility.Visible;
        };
        image.ImageLoadFailed += () =>
        {
            if (loadToken != _wikiHeroLoadToken || !ReferenceEquals(_wikiHeroImage, image)) return;
            // 配图拿不到就整块收起，不显示占位图或占位文字
            CollapseWikiHeroImage();
        };
        _wikiHeroImage = image;
        PanWikiHeroImage.Children.Insert(2, image); // 0 底色、1 占位图、2 真图、3 标签
    }

    /// <summary>收起配图区：没图或没拿到图时整块藏掉，不显示占位图或占位文字。</summary>
    private void CollapseWikiHeroImage()
    {
        ImgWikiHeroPlaceholder.Visibility = Visibility.Collapsed;
        ClearWikiHeroImage();
        PanWikiHeroImage.MinHeight = 0;
        PanWikiHeroImage.Visibility = Visibility.Collapsed;
    }

    private void WikiPlaceholder_ImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        Common.Log($"KSP Wiki 占位图加载失败：{e.ErrorException?.Message}$", Common.LogLevel.Developer);
    }

    private void ClearWikiHeroImage()
    {
        ImgWikiHeroPlaceholder.Visibility = Visibility.Collapsed;
        if (_wikiHeroImage is null) return;
        PanWikiHeroImage.Children.Remove(_wikiHeroImage);
        _wikiHeroImage = null;
    }
    /// <summary>从条目 HTML 里挑一张尽量大的配图：取页内宽度最大的非图标图，缩略图过小时换回原图。</summary>
    private static string? ExtractWikiImage(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        const string host = "https://wiki.kerbalspaceprogram.com";
        var candidates = new List<(int Width, string Url)>();
        foreach (Match match in Regex.Matches(html, @"<img\b[^>]*>", RegexOptions.IgnoreCase))
        {
            var tag = match.Value;
            var srcMatch = Regex.Match(tag, @"src=""([^""]+)""", RegexOptions.IgnoreCase);
            if (!srcMatch.Success) continue;
            var src = srcMatch.Groups[1].Value;
            if (!src.Contains("/images/") || src.Contains("/skins/")) continue;
            if (src.Contains("Symbol", StringComparison.OrdinalIgnoreCase) ||
                src.Contains("Icon", StringComparison.OrdinalIgnoreCase) ||
                src.Contains("Fund_char", StringComparison.OrdinalIgnoreCase))
                continue;

            var width = 0;
            var widthMatch = Regex.Match(src, @"/thumb/[^/]+/[^/]+/[^/]+/(\d+)px-", RegexOptions.IgnoreCase);
            if (widthMatch.Success)
                width = int.Parse(widthMatch.Groups[1].Value);
            else
            {
                var attrMatch = Regex.Match(tag, @"width=""(\d+)""", RegexOptions.IgnoreCase);
                if (attrMatch.Success) width = int.Parse(attrMatch.Groups[1].Value);
            }

            if (width is > 0 and < 120) continue; // 小图标直接跳过
            // 缩略图地址直接沿用 Wiki 给出的尺寸：请求更大尺寸会被 301 到原图，链路容易出问题。
            var absolute = src.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? src
                : host + (src.StartsWith('/') ? src : "/" + src);
            candidates.Add((width, absolute));
        }

        if (candidates.Count == 0) return null;
        var best = candidates.OrderByDescending(candidate => candidate.Width).First();
        if (best.Width is > 0 and < 300)
        {
            // 信息框常只给 128/168px 的小缩略图，放到 230 高的大卡上会糊，改取原图。
            var original = Regex.Replace(best.Url,
                @"^(https://[^/]+)/images/thumb/([^/]+)/([^/]+)/([^/]+)/\d+px-[^/]+$", "$1/images/$2/$3/$4",
                RegexOptions.IgnoreCase);
            if (original != best.Url) return original;
        }

        return best.Url;
    }

    private async Task<string?> TryFetchSectionTextAsync(string title, CancellationToken token, int section)
    {
        try
        {
            var parseUrl = WikiApi + "?action=parse&prop=text&redirects=1&format=json&formatversion=2&page=" +
                           Uri.EscapeDataString(title) +
                           (section >= 0 ? "&section=" + section : "");
            var parseJson = await Http.GetStringAsync(parseUrl, token).ConfigureAwait(true);
            using var parseDoc = ParseWikiJson(parseJson);
            if (!parseDoc.RootElement.TryGetProperty("parse", out var parse)) return null;
            return parse.GetProperty("text").GetString();
        }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            return null;
        }
    }
    /// <summary>把 parse 返回的 HTML 压成可读纯文本：去表格/样式/引用，保留段落。</summary>
    internal static string CleanWikiText(string html, string title, int maxLength = 1100)
    {
        html = Regex.Replace(html, @"(?is)<table.*?</table>", " ");
        html = Regex.Replace(html, @"(?is)<style.*?</style>", " ");
        html = Regex.Replace(html, @"(?is)<(sup|figure|audio|nav|script).*?</\1>", " ");
        html = Regex.Replace(html, @"(?i)<br\s*/?>", "\n");
        html = Regex.Replace(html, @"(?is)</(p|div|li|h[1-6]|tr)>", "\n");
        // 内联标签（链接/加粗/斜体等）只去标签、保留文字，避免「有 4 个 RCS」这种被插空格
        html = Regex.Replace(html, @"(?i)</?(a|b|i|u|em|strong|code|sub|sup|mark|small|big|span)\b[^>]*>", "");
        html = Regex.Replace(html, @"<[^>]+>", " ");
        html = WebUtility.HtmlDecode(html);

        // KSP Wiki 的条目正文里偶尔残留原始 wiki 标记（模板展开失败、编辑器漏转义等）。
        // 这里按 MediaWiki 的展示语义还原成可见文字：链接显示标签文字，而不是整段抹掉。
        // 图片/文件引用没有可读文字，按原版那样丢弃；模板同理（不把 {{...}} 当正文）。
        html = Regex.Replace(html, @"(?is)\[\[(?:file|image|media)\s*:[^\[\]]*\]\]", " ");
        html = Regex.Replace(html, @"(?is)\{\{[^{}]*\}\}", " ");
        html = Regex.Replace(html, @"\[\[\s*[^\[\]\|]*\|\s*([^\[\]]*?)\s*\]\]", "$1");
        html = Regex.Replace(html, @"\[\[\s*([^\[\]]*?)\s*\]\]", "$1");
        html = Regex.Replace(html, @"(?i)\[\[?([a-z]{2}(?:-[a-z]{2})?):([^\[\]\|]*?)\]\]?", "$2", RegexOptions.IgnoreCase);
        // 外链：显示描述文字；只有裸链接时保留地址，别把整段吃掉
        html = Regex.Replace(html, @"(?i)\[https?://[^\s\]]+\s+([^\]]*)\]", "$1");
        html = Regex.Replace(html, @"\[#([^\[\]]*)\]", "$1");
        // 只剩一层方括号的残留：[atmosphere|大气] / [大气]
        html = Regex.Replace(html, @"\[([^\[\]\|\n]{1,60})\|([^\[\]\n]{1,60})\]", "$2");
        html = Regex.Replace(html, @"\[([^\[\]\n]{1,60})\]", "$1");
        // 粗斜体标记 ''''' / ''' / '' → 只保留文字
        html = Regex.Replace(html, @"'{2,5}", "");
        html = Regex.Replace(html, @"[ \t\u00A0]+", " ");
        html = Regex.Replace(html, @"\n[ \t]*", "\n");
        html = Regex.Replace(html, @"\n{2,}", "\n");

        var lines = html.Split('\n');
        var builder = new StringBuilder();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (string.Equals(line, title, StringComparison.Ordinal)) continue;
            if (builder.Length > 0) builder.Append('\n');
            builder.Append(line);
            if (builder.Length > maxLength) break;
        }

        var text = builder.ToString().Trim();
        if (text.Length <= maxLength) return text;
        var cut = text.Substring(0, maxLength);
        var dot = Math.Max(cut.LastIndexOf('.'),
            Math.Max(cut.LastIndexOf('。'), Math.Max(cut.LastIndexOf('!'), cut.LastIndexOf('?'))));
        return (dot > maxLength / 3 ? cut.Substring(0, dot + 1) : cut) + "…";
    }

    private static string StripLanguageSuffix(string title)
    {
        foreach (var suffix in new[]
                 {
                     "/zh-cn", "/zh-tw", "/zh", "/ru", "/ko", "/de", "/fr", "/es", "/ja", "/pl", "/pt", "/pt-br",
                     "/it", "/nl", "/tr", "/uk", "/cs", "/hu", "/sv"
                 })
            if (title.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return title.Substring(0, title.Length - suffix.Length);
        return title;
    }

    private static string BuildWikiPageUrl(string title)
    {
        var segments = title.Replace(' ', '_').Split('/');
        return "https://wiki.kerbalspaceprogram.com/wiki/" + string.Join("/", segments.Select(Uri.EscapeDataString));
    }

    private void BtnWikiNext_Click(object sender, EventArgs e) => _ = LoadWikiEntryAsync();

    /// <summary>整张说明卡都是「换一条」的点击区域，不必非要点到刷新图标。</summary>
    private void CardWikiBanner_Click(object sender, MouseButtonEventArgs e)
    {
        if (!BtnWikiNext.IsEnabled) return;
        e.Handled = true;
        BtnWikiNext_Click(sender, EventArgs.Empty);
    }

    private void BtnWikiOpen_Click(object sender, MouseButtonEventArgs e)
    {
        if (_wikiUrl.Length > 0) PCL.Core.App.Basics.OpenPath(_wikiUrl);
    }
    private void BtnCommunityClose_Click(object sender, EventArgs e)
    {
        CardCommunity.Visibility = Visibility.Collapsed;
    }
}