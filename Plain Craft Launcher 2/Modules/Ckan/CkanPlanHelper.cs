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
