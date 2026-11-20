using System;
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
        ImgWikiHero.ImageLoaded += () =>
        {
            ImgWikiHero.Visibility = Visibility.Visible;
            LabWikiHeroPlaceholder.Visibility = Visibility.Collapsed;
        };
        ImgWikiHero.ImageLoadFailed += () =>
        {
            ImgWikiHero.Visibility = Visibility.Collapsed;
            LabWikiHeroPlaceholder.Visibility = Visibility.Visible;
            LabWikiHeroPlaceholder.Text = "配图加载失败，可点「换一条」重试。";
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
            var randomUrl = WikiApi +
                            "?action=query&list=random&rnlimit=1&rnnamespace=0&format=json&formatversion=2";
            var title = string.Empty;
            // 随机池里会抽到首页/导航页，这类条目没有可读内容，重抽几次跳过。
            for (var attempt = 0; attempt < 4 && title.Length == 0; attempt++)
            {
                var randomJson = await Http.GetStringAsync(randomUrl, token).ConfigureAwait(true);
                using var randomDoc = JsonDocument.Parse(randomJson);
                var candidate = randomDoc.RootElement.GetProperty("query").GetProperty("random")[0]
                    .GetProperty("title").GetString() ?? string.Empty;
                var normalized = StripLanguageSuffix(candidate).Trim();
                if (normalized is "Main Page" or "Wiki Home" or "KSP Wiki") continue;
                title = candidate;
            }

            if (title.Length == 0) throw new InvalidOperationException("Wiki 未返回可用的条目名。");

            var baseTitle = StripLanguageSuffix(title);
            LabWikiTitle.Text = baseTitle;

            // 随机条目常是 /ru、/ko 这类语言子页：优先取中文/英文主条目正文，取不到再退回原条目。
            // 分类页、Parts/xxx 这类条目首页常常没有正文，这时改抓整页再压成文本。
            string? plain = null;
            string? usedTitle = null;
            string? usedHtml = null;
            foreach (var candidate in new[] { baseTitle + "/zh-cn", baseTitle, title })
            {
                if (plain is not null && plain.Length > 0) break;
                foreach (var section in new[] { 0, -1 })
                {
                    var html = await TryFetchSectionTextAsync(candidate, token, section).ConfigureAwait(true);
                    if (html is null) continue;
                    var text = CleanWikiText(html, StripLanguageSuffix(candidate));
                    if (text.Length == 0) continue;
                    plain = text;
                    usedTitle = candidate;
                    usedHtml = html;
                    break;
                }
            }

            _wikiUrl = BuildWikiPageUrl(usedTitle ?? baseTitle);
            BtnWikiOpen.IsEnabled = true;
            LabWikiText.Text = plain is { Length: > 0 }
                ? plain
                : "这个条目暂时取不到正文，点「在 Wiki 上打开」查看完整页面。";

            // 主卡大图与相关条目卡都在这里补上，形成「多卡片 + 每卡带图」的版式
            ShowWikiHeroImage(baseTitle, ExtractWikiImage(usedHtml));
            await LoadWikiRelatedAsync(usedTitle ?? baseTitle, baseTitle, token).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested) return;
            LabWikiTitle.Text = "KSP Wiki 条目加载失败";
            LabWikiText.Text = ex.Message + "\n点「换一条」重试。";
            LabWikiHeroPlaceholder.Visibility = Visibility.Visible;
            LabWikiHeroPlaceholder.Text = "条目加载失败";
        }
        finally
        {
            if (!token.IsCancellationRequested) BtnWikiNext.IsEnabled = true;
        }
    }

    /// <summary>把页面恢复成「加载中」的状态。</summary>
    private void ShowWikiLoading()
    {
        LabWikiTitle.Text = "正在拉取随机条目…";
        LabWikiText.Text = "";
        LabWikiHeroTag.Text = "";
        BorWikiHeroTag.Visibility = Visibility.Collapsed;
        ImgWikiHero.Visibility = Visibility.Collapsed;
        ImgWikiHero.Source = "";
        LabWikiHeroPlaceholder.Visibility = Visibility.Visible;
        LabWikiHeroPlaceholder.Text = "正在获取条目配图…";
        PanWikiRelated.Children.Clear();
        CardWikiRelatedHost.Visibility = Visibility.Collapsed;
        BtnWikiOpen.IsEnabled = false;
        BtnWikiNext.IsEnabled = false;
    }

    /// <summary>设置主卡的大横幅图；没有配图时退回文字占位。</summary>
    private void ShowWikiHeroImage(string title, string? imageUrl)
    {
        LabWikiHeroTag.Text = title;
        if (imageUrl is null)
        {
            BorWikiHeroTag.Visibility = Visibility.Collapsed;
            ImgWikiHero.Visibility = Visibility.Collapsed;
            ImgWikiHero.Source = "";
            LabWikiHeroPlaceholder.Visibility = Visibility.Visible;
            LabWikiHeroPlaceholder.Text = "这个条目在 Wiki 上没有配图";
            return;
        }

        BorWikiHeroTag.Visibility = Visibility.Visible;
        LabWikiHeroPlaceholder.Visibility = Visibility.Visible;
        LabWikiHeroPlaceholder.Text = "正在加载配图…";
        ImgWikiHero.Visibility = Visibility.Collapsed;
        ImgWikiHero.Source = imageUrl;
    }

    /// <summary>拉取主条目左下角的「相关条目」链接，逐张补上带图小卡。</summary>
    private async Task LoadWikiRelatedAsync(string title, string baseTitle, CancellationToken token)
    {
        var linksUrl = WikiApi +
                       "?action=query&prop=links&redirects=1&pllimit=40&plnamespace=0&format=json&formatversion=2&titles=" +
                       Uri.EscapeDataString(title);
        string[] candidates;
        try
        {
            var json = await Http.GetStringAsync(linksUrl, token).ConfigureAwait(true);
            using var doc = JsonDocument.Parse(json);
            var pages = doc.RootElement.GetProperty("query").GetProperty("pages");
            if (pages.GetArrayLength() == 0) return;
            if (!pages[0].TryGetProperty("links", out var linksElement)) return;
            candidates = linksElement.EnumerateArray()
                .Select(link => link.TryGetProperty("title", out var t) ? t.GetString() : null)
                .Where(link => IsUsableRelatedTitle(link, baseTitle))
                .Select(link => link!)
                .Take(8)
                .ToArray();
        }
        catch (Exception)
        {
            return;
        }

        var added = 0;
        foreach (var candidate in candidates)
        {
            if (added >= 3 || token.IsCancellationRequested) break;
            string? html = null;
            foreach (var section in new[] { 0, -1 })
            {
                html = await TryFetchSectionTextAsync(candidate, token, section).ConfigureAwait(true);
                if (!string.IsNullOrWhiteSpace(html)) break;
            }
            if (token.IsCancellationRequested) return;
            if (string.IsNullOrWhiteSpace(html)) continue;

            var snippet = CleanWikiText(html, StripLanguageSuffix(candidate), 200);
            if (snippet.Length < 40) continue;

            AddWikiRelatedCard(candidate, BuildWikiPageUrl(candidate), ExtractWikiImage(html), snippet);
            CardWikiRelatedHost.Visibility = Visibility.Visible;
            added++;
        }
    }

    /// <summary>相关条目候选：跳过版本号页、命名空间页、语言子页与自身。</summary>
    private static bool IsUsableRelatedTitle(string? title, string baseTitle)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;
        if (title.Contains('/') || title.Contains(':')) return false;
        if (title.Length > 46) return false;
        if (string.Equals(title, baseTitle, StringComparison.OrdinalIgnoreCase)) return false;
        if (Regex.IsMatch(title, @"^[0-9]+(\.[0-9]+)*$")) return false;
        return true;
    }

    /// <summary>代码构建一张「横幅图 + 摘要 + 打开按钮」的相关条目卡片。</summary>
    private void AddWikiRelatedCard(string title, string url, string? imageUrl, string snippet)
    {
        var card = new MyCard { Title = title, Margin = new Thickness(0, 0, 0, 12) };
        var stack = new StackPanel { Margin = new Thickness(0, 38, 0, 0) };

        var imageHost = new Grid { Height = 132, Margin = new Thickness(16, 0, 16, 0), ClipToBounds = true };
        var back = new Border { CornerRadius = new CornerRadius(4) };
        back.SetResourceReference(Border.BackgroundProperty, "ColorBrushSemiTransparent");
        imageHost.Children.Add(back);
        if (imageUrl is null)
        {
            var placeholder = new TextBlock
            {
                Text = "该条目没有配图",
                FontSize = 12,
                Opacity = 0.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            imageHost.Children.Add(placeholder);
        }
        else
        {
            var image = new MyImage
            {
                Source = imageUrl,
                Stretch = Stretch.UniformToFill,
                CornerRadius = new CornerRadius(4)
            };
            imageHost.Children.Add(image);
        }

        stack.Children.Add(imageHost);

        var text = new TextBlock
        {
            Text = snippet,
            Margin = new Thickness(20, 9, 20, 0),
            FontSize = 12.5,
            Opacity = 0.75,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap
        };
        stack.Children.Add(text);

        var button = new MyButton
        {
            Text = "在 Wiki 上打开",
            MinWidth = 120,
            Height = 28,
            Padding = new Thickness(13, 0, 13, 0),
            Margin = new Thickness(20, 10, 0, 16),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        button.Click += (_, _) => PCL.Core.App.Basics.OpenPath(url);
        stack.Children.Add(button);

        card.Children.Add(stack);
        PanWikiRelated.Children.Add(card);
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
            using var parseDoc = JsonDocument.Parse(parseJson);
            if (!parseDoc.RootElement.TryGetProperty("parse", out var parse)) return null;
            return parse.GetProperty("text").GetString();
        }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            return null;
        }
    }
    /// <summary>把 parse 返回的 HTML 压成可读纯文本：去表格/样式/引用，保留段落。</summary>
    private static string CleanWikiText(string html, string title, int maxLength = 1100)
    {
        html = Regex.Replace(html, @"(?is)<table.*?</table>", " ");
        html = Regex.Replace(html, @"(?is)<style.*?</style>", " ");
        html = Regex.Replace(html, @"(?is)<(sup|figure|audio|nav|script).*?</\1>", " ");
        html = Regex.Replace(html, @"(?i)<br\s*/?>", "\n");
        html = Regex.Replace(html, @"(?is)</(p|div|li|h[1-6]|tr)>", "\n");
        html = Regex.Replace(html, @"<[^>]+>", " ");
        html = WebUtility.HtmlDecode(html);
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

    private void BtnWikiOpen_Click(object sender, MouseButtonEventArgs e)
    {
        if (_wikiUrl.Length > 0) PCL.Core.App.Basics.OpenPath(_wikiUrl);
    }
    private void BtnCommunityClose_Click(object sender, EventArgs e)
    {
        CardCommunity.Visibility = Visibility.Collapsed;
    }
}