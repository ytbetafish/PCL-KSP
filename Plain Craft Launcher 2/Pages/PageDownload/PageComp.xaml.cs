using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using PCL.Core.App.Localization;
using PCL.Core.UI.Hints;
using Pcl.Ckan;

namespace PCL;

[ContentProperty("SearchTags")]
public partial class PageComp : Grid
{
    private readonly PageCompViewModel _vm = new();

    private List<Comp.CompProject> _projects = new();
    private List<ModSearchResult> _results = new();
    private List<ModSearchResult> _rawResults = new();
    private readonly HashSet<int> _enrichedPages = new();
    private CancellationTokenSource? _enrichCts;
    private int _currentPage = 0;
    private const int PageSize = 40;
    private bool _loading;

    public PageComp()
    {
        InitializeComponent();
        DataContext = _vm;
        Loaded += PageComp_Loaded;
    }

    private async void PageComp_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadProjects(null);
    }

    /// <summary>从 CKAN 注册表搜索模组并刷新列表。</summary>
    private async Task LoadProjects(string? keyword)
    {
        if (_loading)
            return;
        _loading = true;
        PanLoad.Visibility = Visibility.Visible;
        PanContent.Visibility = Visibility.Collapsed;
        _vm.ApplyErrorState(null);
        try
        {
            var query = string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim();
            var sw = Stopwatch.StartNew();
            Common.Log("[CKAN] SearchAsync 开始");
            var results = await Launcher.Ckan.SearchAsync(query);
            Common.Log($"[CKAN] SearchAsync 返回 {results.Count} 条，耗时 {sw.ElapsedMilliseconds} ms");
            sw.Restart();
            _rawResults = results.ToList();
            PopulateTags();
            RebuildFromRaw();
            Common.Log($"[CKAN] ToProjects {_projects.Count} 条，耗时 {sw.ElapsedMilliseconds} ms");
            PanContent.Visibility = Visibility.Visible;
            Common.Log($"[CKAN] RefreshList 完成，卡片 {PanProjects.Children.Count} 个");
            _ = EnrichCurrentPageAsync();
            _ = RefreshWhenCatalogReadyAsync();
        }
        catch (Exception ex)
        {
            Common.Log(ex, "CKAN 模组列表加载失败");
            PanContent.Visibility = Visibility.Visible;
            _vm.ApplyErrorState(ex.Message);
        }
        finally
        {
            PanLoad.Visibility = Visibility.Collapsed;
            _loading = false;
        }
    }

    /// <summary>
    /// 首屏可能用的是磁盘快照（CKAN 首次解析仓库数据要 ~19 秒，不该让页面干等）。
    /// 这里等后台把真实目录算完，条数变了就静默换一次数据：不弹加载圈、不动用户当前筛选。
    /// </summary>
    private async Task RefreshWhenCatalogReadyAsync()
    {
        try
        {
            var build = Launcher.Ckan.CatalogBuildTask;
            if (build.IsCompleted)
                return;
            await build.ConfigureAwait(true);
            if (_loading)
                return;
            var fresh = await Launcher.Ckan.SearchAsync(null).ConfigureAwait(true);
            if (fresh.Count == _rawResults.Count)
                return;
            _rawResults = fresh.ToList();
            PopulateTags();
            RebuildFromRaw();
            _ = EnrichCurrentPageAsync();
        }
        catch
        {
            // 静默刷新失败不影响已经渲染出来的列表
        }
    }

    /// <summary>读取排序下拉框选中项（与原版 CompSortType 编号一致）。</summary>
    private int GetSortId()
    {
        if (ComboSearchSort.SelectedItem is FrameworkElement fe && fe.Tag != null &&
            int.TryParse(fe.Tag.ToString(), out var id))
            return id;
        return 1;
    }

    /// <summary>在本地按排序项排列结果；默认/相关性保持注册表原顺序（稳定）。</summary>
    private static List<ModSearchResult> ApplySort(List<ModSearchResult> list, int sortId)
    {
        switch (sortId)
        {
            case 3: // 下载量降序；未回填下载量(0)的模组排后
                return list
                    .OrderByDescending(r => r.Downloads)
                    .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            case 5: // 最新发布
            case 6: // 最近更新（CKAN 目录仅登记发布日期，同用 ReleaseDate）
                return list
                    .OrderByDescending(r => r.ReleaseDate)
                    .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            default: // 1 默认 / 2 相关性
                return list;
        }
    }

    /// <summary>按兼容性过滤 + 排序后重建列表（保持 _results 与卡片页对齐，便于按页回填）。</summary>
    /// <summary>CKAN 高级检索条件（作者/描述/许可/依赖），null 表示未启用。</summary>
    private ModSearchFilter? _advFilter;

    private void BtnAdvancedSearch_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var choice = CkanAdvancedSearchDialog.Choose(_advFilter);
        if (choice is null)
        {
            return;
        }

        _advFilter = choice.Value.Filter;
        UpdateAdvFilterLabel();
        if (_rawResults.Count > 0)
        {
            RebuildFromRaw();
            _ = EnrichCurrentPageAsync();
        }
    }

    private void UpdateAdvFilterLabel()
    {
        var parts = new List<string>();
        if (_advFilter is { } adv)
        {
            if (!string.IsNullOrWhiteSpace(adv.Author)) parts.Add("作者:" + adv.Author);
            if (!string.IsNullOrWhiteSpace(adv.Description)) parts.Add("描述:" + adv.Description);
            if (!string.IsNullOrWhiteSpace(adv.License)) parts.Add("许可:" + adv.License);
            if (!string.IsNullOrWhiteSpace(adv.DependsOn)) parts.Add("依赖:" + adv.DependsOn);
        }
        LabAdvFilter.Text = string.Join("；", parts);
        LabAdvFilter.Visibility = parts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>应用高级检索条件。依赖过滤走注册表的反向依赖表（与 CKAN 的筛选口径一致）。</summary>
    private List<ModSearchResult> ApplyAdvancedFilter(List<ModSearchResult> source, ModSearchFilter adv)
    {
        return ModAdvFilter.Apply(source, adv, target =>
        {
            try
            {
                return Launcher.Ckan.GetReverseAndConflicts(target).Item1;
            }
            catch
            {
                return Array.Empty<string>();
            }
        });
    }


    private void RebuildFromRaw()
    {
        var filtered = GetCompatId() switch
        {
            1 => _rawResults.Where(r => r.IsCompatible).ToList(),
            2 => _rawResults.Where(r => !r.IsCompatible).ToList(),
            _ => _rawResults.ToList()
        };
        if (_advFilter is { } adv && !adv.IsEmpty)
        {
            filtered = ApplyAdvancedFilter(filtered, adv);
        }
        var tagFilter = GetTagFilter();
        if (!string.IsNullOrEmpty(tagFilter))
        {
            filtered = filtered
                .Where(r => r.Tags != null && r.Tags.Any(t => string.Equals(t, tagFilter, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }
        _results = ApplySort(filtered, GetSortId());
        _projects = CkanCompAdapter.ToProjects(_results);
        _enrichedPages.Clear();
        _currentPage = 0;
        RefreshList();
    }

    private int GetCompatId()
    {
        if (ComboSearchCompat.SelectedItem is FrameworkElement fe && fe.Tag != null &&
            int.TryParse(fe.Tag.ToString(), out var id))
            return id;
        return 0;
    }

    private bool _populatingTags;

    /// <summary>按当前目录结果聚合标签，填充标签下拉框（保留已选标签）。</summary>
    private void PopulateTags()
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in _rawResults)
        {
            if (item.Tags == null) continue;
            foreach (var tag in item.Tags)
            {
                if (string.IsNullOrWhiteSpace(tag)) continue;
                counts.TryGetValue(tag, out var n);
                counts[tag] = n + 1;
            }
        }

        var selected = GetTagFilter();
        _populatingTags = true;
        try
        {
            ComboSearchTag.Items.Clear();
            ComboSearchTag.Items.Add(new MyComboBoxItem { Content = "全部标签", Tag = string.Empty });
            foreach (var pair in counts.OrderByDescending(k => k.Value)
                         .ThenBy(k => k.Key, StringComparer.OrdinalIgnoreCase).Take(40))
            {
                ComboSearchTag.Items.Add(new MyComboBoxItem
                {
                    Content = pair.Key + " (" + pair.Value + ")",
                    Tag = pair.Key
                });
            }

            var index = 0;
            if (!string.IsNullOrEmpty(selected))
            {
                for (var i = 1; i < ComboSearchTag.Items.Count; i++)
                {
                    if (ComboSearchTag.Items[i] is FrameworkElement fe &&
                        string.Equals(fe.Tag as string, selected, StringComparison.OrdinalIgnoreCase))
                    {
                        index = i;
                        break;
                    }
                }
            }
            ComboSearchTag.SelectedIndex = index;
        }
        finally
        {
            _populatingTags = false;
        }
    }

    private string? GetTagFilter()
    {
        if (ComboSearchTag.SelectedItem is FrameworkElement fe && fe.Tag is string tag && tag.Length > 0)
            return tag;
        return null;
    }

    private void ComboSearchTag_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_populatingTags) return;
        if (_rawResults.Count == 0) return;
        RebuildFromRaw();
        _ = EnrichCurrentPageAsync();
    }

    private void ComboSearchCompat_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_rawResults.Count == 0)
            return;
        RebuildFromRaw();
        _ = EnrichCurrentPageAsync();
    }

    private void RefreshList()
    {
        PanProjects.Children.Clear();
        var start = _currentPage * PageSize;
        var end = Math.Min(start + PageSize, _projects.Count);
        for (var i = start; i < end; i++)
            PanProjects.Children.Add(_projects[i].ToCompItem(false, false));
        _vm.ApplyPagerState(_currentPage, _projects.Count, PageSize);
    }

    /// <summary>异步回填当前页的 SpaceDock 信息（图标/下载量等）；已回填的页保留在本地缓存，不重复请求。</summary>
    private async Task EnrichCurrentPageAsync()
    {
        _enrichCts?.Cancel();
        _enrichCts?.Dispose();
        _enrichCts = new CancellationTokenSource();
        var ct = _enrichCts.Token;

        if (_results.Count == 0 || _enrichedPages.Contains(_currentPage))
            return;

        var start = _currentPage * PageSize;
        if (start >= _results.Count)
            return;
        var count = Math.Min(PageSize, _results.Count - start);

        try
        {
            await Launcher.Ckan.EnrichRangeAsync(_results, start, count, ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested)
                return;

            _enrichedPages.Add(_currentPage);
            var mapped = CkanCompAdapter.ToProjects(_results.GetRange(start, count));
            for (var i = 0; i < mapped.Count; i++)
                _projects[start + i] = mapped[i];

            // Refresh cards in place; do not rebuild the tree (Clear+Add) to avoid flicker on focus loss/restore.
            await Dispatcher.InvokeAsync(() =>
            {
                var children = PanProjects.Children;
                for (var i = 0; i < children.Count && i < mapped.Count; i++)
                {
                    if (children[i] is FrameworkElement fe &&
                        MyVirtualizingElement.TryInit(fe) is MyCompItem item)
                        item.RefreshFrom(mapped[i], false, false);
                }
            });
        }
        catch (OperationCanceledException)
        {
            // 翻页取消，丢弃本次回填结果。
        }
        catch (Exception ex)
        {
            Common.Log(ex, "当前页模组信息回填失败");
        }
    }

    public ItemCollection SearchTags => ComboSearchTag.Items;

    public string TypeName => "模组";
    public string TypeNameSpaced => "模组";

    public object PageType { get; set; }

    private void PanSearchBox_Search(object sender, EventArgs e) => StartSearch();

    private void EnterTrigger(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            StartSearch();
    }

    private void BtnSearchReset_Click(object sender, EventArgs e) => ResetFilter();

    private void BtnPageFirst_Click(object sender, EventArgs e)
    {
        _currentPage = 0;
        RefreshList();
        _ = EnrichCurrentPageAsync();
    }

    private void BtnPageLeft_Click(object sender, EventArgs e)
    {
        if (_currentPage > 0)
        {
            _currentPage--;
            RefreshList();
            _ = EnrichCurrentPageAsync();
        }
    }

    private void BtnPageRight_Click(object sender, EventArgs e)
    {
        if ((_currentPage + 1) * PageSize < _projects.Count)
        {
            _currentPage++;
            RefreshList();
            _ = EnrichCurrentPageAsync();
        }
    }

    private void Load_State(object sender, LoadingState state, LoadingState oldState)
    {
    }

    private async void StartSearch()
    {
        await LoadProjects(PanSearchBox.Text);
    }

    private async void ResetFilter()
    {
        PanSearchBox.Text = "";
        ComboSearchTag.SelectedIndex = 0;
        ComboSearchSort.SelectedIndex = 0;
        ComboSearchCompat.SelectedIndex = 0;
        await LoadProjects(null);
    }

    private void ComboSearchSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_rawResults.Count == 0)
            return;
        RebuildFromRaw();
        _ = EnrichCurrentPageAsync();
    }
}
