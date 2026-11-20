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
            "cached" => q.Where(m => m.IsCached),
            "uncached" => q.Where(m => !m.IsCached),
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
        if (!string.IsNullOrEmpty(mod.ReplacedById)) info.Add("可替换为 " + mod.ReplacedById);
        if (mod.IsAutodetected) info.Add("AD（手动安装）");
        if (mod.IsCached) info.Add("已缓存");

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

    private async void BtnFilterCached_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => await LoadAllThenFilter("cached");

    private async void BtnFilterUncached_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => await LoadAllThenFilter("uncached");

    private async void BtnCacheManager_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        CkanCacheDialog.Show(GetService(), null);
        // 缓存可能被清空或导入，重新计算列表里的「已缓存」标记。
        try
        {
            await GetService().WarmUpAsync();
            _lastResults = await GetService().SearchAsync(null);
            ApplyFilter();
        }
        catch (Exception ex)
        {
            LabModDesc.Text = "刷新缓存状态失败：" + ex.Message;
        }
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
            LabRecommends.Visibility = Visibility.Collapsed;
            LabSuggests.Visibility = Visibility.Collapsed;
            LabMeta.Visibility = Visibility.Collapsed;
            BtnInstall.Visibility = Visibility.Collapsed;
            BtnUninstall.Visibility = Visibility.Collapsed;
            BtnContents.Visibility = Visibility.Collapsed;
            BtnResolveConflicts.Visibility = Visibility.Collapsed;
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
        if (!string.IsNullOrEmpty(_selected.ReplacedById))
        {
            LabModHome.Text = (LabModHome.Text + "　｜　已被 " + _selected.ReplacedById + " 替换").Trim();
        }
        if (_selected.Tags is { Count: > 0 })
        {
            LabModHome.Text = (LabModHome.Text + "　｜　标签：" + string.Join("、", _selected.Tags)).Trim();
        }

        // CKAN 元数据：类型 / 发布状态 / 最高 KSP / 源码仓库 / 问题反馈
        var metaParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(_selected.Kind))
        {
            metaParts.Add("类型：" + _selected.Kind);
        }
        if (!string.IsNullOrWhiteSpace(_selected.ReleaseStatus))
        {
            metaParts.Add("发布状态：" + _selected.ReleaseStatus);
        }
        if (!string.IsNullOrWhiteSpace(_selected.MaxKsp))
        {
            metaParts.Add("最高 KSP：" + _selected.MaxKsp);
        }
        if (!string.IsNullOrWhiteSpace(_selected.Version))
        {
            metaParts.Add("版本：" + _selected.Version);
        }
        if (!string.IsNullOrWhiteSpace(_selected.License))
        {
            metaParts.Add("许可证：" + _selected.License);
        }
        if (!string.IsNullOrWhiteSpace(_selected.RepositoryUrl))
        {
            metaParts.Add("源码：" + _selected.RepositoryUrl);
        }
        if (!string.IsNullOrWhiteSpace(_selected.BugTrackerUrl))
        {
            metaParts.Add("问题反馈：" + _selected.BugTrackerUrl);
        }
        LabMeta.Text = string.Join("　｜　", metaParts);
        LabMeta.Visibility = metaParts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        BtnInstall.Visibility = string.IsNullOrWhiteSpace(KspSettings.Current.KspRootPath) ? Visibility.Collapsed : Visibility.Visible;
        BtnUninstall.Visibility = _selected.IsInstalled ? Visibility.Visible : Visibility.Collapsed;
        BtnContents.Visibility = Visibility.Visible;
        BtnResolveConflicts.Visibility = Visibility.Visible;
        BtnTranslate.Visibility = string.IsNullOrWhiteSpace(_originalDescription) ? Visibility.Collapsed : Visibility.Visible;

        // 拉一下依赖列表
        try
        {
            var plan = await GetService().GetInstallPlanAsync(_selected.Identifier);
            FillRelLab(LabDeps, "依赖：", plan?.ToInstall);

            var (rev, conflicts) = GetService().GetReverseAndConflicts(_selected.Identifier);
            FillRelLab(LabReverse, "被依赖：", rev);
            FillRelLab(LabConflicts, "冲突：", conflicts);

            var optional = await GetService().GetOptionalRelationsAsync(_selected.Identifier);
            FillRelLab(LabRecommends, "推荐：", optional?.Recommends);
            FillRelLab(LabSuggests, "建议：", optional?.Suggests);
        }
        catch
        {
            LabDeps.Visibility = Visibility.Collapsed;
            LabReverse.Visibility = Visibility.Collapsed;
            LabConflicts.Visibility = Visibility.Collapsed;
            LabRecommends.Visibility = Visibility.Collapsed;
            LabSuggests.Visibility = Visibility.Collapsed;
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

        var withRecommendations = false;
        try
        {
            var optional = await GetService().GetOptionalRelationsAsync(_selected.Identifier);
            if (optional is { Recommends.Count: > 0 })
            {
                var shown = string.Join("、", optional.Recommends.Take(5));
                var more = optional.Recommends.Count > 5 ? $" 等 {optional.Recommends.Count} 个" : "";
                var answer = MessageBox.Show(
                    $"「{_selected.Name}」推荐一并安装：{shown}{more}。\n是否随本次安装一起装上这些推荐模组？",
                    "PCL KSP", MessageBoxButton.YesNo, MessageBoxImage.Question);
                withRecommendations = answer == MessageBoxResult.Yes;
            }
        }
        catch { /* 推荐关系读取失败不阻断安装 */ }

        try
        {
            PanProgress.Visibility = Visibility.Visible;
            BarProgress.IsIndeterminate = true;
            LabProgress.Text = "正在解析依赖…";

            var plan = await Task.Run(() => CkanPlanHelper.ResolvePlan(GetService(), _selected.Identifier, allowIncompatible, withRecommendations));
            if (plan is null)
            {
                PanProgress.Visibility = Visibility.Collapsed;
                MessageBox.Show("无法为该模组生成安装计划（未附加 KSP 实例？）。", "PCL KSP", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            LabProgress.Text = $"计划：安装 {plan.ToInstall.Count} 个，升级 {plan.ToUpgrade.Count} 个" +
                               (plan.ToRemove.Count > 0 ? $"，移除被替换的 {plan.ToRemove.Count} 个。" : "。");
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

    /// <summary>Contents：查看模组会安装哪些文件（已缓存时读安装包，未缓存时读描述符）。</summary>
    private async void BtnContents_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_selected is null)
        {
            return;
        }
        try
        {
            var entries = await GetService().GetContentsAsync(_selected.Identifier);
            CkanContentsDialog.Show(_selected.Name, entries);
        }
        catch (Exception ex)
        {
            MessageBox.Show("读取内容失败：" + ex.Message, "模组内容", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>scan：扫描 GameData 中未托管的 DLL（CKAN 的 AD），并在列表中标注。</summary>
    /// <summary>扫描全部已装模组之间的 conflicts 关系，勾选卸载以解除冲突。</summary>
    private async void BtnConflictScan_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        try
        {
            PanLoad.Visibility = Visibility.Visible;
            var pairs = await GetService().GetConflictPairsAsync();
            PanLoad.Visibility = Visibility.Collapsed;
            if (pairs.Count == 0)
            {
                MessageBox.Show("未发现已装模组之间的冲突关系。",
                    "冲突诊断", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var picked = await PickConflictUninstallsAsync(pairs);
            if (picked.Count == 0)
            {
                return;
            }
            await UninstallConflictsAsync(picked);
        }
        catch (InstanceReadOnlyException)
        {
            PanLoad.Visibility = Visibility.Collapsed;
            MessageBox.Show("当前实例为只读（CKAN 注册表不可写），无法卸载冲突模组。请关闭占用该实例的 CKAN 客户端后重试。",
                "冲突诊断", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            PanLoad.Visibility = Visibility.Collapsed;
            MessageBox.Show("冲突诊断失败：" + ex.Message, "冲突诊断", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>处理当前选中模组的冲突：只列出与它冲突的已装项。</summary>
    private async void BtnResolveConflicts_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_selected is null)
        {
            return;
        }
        try
        {
            PanLoad.Visibility = Visibility.Visible;
            var pairs = await GetService().GetConflictPairsAsync(_selected.Identifier);
            PanLoad.Visibility = Visibility.Collapsed;
            if (pairs.Count == 0)
            {
                MessageBox.Show("未发现与该模组冲突的已装模组。",
                    "处理冲突", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var picked = await PickConflictUninstallsAsync(pairs);
            if (picked.Count == 0)
            {
                return;
            }
            await UninstallConflictsAsync(picked);
        }
        catch (InstanceReadOnlyException)
        {
            PanLoad.Visibility = Visibility.Collapsed;
            MessageBox.Show("当前实例为只读（CKAN 注册表不可写），无法卸载冲突模组。请关闭占用该实例的 CKAN 客户端后重试。",
                "处理冲突", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            PanLoad.Visibility = Visibility.Collapsed;
            MessageBox.Show("处理冲突失败：" + ex.Message, "处理冲突", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task<IReadOnlyList<string>> PickConflictUninstallsAsync(IReadOnlyList<ModConflictPair> pairs)
    {
        var installed = (await GetService().ListInstalledAsync())
            .Where(r => r.IsInstalled)
            .Select(r => r.Identifier)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var picked = CkanConflictDialog.Choose(pairs, installed);
        if (picked.Count == 0 && pairs.Count > 0)
        {
            MessageBox.Show("冲突对的双方都没有可卸载的已装模组，或已取消选择。",
                "冲突诊断", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        return picked;
    }

    private async Task UninstallConflictsAsync(IReadOnlyList<string> identifiers)
    {
        PanLoad.Visibility = Visibility.Visible;
        await GetService().UninstallManyAsync(identifiers);
        PanLoad.Visibility = Visibility.Collapsed;
        MessageBox.Show($"已卸载 {identifiers.Count} 个冲突模组。",
            "冲突诊断", MessageBoxButton.OK, MessageBoxImage.Information);
        _lastResults = await GetService().ListInstalledAsync();
        ApplyFilter();
        if (_selected is { } current)
        {
            SelectMod(current);
        }
    }

    /// <summary>在资源管理器打开当前 KSP 实例根目录（CKAN 的 Open KSP directory）。</summary>
    private void BtnOpenKspDir_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var root = KspSettings.Current.KspRootPath;
        if (string.IsNullOrWhiteSpace(root) || !System.IO.Directory.Exists(root))
        {
            MessageBox.Show("当前没有设置有效的 KSP 目录，请先在设置页选择实例。",
                "打开 KSP 目录", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        PCL.Core.App.Basics.OpenPath(root);
    }

    private async void BtnCleanOrphans_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        try
        {
            PanLoad.Visibility = Visibility.Visible;
            var items = await GetService().GetOrphanDependenciesAsync();
            PanLoad.Visibility = Visibility.Collapsed;
            if (items.Count == 0)
            {
                MessageBox.Show("没有可清理的孤儿依赖：所有自动安装的依赖都仍被已装模组需要。",
                    "清理孤儿依赖", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var picked = CkanOrphanDialog.Choose(items);
            if (picked.Count == 0)
            {
                return;
            }

            PanLoad.Visibility = Visibility.Visible;
            await GetService().UninstallManyAsync(picked.Select(p => p.Identifier).ToList());
            PanLoad.Visibility = Visibility.Collapsed;
            MessageBox.Show($"已卸载 {picked.Count} 个孤儿依赖。",
                "清理孤儿依赖", MessageBoxButton.OK, MessageBoxImage.Information);

            _lastResults = await GetService().ListInstalledAsync();
            ApplyFilter();
        }
        catch (InstanceReadOnlyException)
        {
            PanLoad.Visibility = Visibility.Collapsed;
            MessageBox.Show("当前实例为只读（CKAN 注册表不可写），无法卸载依赖。请关闭占用该实例的 CKAN 客户端后重试。",
                "清理孤儿依赖", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            PanLoad.Visibility = Visibility.Collapsed;
            MessageBox.Show("清理孤儿依赖失败：" + ex.Message, "清理孤儿依赖", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnScanUnmanaged_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        try
        {
            PanLoad.Visibility = Visibility.Visible;
            var changed = await GetService().ScanUnmanagedAsync();
            var items = await GetService().GetUnmanagedAsync();
            PanLoad.Visibility = Visibility.Collapsed;

            var text = items.Count == 0
                ? "未发现未托管的 DLL：GameData 中的 DLL 全部已在 CKAN 注册表登记。"
                : $"发现 {items.Count} 个未托管的 DLL（AD / autodetected）：\n\n" +
                  string.Join("\n", items.Take(20).Select(i =>
                      "· " + i.Identifier + (string.IsNullOrEmpty(i.Path) ? "" : " —— " + i.Path))) +
                  (items.Count > 20 ? $"\n…… 共 {items.Count} 个" : "") +
                  "\n\n这些模组由手动安装产生：CKAN 能识别它们在游戏里，但无法安全卸载其文件。" +
                  "如需纳入管理，请卸载手动版本后用对应 CKAN 模组重新安装一次。";
            if (changed)
            {
                text += "\n\n（注册表已更新）";
            }
            MessageBox.Show(text, "扫描未托管模组", MessageBoxButton.OK, MessageBoxImage.Information);

            _lastResults = await GetService().ListInstalledAsync();
            ApplyFilter();
        }
        catch (InstanceReadOnlyException)
        {
            PanLoad.Visibility = Visibility.Collapsed;
            MessageBox.Show("当前实例为只读（CKAN 注册表不可写），无法执行扫描。请关闭占用该实例的 CKAN 客户端后重试。",
                "扫描未托管模组", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            PanLoad.Visibility = Visibility.Collapsed;
            MessageBox.Show("扫描失败：" + ex.Message, "扫描未托管模组", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Changeset：勾选多个模组，统一解析后一次性应用（安装/升级/移除）。</summary>
    private async void BtnChangeset_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var candidates = _lastResults.Where(m => !m.IsInstalled || m.HasUpdate).ToList();
        if (candidates.Count == 0)
        {
            MessageBox.Show("当前列表没有可变更的模组（仅列出未安装或有更新的项）。", "批量变更",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var choice = CkanChangesetDialog.Choose(GetService(), candidates);
        if (choice is null)
        {
            return;
        }

        var plan = choice.Value.Plan;
        try
        {
            PanProgress.Visibility = Visibility.Visible;
            BarProgress.IsIndeterminate = true;
            LabProgress.Text = $"变更集：{choice.Value.Identifiers.Count} 个目标，安装 {plan.ToInstall.Count}、" +
                               $"升级 {plan.ToUpgrade.Count}、移除 {plan.ToRemove.Count}。";
            var progress = new Progress<string>(msg =>
                Dispatcher.BeginInvoke(new Action(() => LabProgress.Text = msg)));

            await GetService().ExecutePlanAsync(plan, progress);

            PanProgress.Visibility = Visibility.Collapsed;
            MessageBox.Show("变更集已应用。", "批量变更", MessageBoxButton.OK, MessageBoxImage.Information);
            _filter = "installed";
            _lastResults = await GetService().ListInstalledAsync();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            PanProgress.Visibility = Visibility.Collapsed;
            MessageBox.Show("变更集应用失败：" + ex.Message, "批量变更", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Audit recommendations：汇总已装模组的推荐项，勾选后批量安装。</summary>
    private async void BtnAuditRecommendations_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        try
        {
            PanLoad.Visibility = Visibility.Visible;
            await GetService().WarmUpAsync();
            var items = await GetService().GetAuditRecommendationsAsync();
            PanLoad.Visibility = Visibility.Collapsed;
            if (items.Count == 0)
            {
                MessageBox.Show("没有发现已装模组推荐、但尚未安装的模组。", "推荐审计", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var selected = CkanOptionalInstallDialog.Choose(items, "推荐审计");
            if (selected.Count == 0)
            {
                return;
            }

            PanProgress.Visibility = Visibility.Visible;
            BarProgress.IsIndeterminate = true;
            var progress = new Progress<string>(msg =>
                Dispatcher.BeginInvoke(new Action(() => LabProgress.Text = msg)));

            var ok = 0;
            foreach (var item in selected)
            {
                LabProgress.Text = $"正在安装推荐模组 {item.Name}…";
                var plan = await Task.Run(() => CkanPlanHelper.ResolvePlan(GetService(), item.Identifier));
                if (plan is not null && string.IsNullOrEmpty(plan.FailureReason))
                {
                    await GetService().ExecutePlanAsync(plan, progress);
                    ok++;
                }
            }

            PanProgress.Visibility = Visibility.Collapsed;
            MessageBox.Show($"推荐审计完成：已安装 {ok}/{selected.Count} 个模组。", "推荐审计", MessageBoxButton.OK, MessageBoxImage.Information);
            _filter = "installed";
            _lastResults = await GetService().ListInstalledAsync();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            PanLoad.Visibility = Visibility.Collapsed;
            PanProgress.Visibility = Visibility.Collapsed;
            MessageBox.Show("推荐审计失败：" + ex.Message, "推荐审计", MessageBoxButton.OK, MessageBoxImage.Error);
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
