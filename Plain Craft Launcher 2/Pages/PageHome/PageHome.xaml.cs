using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;

namespace PCL.Pages;

public partial class PageHome
{
    private const string UpdateLogText =
        "2026-11-19\n" +
        "· 主页按原版结构恢复：左栏启动面板（含启动中动画、进度、阶段与取消），右栏公告 / 社区 / 更新日志卡片。\n" +
        "· 设置页「主页」可选择显示方式：空白、预设、本地 XAML 文件。\n" +
        "· 预设主页可选项接入原版中与 Minecraft 无关的第三方预设（Bangumi 番剧 / GitHub 仪表盘 / 今日新闻热点 / PCL CE 公告栏），联网拉取并缓存。\n" +
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

    public PageHome()
    {
        InitializeComponent();
        Unloaded += (_, _) => _presetCts?.Cancel();
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
        if (mode == 1)
        {
            LoadPreset(KspSettings.Current.HomePagePreset);
            return;
        }
        HideAllViews();
        if (mode == 2) LoadCustom(KspSettings.Current.HomePageLocalFile);
    }

    private void HideAllViews()
    {
        PanPreset.Visibility = Visibility.Collapsed;
        CardTrivia.Visibility = Visibility.Collapsed;
        CardPresetLoading.Visibility = Visibility.Collapsed;
        CardCustomError.Visibility = Visibility.Collapsed;
    }

    private void LoadPreset(int id)
    {
        var preset = HomePagePreset.Find(HomePagePreset.Resolve(id));
        _currentPreset = preset;
        _presetCts?.Cancel();
        HideAllViews();
        PanCustomHost.Content = null;
        PanCustomHost.Visibility = Visibility.Collapsed;

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
    private async Task LoadNetworkPresetAsync(HomePagePreset preset)
    {
        var cts = new CancellationTokenSource();
        _presetCts = cts;
        var token = cts.Token;

        CardCustomError.Visibility = Visibility.Collapsed;
        LabPresetLoading.Text = "正在加载「" + preset.Title + "」…";
        CardPresetLoading.Visibility = Visibility.Visible;

        var cache = Path.Combine(CacheDir, "preset-" + preset.Id + ".xaml");
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
            var text = await Http.GetStringAsync(preset.Url!, token).ConfigureAwait(true);
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
                if (!hasCache) ShowError("预设主页解析失败：" + parseError);
            }
        }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested) return;
            CardPresetLoading.Visibility = Visibility.Collapsed;
            if (!hasCache)
                ShowError("预设主页加载失败：" + ex.Message);
        }
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
        raw = raw.Replace("\uFEFF", string.Empty);
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
        LabWelcome.Text = "PCL KSP 修改版 v" + PCL.Core.App.Basics.VersionName;
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
        BtnPresetRetry.Visibility = _currentPreset.Url is not null ? Visibility.Visible : Visibility.Collapsed;
        CardCustomError.Visibility = Visibility.Visible;
    }

    private void BtnPresetRetry_Click(object sender, MouseButtonEventArgs e)
    {
        if (_currentPreset.Url is null) return;
        CardCustomError.Visibility = Visibility.Collapsed;
        _ = LoadNetworkPresetAsync(_currentPreset);
    }

    private void BtnCommunityClose_Click(object sender, EventArgs e)
    {
        CardCommunity.Visibility = Visibility.Collapsed;
    }
}