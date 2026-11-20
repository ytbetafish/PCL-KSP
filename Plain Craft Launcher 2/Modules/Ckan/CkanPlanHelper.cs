using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Pcl.Ckan;

namespace PCL;

/// <summary>
/// CKAN 安装计划解析助手：处理 TooManyModsProvide（同一个依赖有多个模组可以提供），
/// 逐个弹出选择框收集用户的选择，再带着选择重新解析，直到计划可用或用户取消。
/// </summary>
internal static class CkanPlanHelper
{
    /// <summary>防止同一选择反复出现导致死循环。</summary>
    private const int MaxChoiceRounds = 8;

    /// <summary>
    /// 解析安装计划。遇到多提供者依赖时同步弹出选择框（自动切回 UI 线程），用户取消返回 null。
    /// 可在后台线程直接调用。
    /// </summary>
    public static ModInstallPlan? ResolvePlan(IModService service, string identifier, bool allowIncompatible = false, bool withRecommendations = false)
    {
        var picks = new List<string>();
        for (var round = 0; round < MaxChoiceRounds; round++)
        {
            var plan = service.GetInstallPlanAsync(identifier, allowIncompatible, picks, withRecommendations).GetAwaiter().GetResult();
            if (plan?.ProviderChoices is not { Count: > 0 })
            {
                return plan;
            }

            var choice = plan.ProviderChoices[0];
            var picked = RunOnUiThread(() => CkanProviderChoiceDialog.Choose(choice));
            if (picked is null)
            {
                return null;
            }
            if (picks.Contains(picked.Identifier, StringComparer.OrdinalIgnoreCase))
            {
                // 同一个候选反复被要求选择，说明解析器无法归一化，带现有选择直接返回结果。
                break;
            }
            picks.Add(picked.Identifier);
        }
        return service.GetInstallPlanAsync(identifier, allowIncompatible, picks, withRecommendations).GetAwaiter().GetResult();
    }

    private static T RunOnUiThread<T>(Func<T> func)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            return func();
        }
        return dispatcher.Invoke(func);
    }
}

/// <summary>
/// 多提供者依赖的候选选择窗口：列出所有能满足该依赖的模组，单选后确定，
/// 与 CKAN 客户端的 TooManyModsProvide 选择框语义一致。
/// </summary>
internal static class CkanProviderChoiceDialog
{
    public static ModProviderOption? Choose(ModProviderChoice choice)
    {
        ModProviderOption? result = null;
        var window = new Window
        {
            Title = "选择提供者",
            Width = 520,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false
        };
        var main = Application.Current?.MainWindow;
        if (main is { IsVisible: true } && !ReferenceEquals(main, window))
        {
            window.Owner = main;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        var root = new DockPanel { Margin = new Thickness(16, 14, 16, 14) };

        var message = $"「{choice.Requester ?? "某个模组"}」需要 {choice.Requested}，" +
                      $"它可以由以下 {choice.Options.Count} 个模组提供。请选择要安装的一个：";
        if (!string.IsNullOrWhiteSpace(choice.ChoiceHelpText))
        {
            message += Environment.NewLine + choice.ChoiceHelpText;
        }
        var header = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var bottom = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);

        var listPanel = new StackPanel();
        root.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 320,
            Content = listPanel
        });

        var items = new List<MyListItem>();
        foreach (var option in choice.Options)
        {
            var item = new MyListItem
            {
                Title = string.IsNullOrWhiteSpace(option.Name) ? option.Identifier : option.Name,
                Info = string.IsNullOrWhiteSpace(option.Version)
                    ? option.Identifier
                    : option.Version + " · " + option.Identifier,
                Height = 46d,
                Type = MyListItem.CheckType.RadioBox,
                tag = option
            };
            item.SvgIcon = "lucide/package";
            items.Add(item);
            listPanel.Children.Add(item);
        }
        if (items.Count > 0)
        {
            items[0].Checked = true;
        }

        var btnOk = new MyButton
        {
            Text = "确定",
            Width = 120,
            Margin = new Thickness(0, 0, 10, 0)
        };
        btnOk.Click += (_, _) =>
        {
            var picked = items.FirstOrDefault(i => i.Checked)?.tag as ModProviderOption;
            if (picked is null)
            {
                return;
            }
            result = picked;
            window.DialogResult = true;
        };
        bottom.Children.Add(btnOk);

        var btnCancel = new MyButton { Text = "取消", Width = 120 };
        btnCancel.Click += (_, _) => window.DialogResult = false;
        bottom.Children.Add(btnCancel);

        window.Content = root;
        window.ShowDialog();
        return result;
    }
}

/// <summary>
/// 推荐审计的多选窗口：列出已装模组推荐、但尚未安装的模组，勾选后批量安装。
/// </summary>
internal static class CkanOptionalInstallDialog
{
    public static IReadOnlyList<ModAuditItem> Choose(IReadOnlyList<ModAuditItem> items, string title)
    {
        var picked = new List<ModAuditItem>();
        var window = new Window
        {
            Title = title,
            Width = 560,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false
        };
        var main = Application.Current?.MainWindow;
        if (main is { IsVisible: true } && !ReferenceEquals(main, window))
        {
            window.Owner = main;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        var root = new DockPanel { Margin = new Thickness(16, 14, 16, 14) };

        var header = new TextBlock
        {
            Text = $"已装模组推荐了以下 {items.Count} 个尚未安装的模组，勾选后一并安装：",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var bottom = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);

        var listPanel = new StackPanel();
        root.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 360,
            Content = listPanel
        });

        var rows = new List<(ModAuditItem Item, MyListItem Ui)>();
        foreach (var item in items)
        {
            var ui = new MyListItem
            {
                Title = item.Name,
                Info = item.Identifier + " · 来自 " + item.SourceName,
                Height = 46d,
                Type = MyListItem.CheckType.CheckBox,
                tag = item
            };
            ui.SvgIcon = "lucide/package";
            rows.Add((item, ui));
            listPanel.Children.Add(ui);
        }

        var btnAll = new MyButton { Text = "全选", Width = 100, Margin = new Thickness(0, 0, 10, 0) };
        btnAll.Click += (_, _) =>
        {
            foreach (var row in rows)
            {
                row.Ui.Checked = true;
            }
        };
        bottom.Children.Add(btnAll);

        var btnOk = new MyButton { Text = "安装选中", Width = 120, Margin = new Thickness(0, 0, 10, 0) };
        btnOk.Click += (_, _) =>
        {
            picked = rows.Where(r => r.Ui.Checked).Select(r => r.Item).ToList();
            window.DialogResult = true;
        };
        bottom.Children.Add(btnOk);

        var btnCancel = new MyButton { Text = "取消", Width = 120 };
        btnCancel.Click += (_, _) => window.DialogResult = false;
        bottom.Children.Add(btnCancel);

        window.Content = root;
        window.ShowDialog();
        return picked;
    }
}

/// <summary>
/// CKAN Changeset 的交互窗口：勾选多个模组 → 预览变更集（安装/升级/移除）→ 一次性应用。
/// </summary>
internal static class CkanChangesetDialog
{
    public static (IReadOnlyList<string> Identifiers, ModInstallPlan Plan)? Choose(IModService service, IReadOnlyList<ModSearchResult> candidates)
    {
        IReadOnlyList<string>? confirmed = null;
        ModInstallPlan? confirmedPlan = null;

        var window = new Window
        {
            Title = "批量变更（Changeset）",
            Width = 640,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false
        };
        var main = Application.Current?.MainWindow;
        if (main is { IsVisible: true } && !ReferenceEquals(main, window))
        {
            window.Owner = main;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        var root = new StackPanel { Margin = new Thickness(16, 14, 16, 14) };
        root.Children.Add(new TextBlock
        {
            Text = $"勾选要变更的模组（共 {candidates.Count} 项）。变更集会在应用前统一解析依赖与冲突，再一次性执行。",
            TextWrapping = TextWrapping.Wrap
        });

        var listPanel = new StackPanel();
        root.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 300,
            Margin = new Thickness(0, 10, 0, 0),
            Content = listPanel
        });

        var rows = new List<(ModSearchResult Mod, MyListItem Ui)>();
        foreach (var mod in candidates)
        {
            var info = new List<string>();
            if (!string.IsNullOrWhiteSpace(mod.Version)) info.Add(mod.Version!);
            info.Add(mod.HasUpdate ? "有更新" : mod.IsInstalled ? "已安装" : "未安装");
            var ui = new MyListItem
            {
                Title = mod.Name,
                Info = string.Join(" · ", info),
                Height = 46d,
                Type = MyListItem.CheckType.CheckBox,
                tag = mod
            };
            ui.SvgIcon = "lucide/package";
            rows.Add((mod, ui));
            listPanel.Children.Add(ui);
        }

        var previewText = new TextBlock
        {
            Text = "尚未预览。",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(0, 12, 0, 0),
            Opacity = 0.75
        };
        root.Children.Add(previewText);

        var bottom = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        root.Children.Add(bottom);

        var btnAll = new MyButton { Text = "全选", Width = 90, Margin = new Thickness(0, 0, 10, 0) };
        bottom.Children.Add(btnAll);

        var btnPreview = new MyButton { Text = "预览计划", Width = 110, Margin = new Thickness(0, 0, 10, 0) };
        bottom.Children.Add(btnPreview);

        var btnApply = new MyButton { Text = "应用变更", Width = 110, Margin = new Thickness(0, 0, 10, 0) };
        bottom.Children.Add(btnApply);

        var btnCancel = new MyButton { Text = "取消", Width = 90 };
        bottom.Children.Add(btnCancel);

        List<string> PickIds() => rows.Where(r => r.Ui.Checked).Select(r => r.Mod.Identifier).ToList();

        btnAll.Click += (_, _) =>
        {
            foreach (var row in rows)
            {
                row.Ui.Checked = true;
            }
        };

        btnPreview.Click += async (_, _) =>
        {
            var ids = PickIds();
            if (ids.Count == 0)
            {
                previewText.Text = "请先勾选要变更的模组。";
                return;
            }
            previewText.Text = "正在解析变更集…";
            btnPreview.IsEnabled = false;
            try
            {
                var plan = await Task.Run(() => service.GetMultiInstallPlanAsync(ids).GetAwaiter().GetResult());
                previewText.Text = DescribePlan(plan);
            }
            catch (Exception ex)
            {
                previewText.Text = "解析失败：" + ex.Message;
            }
            finally
            {
                btnPreview.IsEnabled = true;
            }
        };

        btnApply.Click += async (_, _) =>
        {
            var ids = PickIds();
            if (ids.Count == 0)
            {
                previewText.Text = "请先勾选要变更的模组。";
                return;
            }
            previewText.Text = "正在解析变更集…";
            btnApply.IsEnabled = false;
            try
            {
                var plan = await Task.Run(() => service.GetMultiInstallPlanAsync(ids).GetAwaiter().GetResult());
                if (plan is null || !string.IsNullOrEmpty(plan.FailureReason) || plan.ProviderChoices is { Count: > 0 })
                {
                    previewText.Text = DescribePlan(plan);
                    return;
                }
                confirmed = ids;
                confirmedPlan = plan;
                window.DialogResult = true;
            }
            catch (Exception ex)
            {
                previewText.Text = "解析失败：" + ex.Message;
            }
            finally
            {
                btnApply.IsEnabled = true;
            }
        };

        btnCancel.Click += (_, _) => window.DialogResult = false;

        window.Content = root;
        window.ShowDialog();
        return confirmed is null || confirmedPlan is null ? null : (confirmed, confirmedPlan);
    }

    private static string DescribePlan(ModInstallPlan? plan)
    {
        static string Short(IReadOnlyList<string> ids)
            => ids.Count == 0 ? "无" : string.Join("、", ids.Take(6)) + (ids.Count > 6 ? $" 等 {ids.Count} 个" : "");

        if (plan is null)
        {
            return "无法解析所选模组（未附加 KSP 实例或标识未知）。";
        }
        if (!string.IsNullOrEmpty(plan.FailureReason))
        {
            return "变更集不可用：" + plan.FailureReason;
        }
        if (plan.ProviderChoices is { Count: > 0 })
        {
            return "变更集含多提供者依赖，请先在详情页完成该依赖的选择安装后再试。";
        }
        return $"将安装 {plan.ToInstall.Count} 个：{Short(plan.ToInstall)}\n" +
               $"将升级 {plan.ToUpgrade.Count} 个：{Short(plan.ToUpgrade)}\n" +
               $"将移除 {plan.ToRemove.Count} 个：{Short(plan.ToRemove)}";
    }
}

/// <summary>
/// 下载缓存管理窗口（CKAN 的 cache 面板）：显示占用、清空缓存、导入已下载的模组包。
/// </summary>
internal static class CkanCacheDialog
{
    public static void Show(IModService service, Window? owner)
    {
        var window = new Window
        {
            Title = "下载缓存",
            Width = 540,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false
        };
        if (owner is { IsVisible: true })
        {
            window.Owner = owner;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        var root = new StackPanel { Margin = new Thickness(16, 14, 16, 14) };
        var info = new TextBlock
        {
            Text = "正在统计缓存…",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5
        };
        root.Children.Add(info);

        var bottom = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        root.Children.Add(bottom);

        var btnImport = new MyButton { Text = "导入已下载", Width = 110, Margin = new Thickness(0, 0, 10, 0) };
        var btnClear = new MyButton { Text = "清空缓存", Width = 110, Margin = new Thickness(0, 0, 10, 0) };
        var btnClose = new MyButton { Text = "关闭", Width = 90 };
        bottom.Children.Add(btnImport);
        bottom.Children.Add(btnClear);
        bottom.Children.Add(btnClose);

        async void RefreshAsync()
        {
            info.Text = "正在统计缓存…";
            try
            {
                var stats = await Task.Run(() => service.GetCacheStatsAsync().GetAwaiter().GetResult());
                info.Text = stats is null
                    ? "未附加 KSP 实例，无法读取缓存。"
                    : $"缓存目录：{stats.CachePath}\n文件 {stats.FileCount} 个，占用 {FormatSize(stats.TotalBytes)}。";
            }
            catch (Exception ex)
            {
                info.Text = "读取缓存失败：" + ex.Message;
            }
        }

        btnImport.Click += async (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择已下载的模组包",
                Filter = "CKAN 模组包 (*.zip;*.ckan)|*.zip;*.ckan|所有文件 (*.*)|*.*",
                Multiselect = true
            };
            if (dialog.ShowDialog(window) != true)
            {
                return;
            }
            btnImport.IsEnabled = false;
            info.Text = "正在导入…";
            try
            {
                var result = await Task.Run(() => service.ImportCacheFilesAsync(dialog.FileNames).GetAwaiter().GetResult());
                var text = $"已导入 {result.Imported} 个文件到下载缓存。";
                if (result.Failed.Count > 0)
                {
                    text += $"\n{result.Failed.Count} 个文件无法读取。";
                }
                MessageBox.Show(window, text, "导入已下载模组", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(window, "导入失败：" + ex.Message, "导入已下载模组", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnImport.IsEnabled = true;
                RefreshAsync();
            }
        };

        btnClear.Click += async (_, _) =>
        {
            var answer = MessageBox.Show(window, "确定清空下载缓存？已下载的安装包会被删除，但不会影响已安装的模组。",
                "清空缓存", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }
            btnClear.IsEnabled = false;
            info.Text = "正在清空…";
            try
            {
                await Task.Run(() => service.ClearCacheAsync().GetAwaiter().GetResult());
            }
            catch (Exception ex)
            {
                MessageBox.Show(window, "清空失败：" + ex.Message, "清空缓存", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnClear.IsEnabled = true;
                RefreshAsync();
            }
        };

        btnClose.Click += (_, _) => window.Close();

        window.Content = root;
        window.Show();
        RefreshAsync();
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return bytes + " B";
        if (bytes < 1024 * 1024) return (bytes / 1024d).ToString("0.0") + " KB";
        if (bytes < 1024L * 1024 * 1024) return (bytes / 1024d / 1024d).ToString("0.0") + " MB";
        return (bytes / 1024d / 1024d / 1024d).ToString("0.00") + " GB";
    }
}

/// <summary>Contents 页：列出模组安装包内的文件（或未缓存时的安装描述符）。</summary>
internal static class CkanContentsDialog
{
    internal static void Show(string moduleName, IReadOnlyList<ModContentEntry> entries)
    {
        var window = new Window
        {
            Title = "内容 - " + moduleName,
            Width = 680,
            Height = 560,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar = false
        };
        var main = Application.Current?.MainWindow;
        if (main is { IsVisible: true })
        {
            window.Owner = main;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        var root = new DockPanel { Margin = new Thickness(16, 14, 16, 14) };
        var header = new TextBlock
        {
            Text = entries.Count == 0
                ? $"「{moduleName}」没有可展示的安装内容（元数据缺少 install 描述符，且本地未缓存安装包）。"
                : $"「{moduleName}」将安装 {entries.Count} 项内容。" +
                  (entries.Any(e => e.InstallTo == null) ? "（来自已缓存的安装包）" : "（来自元数据安装描述符）"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var body = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        body.Content = new TextBlock
        {
            Text = string.Join(Environment.NewLine, entries.Select(e =>
                e.InstallTo is null ? e.Path : e.Path + "  →  " + e.InstallTo)),
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };
        root.Children.Add(body);

        var bottom = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);

        var btnClose = new MyButton { Text = "关闭", Width = 90 };
        btnClose.Click += (_, _) => window.Close();
        bottom.Children.Add(btnClose);

        window.Content = root;
        window.Show();
    }
}

/// <summary>CKAN 高级检索条件：按元数据字段过滤。</summary>
internal sealed record ModSearchFilter(string? Author, string? Description, string? License, string? DependsOn)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Author)
        && string.IsNullOrWhiteSpace(Description)
        && string.IsNullOrWhiteSpace(License)
        && string.IsNullOrWhiteSpace(DependsOn);
}

/// <summary>高级检索窗口：作者 / 描述关键词 / 许可证 / 依赖模组。</summary>
internal static class CkanAdvancedSearchDialog
{
    public static (bool Apply, ModSearchFilter? Filter)? Choose(ModSearchFilter? current)
    {
        var window = new Window
        {
            Title = "高级检索",
            Width = 560,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false
        };
        var main = Application.Current?.MainWindow;
        if (main is { IsVisible: true })
        {
            window.Owner = main;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        var root = new StackPanel { Margin = new Thickness(16, 14, 16, 14) };
        root.Children.Add(new TextBlock
        {
            Text = "按 CKAN 元数据字段过滤当前模组列表；留空表示不限制。依赖模组请填标识（如 ModuleManager）。",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            Opacity = 0.75
        });

        var grid = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        for (var i = 0; i < 4; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var txtAuthor = new TextBox { Text = current?.Author ?? "" };
        var txtDesc = new TextBox { Text = current?.Description ?? "" };
        var txtLicense = new TextBox { Text = current?.License ?? "" };
        var txtDepends = new TextBox { Text = current?.DependsOn ?? "" };

        void AddRow(int row, string label, TextBox box)
        {
            var lb = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) };
            Grid.SetRow(lb, row);
            Grid.SetColumn(lb, 0);
            grid.Children.Add(lb);
            box.Margin = new Thickness(0, 0, 0, 8);
            box.Padding = new Thickness(6, 4, 6, 4);
            Grid.SetRow(box, row);
            Grid.SetColumn(box, 1);
            grid.Children.Add(box);
        }

        AddRow(0, "作者", txtAuthor);
        AddRow(1, "描述关键词", txtDesc);
        AddRow(2, "许可证", txtLicense);
        AddRow(3, "依赖模组", txtDepends);
        root.Children.Add(grid);

        var bottom = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        root.Children.Add(bottom);

        (bool Apply, ModSearchFilter? Filter)? result = null;

        var btnClear = new MyButton { Text = "清除条件", Width = 110, Margin = new Thickness(0, 0, 10, 0) };
        btnClear.Click += (_, _) =>
        {
            result = (true, null);
            window.DialogResult = true;
        };
        bottom.Children.Add(btnClear);

        var btnOk = new MyButton { Text = "确定", Width = 100, Margin = new Thickness(0, 0, 10, 0) };
        btnOk.Click += (_, _) =>
        {
            result = (true, new ModSearchFilter(
                string.IsNullOrWhiteSpace(txtAuthor.Text) ? null : txtAuthor.Text.Trim(),
                string.IsNullOrWhiteSpace(txtDesc.Text) ? null : txtDesc.Text.Trim(),
                string.IsNullOrWhiteSpace(txtLicense.Text) ? null : txtLicense.Text.Trim(),
                string.IsNullOrWhiteSpace(txtDepends.Text) ? null : txtDepends.Text.Trim()));
            window.DialogResult = true;
        };
        bottom.Children.Add(btnOk);

        var btnCancel = new MyButton { Text = "取消", Width = 90 };
        btnCancel.Click += (_, _) => window.DialogResult = false;
        bottom.Children.Add(btnCancel);

        window.Content = root;
        window.ShowDialog();
        return result;
    }
}

/// <summary>
/// 孤儿依赖清理窗口：列出不再被任何已装模组需要的自动安装依赖，勾选后一次卸载。
/// </summary>
internal static class CkanOrphanDialog
{
    public static IReadOnlyList<ModOrphanItem> Choose(IReadOnlyList<ModOrphanItem> items)
    {
        var picked = new List<ModOrphanItem>();
        var window = new Window
        {
            Title = "清理孤儿依赖",
            Width = 560,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false
        };
        var main = Application.Current?.MainWindow;
        if (main is { IsVisible: true } && !ReferenceEquals(main, window))
        {
            window.Owner = main;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        var root = new DockPanel { Margin = new Thickness(16, 14, 16, 14) };

        var header = new TextBlock
        {
            Text = $"以下 {items.Count} 个依赖由 CKAN 自动安装，现在已没有任何已装模组需要它们。勾选后一并卸载：",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var bottom = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);

        var listPanel = new StackPanel();
        root.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 360,
            Content = listPanel
        });

        var rows = new List<(ModOrphanItem Item, MyListItem Ui)>();
        foreach (var item in items)
        {
            var ui = new MyListItem
            {
                Title = item.Name,
                Info = item.Identifier + (string.IsNullOrEmpty(item.Version) ? "" : " · " + item.Version),
                Height = 46d,
                Type = MyListItem.CheckType.CheckBox,
                tag = item
            };
            ui.SvgIcon = "lucide/package";
            rows.Add((item, ui));
            listPanel.Children.Add(ui);
        }

        var btnAll = new MyButton { Text = "全选", Width = 100, Margin = new Thickness(0, 0, 10, 0) };
        btnAll.Click += (_, _) =>
        {
            foreach (var row in rows)
            {
                row.Ui.Checked = true;
            }
        };
        bottom.Children.Add(btnAll);

        var btnOk = new MyButton { Text = "卸载选中", Width = 120, Margin = new Thickness(0, 0, 10, 0) };
        btnOk.Click += (_, _) =>
        {
            picked = rows.Where(r => r.Ui.Checked).Select(r => r.Item).ToList();
            window.DialogResult = true;
        };
        bottom.Children.Add(btnOk);

        var btnCancel = new MyButton { Text = "取消", Width = 120 };
        btnCancel.Click += (_, _) => window.DialogResult = false;
        bottom.Children.Add(btnCancel);

        window.Content = root;
        window.ShowDialog();
        return picked;
    }
}
