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
