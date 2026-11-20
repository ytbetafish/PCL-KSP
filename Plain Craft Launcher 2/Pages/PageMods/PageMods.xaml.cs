using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using PCL;
using PCL.Translation;
using Pcl.Ckan;

namespace PCL.Pages;

public partial class PageMods
{
    private static ModService? _modService;
    private readonly Dictionary<string, string?> _transCache = new();
    private ModSearchResult? _selected;
    private bool _showChinese;
    private string? _originalDescription;
    private bool _serviceAttaching;
    private string _filter = "all";
    private IReadOnlyList<ModSearchResult> _lastResults = Array.Empty<ModSearchResult>();

    public PageMods()
    {
        InitializeComponent();
        Loaded += async (_, _) => await AttachKspBackgroundAsync();
    }

    private static ModService GetService()
    {
        ModService.ApplyNetworkSettings(
            KspSettings.Current.HttpProxy,
            KspSettings.Current.DownloadConcurrency,
            KspSettings.Current.HttpTimeoutSeconds);
        if (_modService is null)
        {
            _modService = new ModService(dataDir: KspSettings.Current.CkanDataDir);
        }
        _modService.MirrorUrls = new System.Collections.Generic.List<string>(KspSettings.Current.CkanMirrorUrls);
        _modService.OfficialRepoUrl = KspSettings.Current.CkanMasterRepoUrl;
        _modService.RepoStrategy = KspSettings.Current.CkanRepoStrategy;
        _modService.GitHubAccelerationEnabled = KspSettings.Current.GitHubAccelerationEnabled;
        _modService.GitHubAccelerationPrefix = KspSettings.Current.GitHubAccelerationPrefix;
        return _modService;
    }

    private async Task AttachKspBackgroundAsync()
    {
        if (_serviceAttaching) return;
        _serviceAttaching = true;
        try
        {
            var root = KspSettings.Current.KspRootPath;
            if (string.IsNullOrWhiteSpace(root) || !KspInstanceService.IsKspRoot(root)) return;
            PanLoad.Visibility = Visibility.Visible;
            await Task.Run(() =>
            {
                var ksp = new KspInstanceService();
                GetService().AttachInstance(ksp.Load(root));
            });
            await LoadInstalledAsync();
        }
        catch (Exception ex)
        {
            LabModDesc.Text = "KSP 实例附加失败：" + ex.Message;
        }
    }

    /// <summary>加载当前实例已安装的模组列表（默认视图）。</summary>
    private async Task LoadInstalledAsync()
    {
        try
        {
            _filter = "installed";
            _lastResults = await GetService().ListInstalledAsync();
            ApplyFilter();
            if (_lastResults.Count == 0)
                LabModDesc.Text = "当前实例尚未通过 CKAN 安装模组。";
        }
        catch (Exception ex)
        {
            LabModDesc.Text = "已安装模组加载失败：" + ex.Message;
        }
    }

    private async Task DoSearch(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            LabModDesc.Text = "输入关键词后按回车或点搜索。";
            return;
        }
        PanLoad.Visibility = Visibility.Visible;
        try
        {
            _lastResults = await GetService().SearchAsync(query);
            ApplyFilter();
        }
        catch (Exception ex)
        {
            MessageBox.Show("搜索失败：" + ex.Message, "PCL KSP", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ApplyFilter()
    {
        IEnumerable<ModSearchResult> q = _lastResults;
        q = _filter switch
        {
            "installed" => q.Where(m => m.IsInstalled),
            "updates" => q.Where(m => m.HasUpdate),
            "notinstalled" => q.Where(m => !m.IsInstalled),
            "compatible" => q.Where(m => m.IsCompatible),
            "incompatible" => q.Where(m => !m.IsCompatible),
            _ => q,
        };
        BuildList(q.ToList());
        PanLoad.Visibility = Visibility.Collapsed;
        PanListCard.Visibility = Visibility.Visible;
    }

    private void BuildList(IReadOnlyList<ModSearchResult> mods)
    {
        PanList.Children.Clear();
        if (mods.Count == 0)
        {
            PanList.Children.Add(new TextBlock
            {
                Text = "暂无模组",
                Margin = new Thickness(15, 10, 0, 10),
                Opacity = 0.6,
                FontSize = 13
            });
            return;
        }

        foreach (var mod in mods)
            PanList.Children.Add(CreateItem(mod));
    }

    private MyListItem CreateItem(ModSearchResult mod)
    {
        var info = new List<string>();
        if (!string.IsNullOrWhiteSpace(mod.Author)) info.Add(mod.Author!);
        if (!string.IsNullOrWhiteSpace(mod.Version)) info.Add(mod.Version!);
        info.Add(mod.HasUpdate ? "有更新" : mod.IsInstalled ? "已安装" : "未安装");
        if (!mod.IsCompatible) info.Add("不兼容");
        if (!string.IsNullOrEmpty(mod.MaxKsp)) info.Add("最高 KSP " + mod.MaxKsp);

        var item = new MyListItem
        {
            Title = mod.Name,
            Info = string.Join(" · ", info),
            tag = mod,
            Height = 46d,
            // Clickable：允许普通行点击触发 Click（Type=None 时 MouseDown 不会置位 _isMouseDown，Click 永不触发）；该类型不参与勾选。
            Type = MyListItem.CheckType.Clickable
        };
        if (IsImageUrl(mod.IconUrl))
        {
            item.Logo = mod.IconUrl!;
        }
        else
        {
            // 无可用图标时统一用 puzzle 占位：MyListItem 不支持下载页卡片那种首字母色块，
            // 强行改造共用控件会波及其它列表，故实例级列表保持单一 SVG 占位。
            item.SvgIcon = "lucide/puzzle";
        }
        item.Click += (_, _) => SelectMod(mod);
        return item;
    }

    private static bool IsImageUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return true;
        return url.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            || url.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
            || url.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
            || url.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);
    }

    private async void BtnSearch_Click(object sender, EventArgs e) => await DoSearch(TxtSearch.Text);

    private async void BtnFilterInstalled_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        await LoadInstalledAsync();
    }

    private async void BtnFilterUpdates_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _filter = "updates";
        try { _lastResults = await GetService().ListInstalledAsync(); } catch { }
        ApplyFilter();
    }

    private void BtnFilterNotInstalled_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _filter = "notinstalled";
        ApplyFilter();
    }

    private async void BtnFilterCompatible_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => await LoadAllThenFilter("compatible");

    private async void BtnFilterIncompatible_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => await LoadAllThenFilter("incompatible");

    /// <summary>加载全量目录后按兼容性过滤（CKAN 的 Compatible / Incompatible 视图）。</summary>
    private async Task LoadAllThenFilter(string filter)
    {
        PanLoad.Visibility = Visibility.Visible;
        try
        {
            await GetService().WarmUpAsync();
            _lastResults = await GetService().SearchAsync(null);
            _filter = filter;
            ApplyFilter();
        }
        catch (Exception ex)
        {
            LabModDesc.Text = "加载失败：" + ex.Message;
        }
        finally
        {
            PanLoad.Visibility = Visibility.Collapsed;
        }
    }

    private void BtnFilterAll_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _filter = "all";
        ApplyFilter();
    }

    private async void BtnUpdateAll_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        try
        {
            PanProgress.Visibility = Visibility.Visible;
            BarProgress.IsIndeterminate = true;
            var installed = await GetService().ListInstalledAsync();
            var updates = installed.Where(m => m.HasUpdate).ToList();
            if (updates.Count == 0)
            {
                PanProgress.Visibility = Visibility.Collapsed;
                MessageBox.Show("没有需要更新的模组。", "PCL KSP", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var progress = new Progress<string>(msg =>
                Dispatcher.BeginInvoke(new Action(() => LabProgress.Text = msg)));
            int i = 0;
            foreach (var m in updates)
            {
                i++;
                LabProgress.Text = $"正在更新 {m.Name}（{i}/{updates.Count}）…";
                var plan = await Task.Run(() => CkanPlanHelper.ResolvePlan(GetService(), m.Identifier));
                if (plan is not null) await GetService().ExecutePlanAsync(plan, progress);
            }
            PanProgress.Visibility = Visibility.Collapsed;
            MessageBox.Show($"已更新 {updates.Count} 个模组。", "PCL KSP", MessageBoxButton.OK, MessageBoxImage.Information);
            _filter = "updates";
            _lastResults = await GetService().ListInstalledAsync();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            PanProgress.Visibility = Visibility.Collapsed;
            MessageBox.Show("更新失败：" + ex.Message, "PCL KSP", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void SelectMod(ModSearchResult? mod)
    {
        _selected = mod;
        _showChinese = false;
        if (_selected is null)
        {
            LabModName.Text = "（选中一个模组）";
            LabModDesc.Text = "";
            LabModHome.Text = "";
            LabDeps.Visibility = Visibility.Collapsed;
            LabReverse.Visibility = Visibility.Collapsed;
            LabConflicts.Visibility = Visibility.Collapsed;
            BtnInstall.Visibility = Visibility.Collapsed;
            BtnUninstall.Visibility = Visibility.Collapsed;
            BtnTranslate.Visibility = Visibility.Collapsed;
            return;
        }
        LabModName.Text = _selected.Name;
        _originalDescription = _selected.Description;
        LabModDesc.Text = string.IsNullOrWhiteSpace(_originalDescription) ? "（无简介）" : _originalDescription;
        LabModHome.Text = string.IsNullOrWhiteSpace(_selected.Homepage) ? "" : _selected.Homepage;
        if (!_selected.IsCompatible)
        {
            LabModHome.Text = (LabModHome.Text + "　｜　不兼容当前 KSP（最高 " + (_selected.MaxKsp ?? "未知") + "）").Trim();
        }
        BtnInstall.Visibility = string.IsNullOrWhiteSpace(KspSettings.Current.KspRootPath) ? Visibility.Collapsed : Visibility.Visible;
        BtnUninstall.Visibility = _selected.IsInstalled ? Visibility.Visible : Visibility.Collapsed;
        BtnTranslate.Visibility = string.IsNullOrWhiteSpace(_originalDescription) ? Visibility.Collapsed : Visibility.Visible;

        // 拉一下依赖列表
        try
        {
            var plan = await GetService().GetInstallPlanAsync(_selected.Identifier);
            FillRelLab(LabDeps, "依赖：", plan?.ToInstall);

            var (rev, conflicts) = GetService().GetReverseAndConflicts(_selected.Identifier);
            FillRelLab(LabReverse, "被依赖：", rev);
            FillRelLab(LabConflicts, "冲突：", conflicts);
        }
        catch
        {
            LabDeps.Visibility = Visibility.Collapsed;
            LabReverse.Visibility = Visibility.Collapsed;
            LabConflicts.Visibility = Visibility.Collapsed;
        }
    }

    private const int RelNameDisplayCount = 5;

    /// <summary>填充依赖/被依赖/冲突标签：名单过长时只显示前几个，完整名单放入悬停提示。</summary>
    private static void FillRelLab(TextBlock lab, string prefix, IReadOnlyList<string>? names)
    {
        if (names is not { Count: > 0 })
        {
            lab.Visibility = Visibility.Collapsed;
            lab.ToolTip = null;
            return;
        }
        var shown = string.Join("、", names.Take(RelNameDisplayCount));
        lab.Text = names.Count > RelNameDisplayCount
            ? $"{prefix}{shown}（等 {names.Count} 个）"
            : prefix + shown;
        lab.ToolTip = prefix + string.Join("、", names);
        lab.Visibility = Visibility.Visible;
    }

    private async void BtnTranslate_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_selected is null || string.IsNullOrWhiteSpace(_selected.Description))
            return;

        _showChinese = !_showChinese;
        if (!_showChinese)
        {
            LabModDesc.Text = _originalDescription ?? "";
            return;
        }

        var key = _selected.Identifier;
        if (_transCache.TryGetValue(key, out var cached))
        {
            LabModDesc.Text = cached ?? "（翻译失败）";
            return;
        }

        var svc = new OpenAiTranslationService(new TranslationOptions
        {
            BaseUrl = KspSettings.Current.TranslateBaseUrl,
            ApiKey = KspSettings.Current.TranslateApiKey,
            Model = KspSettings.Current.TranslateModel
        });
        LabModDesc.Text = "翻译中…";
        try
        {
            var result = await svc.TranslateAsync(_selected.Description, "zh-CN");
            _transCache[key] = result;
            LabModDesc.Text = result ?? "（翻译失败，请检查翻译设置）";
        }
        catch (Exception ex)
        {
            Common.Log(ex, "[PageMods] 翻译简介失败");
            LabModDesc.Text = "（翻译失败：" + ex.Message + "）";
        }
    }

    private async void BtnInstall_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_selected is null) return;
        var allowIncompatible = false;
        if (!_selected.IsCompatible)
        {
            var answer = MessageBox.Show(
                $"「{_selected.Name}」声明最高兼容 KSP {_selected.MaxKsp ?? "未知"}，与当前实例不兼容。\n仍然继续安装吗？（可能无法正常加载）",
                "PCL KSP", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;
            allowIncompatible = true;
        }
        try
        {
            PanProgress.Visibility = Visibility.Visible;
            BarProgress.IsIndeterminate = true;
            LabProgress.Text = "正在解析依赖…";

            var plan = await Task.Run(() => CkanPlanHelper.ResolvePlan(GetService(), _selected.Identifier, allowIncompatible));
            if (plan is null)
            {
                PanProgress.Visibility = Visibility.Collapsed;
                MessageBox.Show("无法为该模组生成安装计划（未附加 KSP 实例？）。", "PCL KSP", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            LabProgress.Text = $"计划：安装 {plan.ToInstall.Count} 个，升级 {plan.ToUpgrade.Count} 个。";
            var progress = new Progress<string>(msg =>
            {
                Dispatcher.BeginInvoke(new Action(() => LabProgress.Text = msg));
            });

            await GetService().ExecutePlanAsync(plan, progress);

            PanProgress.Visibility = Visibility.Collapsed;
            MessageBox.Show("安装计划执行完成。", "PCL KSP", MessageBoxButton.OK, MessageBoxImage.Information);
            await DoSearch(TxtSearch.Text);
        }
        catch (Exception ex)
        {
            PanProgress.Visibility = Visibility.Collapsed;
            MessageBox.Show("安装失败：" + ex.Message, "PCL KSP", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnUninstall_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_selected is null) return;
        if (MessageBox.Show($"确定卸载 {_selected.Name}？", "PCL KSP",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            PanProgress.Visibility = Visibility.Visible;
            BarProgress.IsIndeterminate = true;
            LabProgress.Text = $"正在卸载 {_selected.Name}…";
            var progress = new Progress<string>(msg =>
                Dispatcher.BeginInvoke(new Action(() => LabProgress.Text = msg)));
            await GetService().UninstallAsync(_selected.Identifier, progress);
            PanProgress.Visibility = Visibility.Collapsed;
            MessageBox.Show("卸载完成。", "PCL KSP", MessageBoxButton.OK, MessageBoxImage.Information);
            await DoSearch(TxtSearch.Text);
        }
        catch (Exception ex)
        {
            PanProgress.Visibility = Visibility.Collapsed;
            MessageBox.Show("卸载失败：" + ex.Message, "PCL KSP", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnViewLog_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var text = GetService().Log.Dump();
        var win = new Window
        {
            Title = "PCL-KSP 操作日志",
            Width = 700,
            Height = 500,
            Content = new TextBox
            {
                Text = string.IsNullOrEmpty(text) ? "（暂无日志）" : text,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            }
        };
        win.ShowDialog();
    }
}
