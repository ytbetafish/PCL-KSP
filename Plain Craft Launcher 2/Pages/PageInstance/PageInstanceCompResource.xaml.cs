using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Pcl.Ckan;
using PCL.Core.UI;
using PCL.Core.UI.Dialogs;
using PCL.Core.UI.Hints;
using System.Windows.Controls.Primitives;

namespace PCL;

/// <summary>
/// KSP 实例设置页 · 模组（复刻原版 PageInstanceCompResource 本地资源管理器骨架）。
/// 原版 MC 的 .jar 扫描、启用禁用、更新、删除、导出、JarInJar、资源包/光影/投影分支已裁剪。
/// 列表数据源为 CKAN 注册表的真实已装模组（名称、作者、版本、图标），对应原版 LocalCompFile 的
/// Name / Authors / Version / Logo / Description 字段；卡片行沿用原版通用控件 MyCompItem。
/// 第一阶段仅复刻多选交互 UI（勾选、全选、选中计数与底部操作栏），批量卸载/更新等真实动作后续接入。
/// </summary>
public partial class PageInstanceCompResource : IPageView
{
    private static ModService? _modService;
    private readonly List<ModSearchResult> _results = new();
    private string? _query;
    private bool _refreshed;
    private bool _serviceAttaching;

    /// <summary>当前选中的模组 Identifier 集合（对齐原版 selectedMods，键由 RawPath 改为 Identifier）。</summary>
    private readonly HashSet<string> _selected = new();
    private int _bottomBarShownCount;

    /// <summary>当前类别筛选（对齐原版 FilterType；启用/禁用因 CKAN 无禁用态已裁剪）。</summary>
    private FilterType _filter = FilterType.All;

    /// <summary>当前排序方式（对齐原版 SortMethod；文件名/文件大小因无数据已裁剪）。</summary>
    private SortMethod _sort = SortMethod.Name;

    private enum FilterType
    {
        All = 0,

        CanUpdate = 3,
        Duplicate = 5,

        /// <summary>收藏（PCL-KSP 扩展：筛选已收藏的已装模组）。</summary>
        Favorites = 6
    }

    private enum SortMethod
    {
        Name,
        Downloads,
        ReleaseDate
    }

    public PageInstanceCompResource()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            RefreshOnNavigate();
            ScheduleVisibilityFix(900);
        };
        Unloaded += (_, _) => _refreshed = false;
    }

    public void OnNavigatedTo(object? parameter) => RefreshOnNavigate();

    public void OnNavigatedFrom()
    {
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
        _modService.MirrorUrls = new List<string>(KspSettings.Current.CkanMirrorUrls);
        _modService.OfficialRepoUrl = KspSettings.Current.CkanMasterRepoUrl;
        _modService.RepoStrategy = KspSettings.Current.CkanRepoStrategy;
        _modService.GitHubAccelerationEnabled = KspSettings.Current.GitHubAccelerationEnabled;
        _modService.GitHubAccelerationPrefix = KspSettings.Current.GitHubAccelerationPrefix;
        return _modService;
    }

    private async void RefreshOnNavigate()
    {
        if (_refreshed)
            return;
        _refreshed = true;

        PanLoad.Visibility = Visibility.Visible;
        Load.State = new MyLoadingStateSimulator { LoadingState = LoadingState.Run };
        PanListCard.Visibility = Visibility.Collapsed;
        PanEmpty.Visibility = Visibility.Collapsed;

        try
        {
            await AttachInstanceAsync();

            // 第一阶段：直接读注册表里的已装模组先渲染出来（不枚举 CKAN 目录，秒开）
            var quick = await Task.Run(() => GetService().ListInstalledQuickAsync()).ConfigureAwait(true);
            _results.Clear();
            _results.AddRange(quick);
            _selected.Clear();
            _bottomBarShownCount = 0;
            RenderList();
            PanLoad.Visibility = Visibility.Collapsed;

            // 第二阶段：后台补齐可更新标记、依赖、冲突与下载量，再刷新一次（勾选状态保留）
            var full = await Task.Run(() => GetService().ListInstalledAsync()).ConfigureAwait(true);
            _results.Clear();
            _results.AddRange(full);
        }
        catch (Exception ex)
        {
            Common.Log(ex, "模组列表加载失败");
            _results.Clear();
        }

        RenderList();
        PanLoad.Visibility = Visibility.Collapsed;
    }


    private async Task AttachInstanceAsync()
    {
        if (_serviceAttaching)
            return;
        _serviceAttaching = true;

        var root = KspSettings.Current.KspRootPath;
        if (string.IsNullOrWhiteSpace(root) || !KspInstanceService.IsKspRoot(root))
            return;

        await Task.Run(() =>
        {
            var ksp = new KspInstanceService();
            GetService().AttachInstanceRobust(ksp.Load(root));
        });
    }

    private void SearchRun(object sender, EventArgs e)
    {
        _query = SearchBox.Text?.Trim();
        RenderList();
    }

    private void RenderList()
    {
        PanList.Children.Clear();

        IEnumerable<ModSearchResult> queryResult = _results;
        if (!string.IsNullOrWhiteSpace(_query))
        {
            queryResult = queryResult.Where(m =>
                m.Name.Contains(_query, StringComparison.OrdinalIgnoreCase) ||
                m.Identifier.Contains(_query, StringComparison.OrdinalIgnoreCase) ||
                (m.Author?.Contains(_query, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        UpdateFilterCounts(queryResult);

        var list = ApplySort(queryResult.Where(CanPassFilter)).ToList();

        // 搜索/筛选后仅保留仍可见项的选中（对齐原版 670-671）。
        var kept = _selected.Where(k => list.Any(m => m.Identifier == k)).ToList();
        _selected.Clear();
        foreach (var key in kept)
            _selected.Add(key);

        if (list.Count == 0)
        {
            PanListCard.Visibility = Visibility.Collapsed;
            PanEmpty.Visibility = Visibility.Visible;
            TxtEmptyDescription.Text = string.IsNullOrEmpty(KspSettings.Current.KspRootPath)
                ? "未选择 KSP 实例"
                : "当前实例尚未通过 CKAN 安装模组";
            RefreshSelectBar();
            return;
        }

        PanEmpty.Visibility = Visibility.Collapsed;
        PanListCard.Visibility = Visibility.Visible;
        foreach (var mod in list)
            PanList.Children.Add(CreateItem(mod));

        ResetAnimState(PanListCard);
        ResetAnimState(PanEmpty);
        ScheduleVisibilityFix(400);
        RefreshSelectBar();
    }

    /// <summary>当前批次中 Identifier 重复（出现超过一次）的模组键集合，供“重复”筛选使用。</summary>
    private readonly HashSet<string> _duplicateKeys = new();

    /// <summary>统计各分类计数并刷新筛选标签文本与显隐（对齐原版 687-722）。</summary>
    private void UpdateFilterCounts(IEnumerable<ModSearchResult> source)
    {
        var all = source.ToList();
        var anyCount = all.Count;

        var updateCount = all.Count(m => m.HasUpdate);

        _duplicateKeys.Clear();
        foreach (var g in all.GroupBy(m => m.Identifier).Where(g => g.Count() > 1))
            _duplicateKeys.Add(g.Key);
        var duplicateCount = all.Count(m => _duplicateKeys.Contains(m.Identifier));

        var favoriteCount = all.Count(m => Comp.CompFavorites.IsFavourite(m.Identifier));

        BtnFilterAll.Text = $"全部 ({anyCount})";
        BtnFilterFavorites.Text = $"收藏 ({favoriteCount})";
        BtnFilterCanUpdate.Text = $"可更新 ({updateCount})";
        BtnFilterDuplicate.Text = $"重复 ({duplicateCount})";

        BtnFilterFavorites.Visibility = _filter == FilterType.Favorites || favoriteCount > 0
            ? Visibility.Visible : Visibility.Collapsed;
        BtnFilterCanUpdate.Visibility = _filter == FilterType.CanUpdate || updateCount > 0
            ? Visibility.Visible : Visibility.Collapsed;
        BtnFilterDuplicate.Visibility = _filter == FilterType.Duplicate || duplicateCount > 0
            ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>判断模组是否通过当前类别筛选（对齐原版 CanPassFilter）。</summary>
    private bool CanPassFilter(ModSearchResult m) => _filter switch
    {

        FilterType.Favorites => Comp.CompFavorites.IsFavourite(m.Identifier),
        FilterType.CanUpdate => m.HasUpdate,
        FilterType.Duplicate => _duplicateKeys.Contains(m.Identifier),
        _ => true
    };

    /// <summary>按当前排序方式排序（对齐原版 DoSort）。</summary>
    private IEnumerable<ModSearchResult> ApplySort(IEnumerable<ModSearchResult> source) => _sort switch
    {
        SortMethod.Downloads => source.OrderByDescending(m => m.Downloads),
        SortMethod.ReleaseDate => source.OrderByDescending(m => m.ReleaseDate ?? DateTime.MinValue),
        _ => source.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
    };

    private static string GetSortName(SortMethod method) => method switch
    {
        SortMethod.Downloads => "下载量",
        SortMethod.ReleaseDate => "发布日期",
        _ => "资源名称"
    };

    private void SetSortMethod(SortMethod target)
    {
        _sort = target;
        BtnSort.Text = $"排序：{GetSortName(target)}";
        RenderList();
    }

    private void BtnSortClick(object sender, Common.RouteEventArgs e)
    {
        var body = new ContextMenu();
        foreach (SortMethod method in Enum.GetValues(typeof(SortMethod)))
        {
            var item = new MyMenuItem { Header = GetSortName(method) };
            item.Click += (_, _) => SetSortMethod(method);
            body.Items.Add(item);
        }
        body.PlacementTarget = (UIElement)sender;
        body.Placement = PlacementMode.Bottom;
        body.IsOpen = true;
    }

    private void ChangeFilter(MyRadioButton sender, bool raiseByMouse)
    {
        _filter = (FilterType)Convert.ToInt32(sender.Tag);
        RenderList();
    }

    private static readonly Brush[] _initialBrushes =
    [
        new SolidColorBrush(Color.FromRgb(0x5B, 0x6E, 0xE1)),
        new SolidColorBrush(Color.FromRgb(0x3D, 0x99, 0x70)),
        new SolidColorBrush(Color.FromRgb(0xE8, 0x83, 0x3A)),
        new SolidColorBrush(Color.FromRgb(0xC8, 0x50, 0x6B)),
        new SolidColorBrush(Color.FromRgb(0x4A, 0x90, 0xA4)),
        new SolidColorBrush(Color.FromRgb(0x8E, 0x7C, 0xC3))
    ];

    private MyCompItem CreateItem(ModSearchResult mod)
    {
        var info = new List<string>();
        if (!string.IsNullOrWhiteSpace(mod.Author))
            info.Add(mod.Author!);
        if (!string.IsNullOrWhiteSpace(mod.Version))
            info.Add(mod.Version!);
        info.Add(mod.HasUpdate ? "有更新" : "已安装");

        var tooltipLines = new List<string>();
        if (mod.Depends is { Count: > 0 })
        {
            info.Add($"前置 {mod.Depends.Count}");
            tooltipLines.Add(FormatRelList("依赖：", mod.Depends)!);
        }
        if (mod.ReverseDeps is { Count: > 0 })
        {
            info.Add($"被依赖 {mod.ReverseDeps.Count}");
            tooltipLines.Add(FormatRelList("被依赖：", mod.ReverseDeps)!);
        }
        if (mod.Conflicts is { Count: > 0 })
        {
            info.Add($"冲突 {mod.Conflicts.Count}");
            tooltipLines.Add(FormatRelList("冲突：", mod.Conflicts)!);
        }

        var project = CkanCompAdapter.ToProject(mod);
        var item = new MyCompItem { Tag = project };
        // 实例本地模组卡片不提供收藏（对齐原版：收藏仅属于下载在线卡片）。
        item.ShowFavoriteBtn = false;
        // 多选模式：单击切换勾选，info 按钮进入详情；按 Identifier 回填选中态。
        item.IsCheckable = true;
        item.ShowDetailBtn = true;
        item.SelectKey = mod.Identifier;
        item.Changed += OnItemChanged;
        item.SetChecked(_selected.Contains(mod.Identifier), false, false);
        item.Title = mod.Name;
        item.Description = string.Join(" · ", info);
        item.ShowMetaRow = false;
        if (tooltipLines.Count > 0)
        {
            item.ToolTip = new ToolTip
            {
                MaxWidth = 360,
                Content = new TextBlock
                {
                    Text = string.Join(Environment.NewLine, tooltipLines),
                    TextWrapping = TextWrapping.Wrap
                }
            };
        }

        item.Letter = GetInitial(mod.Name);
        item.LetterBrush = GetInitialBrush(mod.Name);
        if (IsImageUrl(mod.IconUrl))
        {
            item.Logo = mod.IconUrl!;
            item.ShowLetter = false;
        }
        else
        {
            item.ShowLetter = true;
        }
        return item;
    }

    private const int RelTooltipMaxNames = 5;

    private static string? FormatRelList(string prefix, IReadOnlyList<string>? names)
    {
        if (names is not { Count: > 0 }) return null;
        var text = prefix + string.Join("、", names.Take(RelTooltipMaxNames));
        if (names.Count > RelTooltipMaxNames)
            text += "（等 " + names.Count + " 个）";
        return text;
    }

    private static string GetInitial(string? name)
    {
        name = (name ?? string.Empty).Trim();
        if (name.Length == 0)
            return "?";
        var ch = name[0];
        return char.IsLetter(ch) ? char.ToUpperInvariant(ch).ToString() : ch.ToString();
    }

    private static Brush GetInitialBrush(string key)
    {
        var hash = 0;
        foreach (var c in key ?? string.Empty)
            hash = unchecked(hash * 31 + c);
        return _initialBrushes[Math.Abs(hash) % _initialBrushes.Length];
    }

    private void BtnOpenFolder_Click(object sender, MouseButtonEventArgs e) => OpenGameData();

    /// <summary>跳转到下载页搜索在线模组（对齐原版“下载新资源”）。</summary>
    private void BtnManageDownload_Click(object sender, MouseButtonEventArgs e)
    {
        if (Window.GetWindow(this) is FormMain fm)
            fm.PageChange(FormMain.PageType.Download);
    }

    /// <summary>导出已安装模组清单为 TXT / CSV（对齐原版 BtnManageInfoExport_Click）。</summary>
    private void BtnManageInfoExport_Click(object sender, MouseButtonEventArgs e)
    {
        if (_results.Count == 0)
        {
            DialogCompat.ShowHint("当前没有可导出的模组", NotificationType.Info);
            return;
        }

        var choice = (DialogService.Instance.Show(new TextDialogRequest
        {
            Title = "导出模组信息",
            Content = "选择导出格式：",
            Buttons = DialogButtons.From("文本 (.txt)", "CSV (.csv)", "取消"),
            ForceWait = true
        }).Result?.Button ?? 0);
        if (choice is 0 or 3)
            return; // 0 = 未点击，3 = 取消

        var rows = _results.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var instanceName = string.IsNullOrWhiteSpace(KspSettings.Current.KspRootPath)
            ? "KSP" : Path.GetFileName(KspSettings.Current.KspRootPath.TrimEnd('\\', '/'));

        string content;
        string filter;
        string ext;
        if (choice == 2)
        {
            var lines = new List<string> { "名称,Identifier,版本,作者,下载量,发布日期" };
            lines.AddRange(rows.Select(m =>
                $"{Csv(m.Name)},{Csv(m.Identifier)},{Csv(m.Version)},{Csv(m.Author)},{m.Downloads},{m.ReleaseDate:yyyy-MM-dd}"));
            content = string.Join("\r\n", lines);
            filter = "CSV 文件|*.csv";
            ext = ".csv";
        }
        else
        {
            content = string.Join("\r\n", rows.Select(m =>
                $"{m.Name} | {m.Version} | {m.Author} | {m.Identifier}"));
            filter = "文本文件|*.txt";
            ext = ".txt";
        }

        try
        {
            var savePath = SystemDialogs.SelectSaveFile("保存模组清单", instanceName + " 已安装模组清单" + ext, filter);
            if (string.IsNullOrWhiteSpace(savePath))
                return;
            File.WriteAllText(savePath, content, new System.Text.UTF8Encoding(true));
            Common.OpenExplorer(savePath);
            DialogCompat.ShowHint($"已导出 {rows.Count} 个模组", NotificationType.Success);
        }
        catch (Exception ex)
        {
            Common.Log(ex, "导出模组信息失败");
            DialogCompat.ShowHint("导出失败：" + ex.Message, NotificationType.Error);
        }
    }

    private static string Csv(string? value)
    {
        value = value ?? string.Empty;
        return value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    #region 多选与选择栏

    /// <summary>单张卡片勾选变化：同步选中集合并刷新底部栏（对齐原版 CheckChanged）。</summary>
    private void OnItemChanged(MyCompItem sender, Common.RouteEventArgs e)
    {
        if (Animations.AniExitsAnyControl())
            return;
        if (sender.SelectKey is not { } key)
            return;
        if (sender.Checked)
            _selected.Add(key);
        else
            _selected.Remove(key);
        RefreshSelectBar();
    }

    /// <summary>全选 / 取消全选（对齐原版 BtnManageSelectAll_Click）。</summary>
    private void BtnManageSelectAll_Click(object sender, MouseButtonEventArgs e)
    {
        ChangeAllSelected(_selected.Count < PanList.Children.Count);
    }

    /// <summary>切换所有可见卡片的勾选态并重建选中集合（对齐原版 ChangeAllSelected）。</summary>
    private void ChangeAllSelected(bool value)
    {
        Animations.AniBeginControl();
        try
        {
            _selected.Clear();
            foreach (var raw in PanList.Children)
            {
                if (raw is not MyCompItem item)
                    continue;
                var shouldSelected = value && item.SelectKey is not null;
                item.SetChecked(shouldSelected, false, true);
                if (shouldSelected)
                    _selected.Add(item.SelectKey!);
            }
        }
        finally
        {
            Animations.AniEndControl();
        }

        RefreshSelectBar();
    }

    /// <summary>刷新底部多选栏的计数与显隐动画（对齐原版 RefreshBars 底部栏部分，801-827）。</summary>
    private void RefreshSelectBar()
    {
        var newCount = _selected.Count;
        var selected = newCount > 0;
        if (selected)
            LabSelect.Text = $"已选择 {newCount} 个模组";

        // 按选中项计算底部按钮可用性（对齐原版 740-782；启用/禁用因无禁用态裁剪）。
        var selectedMods = _results.Where(m => _selected.Contains(m.Identifier)).ToList();
        BtnSelectUpdate.IsEnabled = selectedMods.Any(m => m.HasUpdate);

        BtnSelectFavorites.IsEnabled = selectedMods.Count > 0;
        BtnSelectShare.IsEnabled = selectedMods.Count > 0;
        BtnSelectDelete.IsEnabled = selectedMods.Count > 0;

        // 选中时给列表留出浮栏高度，避免底部内容被浮栏永久遮住（对齐原版 ListBackMargin 95 / 15）。
        PanMain.Margin = new System.Windows.Thickness(25, 10, 25, selected ? 95 : 10);

        // 以控件实际可见状态为准：动画被打断后若仍按计数判断，会把浮栏永久卡在隐藏态。
        var nowVisible = CardSelect.Visibility == Visibility.Visible;
        _bottomBarShownCount = newCount;
        Animations.AniStop("Mod Sidebar");

        if (selected)
        {
            CardSelect.Visibility = Visibility.Visible;
            if (nowVisible)
            {
                CardSelect.Opacity = 1d;
                TransSelect.Y = -25;
                return;
            }

            Animations.AniStart(
                    new[]
                    {
                        Animations.AaOpacity(CardSelect, 1d - CardSelect.Opacity, 60),
                        Animations.AaTranslateY(CardSelect, -27 - TransSelect.Y, 120,
                            ease: new Animations.AniEaseOutFluent(Animations.AniEasePower.Weak)),
                        Animations.AaTranslateY(CardSelect, 3d, 150, 120,
                            new Animations.AniEaseInoutFluent(Animations.AniEasePower.Weak)),
                        Animations.AaTranslateY(CardSelect, -1, 90, 270,
                            new Animations.AniEaseInoutFluent(Animations.AniEasePower.Weak))
                    }, "Mod Sidebar");
            }
        else
        {
            if (!nowVisible)
            {
                CardSelect.Opacity = 0d;
                TransSelect.Y = -10;
                return;
            }

            Animations.AniStart(
                new[]
                {
                    Animations.AaOpacity(CardSelect, -CardSelect.Opacity, 90),
                    Animations.AaTranslateY(CardSelect, -10 - TransSelect.Y, 90,
                        ease: new Animations.AniEaseInFluent(Animations.AniEasePower.Weak)),
                    Animations.AaCode(() => CardSelect.Visibility = Visibility.Collapsed, after: true)
                }, "Mod Sidebar");
        }
    }

    /// <summary>取消选择：清空全部勾选（对齐原版 BtnSelectCancel_Click）。</summary>
    private void BtnSelectCancel_Click(object sender, Common.RouteEventArgs e) => ChangeAllSelected(false);

    /// <summary>当前选中项对应的模组数据（对齐原版 selectedMods 反查）。</summary>
    private List<ModSearchResult> SelectedMods()
        => _results.Where(m => _selected.Contains(m.Identifier)).ToList();

    /// <summary>批量动作结束后重新拉取已装列表并重绘（保留搜索词与筛选）。</summary>
    private async Task ReloadListAsync()
    {
        try
        {
            var list = await Task.Run(() => GetService().ListInstalledAsync()).ConfigureAwait(true);
            _results.Clear();
            _results.AddRange(list);
        }
        catch (Exception ex)
        {
            Common.Log(ex, "重新加载模组列表失败");
        }

        RenderList();
    }

    private static string FailSuffix(List<string> failed)
    {
        if (failed.Count == 0) return "";
        var names = string.Join("、", failed.Take(3));
        return $"，失败 {failed.Count} 个（{names}{(failed.Count > 3 ? " 等" : "")}）";
    }

    private bool _busy;

    /// <summary>判断异常是否只是用户主动取消（点了任务卡片上的 ×），这种情况不应报错。</summary>
    private static bool IsUserCancel(Exception? ex)
    {
        while (ex is not null)
        {
            if (ex is OperationCanceledException)
                return true;
            if (ex.GetType().Name == "CancelledActionKraken")
                return true;
            ex = ex.InnerException;
        }

        return false;
    }


    /// <summary>
    /// 可见性自愈：页面进入动画被打断时，参与动画的控件会停在 Opacity 0，
    /// 表现为按钮/文字“鼠标碰到才出现”。延迟一拍把本页可见控件强制置为不透明；
    /// 行内背景 Border 与阴影 Decorator 由 hover 自行管理，跳过。
    /// </summary>
    private void ScheduleVisibilityFix(int delayMs)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(delayMs)
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                try
                {
                    ForceVisible(PanMain);
                }
                catch (Exception ex)
                {
                    Common.Log(ex, "恢复控件可见性失败");
                }
            };
            timer.Start();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private static void ForceVisible(DependencyObject root)
    {
        if (root is null)
            return;
        if (root is FrameworkElement fe)
        {
            if (fe.Visibility != Visibility.Visible)
                return;
            if (fe is Border || fe is Decorator)
                return;
            if (fe.Opacity < 1d)
                fe.Opacity = 1d;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
            ForceVisible(VisualTreeHelper.GetChild(root, i));
    }

    /// <summary>批量更新：对选中且可更新的模组依次应用 CKAN 安装计划（对齐原版 BtnSelectUpdate_Click）。</summary>
    private async void BtnSelectUpdate_Click(object sender, Common.RouteEventArgs e)
    {
        var targets = SelectedMods().Where(m => m.HasUpdate).ToList();
        if (targets.Count == 0)
        {
            DialogCompat.ShowHint("所选模组均没有可用更新", NotificationType.Info);
            return;
        }

        var choice = DialogService.Instance.Show(new TextDialogRequest
        {
            Title = "更新模组",
            Content = $"将更新 {targets.Count} 个模组：下载新版本并覆盖现有文件，必要时一并处理依赖变更。期间请勿关闭启动器。",
            Buttons = DialogButtons.From("更新", "取消"),
            ForceWait = true
        }).Result?.Button ?? 0;
        if (choice != 1)
            return; // 按钮编号从 1 开始：1 = 确认，0 = 未点击，2 = 取消

        if (_busy)
        {
            // 上一次批量操作可能还在收尾（取消后 CKAN 需要时间自行退出），排到它结束再继续，而不是静默丢弃这次点击
            Common.Log("[ModUpdate] 上一次操作尚未收尾，本次点击排队等待");
            DialogCompat.ShowHint("上一次操作正在收尾，稍后自动继续…", NotificationType.Info);
            while (_busy)
                await Task.Delay(150).ConfigureAwait(true);
            Common.Log("[ModUpdate] 上一次操作已结束，继续本次操作");
        }

        _busy = true;
       var ok = 0;
        var failed = new List<string>();
        var service = GetService();

        // 把这次更新作为任务挂到任务管理页（右下角下载按钮），与其它下载任务并列显示
        var loader = new CkanUpdateLoader($"更新 {targets.Count} 个模组", targets.Select(t => t.Name));
        Loading.loaderTaskbar.Add(loader);
        Launcher.frmMain?.BtnExtraDownload.ShowRefresh();

        void OnCkanProgress(string msg, int percent) => loader.Report(percent);

        service.Log.ProgressReported += OnCkanProgress;
        void OnCkanSpeed(long bytesPerSecond) => loader.ReportSpeed(bytesPerSecond);
        service.SpeedReported += OnCkanSpeed;
        IProgress<string>? progress = null;
        var index = 0;
        Common.Log($"[ModUpdate] 开始批量更新 {targets.Count} 个模组，可取消={true}");
        loader.StartTask();        try
        {
            foreach (var m in targets)
            {
                if (loader.IsCancelled)
                    break;

                index++;
                var current = m;
               try
                {
                    // CKAN 的依赖解析与下载安装都是同步阻塞调用，必须放到后台线程，否则界面会卡死
                    var mod = current;
                    var plan = await Task.Run(() => service.GetInstallPlanAsync(mod.Identifier)).ConfigureAwait(true);
                    if (plan is null || !string.IsNullOrEmpty(plan.FailureReason))
                    {
                        failed.Add(mod.Name);
                        loader.Current?.Fail(new InvalidOperationException(plan?.FailureReason ?? "未找到安装计划"));
                    }
                    else
                    {
                        await Task.Run(() => service.ExecutePlanAsync(plan, progress, loader.Token)).ConfigureAwait(true);
                        ok++;
                    }
                }
                catch (Exception ex) when (IsUserCancel(ex))
                {
                    // 用户主动取消，不算失败
                    break;
                }
                catch (Exception ex)
                {
                    Common.Log(ex, "批量更新失败：" + current.Identifier);
                    failed.Add(current.Name);
                    loader.Current?.Fail(ex);
                }

                loader.Advance();
            }
        }
        finally
        {
            service.Log.ProgressReported -= OnCkanProgress;
            service.SpeedReported -= OnCkanSpeed;
            if (!loader.IsCancelled)
                loader.Finish();
            Launcher.frmMain?.BtnExtraDownload.ShowRefresh();
           _busy = false;
        }

        ChangeAllSelected(false);
        await ReloadListAsync();
        DialogCompat.ShowHint($"已更新 {ok} 个模组{FailSuffix(failed)}",
            failed.Count == 0 ? NotificationType.Success : NotificationType.Info);
    }

    /// <summary>批量收藏：把选中模组加入收藏夹（当前写入默认收藏夹）。</summary>
    private void BtnSelectFavorites_Click(object sender, Common.RouteEventArgs e)
    {
        var targets = SelectedMods();
        if (targets.Count == 0)
            return;

        try
        {
            var list = Comp.CompFavorites.FavoritesList;
            if (list is null || list.Count == 0)
            {
                list = new List<Comp.CompFavorites.FavData>
                {
                    Comp.CompFavorites.GetNewFav("默认收藏夹", null)
                };
                Comp.CompFavorites.FavoritesList = list;
            }

            var target = list[0];
            var added = 0;
            foreach (var m in targets.Where(m => !Comp.CompFavorites.IsFavourite(m.Identifier)))
            {
                target.Favs.Add(m.Identifier);
                added++;
            }

            Comp.CompFavorites.Save();
            ChangeAllSelected(false);
            RenderList();
            DialogCompat.ShowHint(
                added > 0
                    ? $"已将 {added} 个模组加入收藏夹「{target.Name}」"
                    : "所选模组均已在收藏夹中",
                added > 0 ? NotificationType.Success : NotificationType.Info);
        }
        catch (Exception ex)
        {
            Common.Log(ex, "批量收藏失败");
            DialogCompat.ShowHint("收藏失败：" + ex.Message, NotificationType.Error);
        }
    }

    /// <summary>分享所选：把选中模组的 Identifier 列表生成分享码并复制到剪贴板（对齐原版 BtnSelectShare_Click）。</summary>
    private void BtnSelectShare_Click(object sender, Common.RouteEventArgs e)
    {
        var ids = SelectedMods().Select(m => m.Identifier).ToHashSet();
        if (ids.Count == 0)
            return;

        try
        {
            var code = Comp.CompFavorites.GetShareCode(ids);
            if (string.IsNullOrWhiteSpace(code))
            {
                DialogCompat.ShowHint("生成分享码失败", NotificationType.Error);
                return;
            }

            Common.ClipboardSet(code, false);
            var count = ids.Count;
            ChangeAllSelected(false);
            DialogCompat.ShowHint($"已复制 {count} 个模组的分享码到剪贴板", NotificationType.Success);
        }
        catch (Exception ex)
        {
            Common.Log(ex, "分享所选失败");
            DialogCompat.ShowHint("分享失败：" + ex.Message, NotificationType.Error);
        }
    }

    /// <summary>批量卸载/删除：按 CKAN 注册表逐个卸载选中模组（对齐原版 BtnSelectDelete_Click）。</summary>
    private async void BtnSelectDelete_Click(object sender, Common.RouteEventArgs e)
    {
        var targets = SelectedMods();
        if (targets.Count == 0)
            return;

        var choice = DialogService.Instance.Show(new TextDialogRequest
        {
            Title = "卸载模组",
            Content = $"将卸载 {targets.Count} 个模组：对应文件会从 GameData 中删除，并自动清理因此变成孤立的依赖。此操作不可撤销。",
            Buttons = DialogButtons.From("卸载", "取消"),
            ForceWait = true
        }).Result?.Button ?? 0;
        if (choice != 1)
            return; // 按钮编号从 1 开始：1 = 确认，0 = 未点击，2 = 取消

        if (_busy)
        {
            // 上一次批量操作可能还在收尾（取消后 CKAN 需要时间自行退出），排到它结束再继续，而不是静默丢弃这次点击
            Common.Log("[ModUpdate] 上一次操作尚未收尾，本次点击排队等待");
            DialogCompat.ShowHint("上一次操作正在收尾，稍后自动继续…", NotificationType.Info);
            while (_busy)
                await Task.Delay(150).ConfigureAwait(true);
            Common.Log("[ModUpdate] 上一次操作已结束，继续本次操作");
        }

        _busy = true;
       var ok = 0;
        var failed = new List<string>();
        var index = 0;
        try
        {
            foreach (var m in targets)
            {
                index++;
                var current = m;
               try
                {
                    var mod = current;
                    await Task.Run(() => GetService().UninstallAsync(mod.Identifier)).ConfigureAwait(true);
                    ok++;
                }
                catch (Exception ex)
                {
                    Common.Log(ex, "批量卸载失败：" + current.Identifier);
                    failed.Add(current.Name);
                }
            }
        }
        finally
        {
           _busy = false;
        }

        ChangeAllSelected(false);
        await ReloadListAsync();
        DialogCompat.ShowHint($"已卸载 {ok} 个模组{FailSuffix(failed)}",
            failed.Count == 0 ? NotificationType.Success : NotificationType.Info);
    }

    #endregion

    private static bool IsImageUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;
        if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return true;
        return url.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            || url.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
            || url.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
            || url.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveGameData()
    {
        var settings = KspSettings.Current;
        if (!string.IsNullOrWhiteSpace(settings.KspGameDataPath) && Directory.Exists(settings.KspGameDataPath))
            return settings.KspGameDataPath;
        var root = settings.KspRootPath;
        return string.IsNullOrWhiteSpace(root) ? null : Path.Combine(root, "GameData");
    }

    private static void OpenGameData()
    {
        var gameData = ResolveGameData();
        if (!string.IsNullOrEmpty(gameData) && Directory.Exists(gameData))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = gameData,
                UseShellExecute = true
            });
    }
}
