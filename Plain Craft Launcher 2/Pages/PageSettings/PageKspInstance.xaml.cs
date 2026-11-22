using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PCL.Pages;

public partial class PageKspInstance
{
    public PageKspInstance()
    {
        InitializeComponent();
        CboInstances.SelectionChanged += CboInstances_SelectionChanged;
        RefreshInstancesList();
        RefreshKspInfo();
    }

    /// <summary>重建实例下拉框：显示名为「自定义名」或目录名，Tag 存完整路径。</summary>
    private void RefreshInstancesList()
    {
        var previous = KspSettings.Current.KspRootPath;
        CboInstances.Items.Clear();

        var paths = new List<string>(KspSettings.Current.Instances);
        if (!string.IsNullOrEmpty(previous) && !paths.Contains(previous))
        {
            paths.Insert(0, previous);
        }

        foreach (var path in paths)
        {
            CboInstances.Items.Add(new MyComboBoxItem
            {
                Content = KspSettings.GetDisplayName(path),
                Tag = path,
                ToolTip = path
            });
        }

        var index = 0;
        for (var i = 0; i < CboInstances.Items.Count; i++)
        {
            if (CboInstances.Items[i] is FrameworkElement fe
                && string.Equals(fe.Tag as string, previous, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }
        if (CboInstances.Items.Count > 0)
        {
            CboInstances.SelectedIndex = index;
        }
    }

    private void CboInstances_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var path = GetSelectedInstancePath();
        if (path is null || string.Equals(path, KspSettings.Current.KspRootPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        KspSettings.Current.KspRootPath = path;
        KspSettings.Save();
        RefreshKspInfo();
    }

    private string? GetSelectedInstancePath()
    {
        if (CboInstances.SelectedItem is FrameworkElement fe && fe.Tag is string path && path.Length > 0)
        {
            return path;
        }
        return null;
    }

    private void RefreshKspInfo()
    {
        var settings = KspSettings.Current;
        var root = settings.KspRootPath;
        LabKspPath.Text = string.IsNullOrEmpty(root) ? "（未设置）" : root;

        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            LabKspVersion.Text = "—";
            LabKspExe.Text = string.IsNullOrEmpty(settings.KspExePath) ? "—" : settings.KspExePath;
            LabKspGameData.Text = string.IsNullOrEmpty(settings.KspGameDataPath) ? "—" : settings.KspGameDataPath;
            LabKspLogs.Text = string.IsNullOrEmpty(settings.KspLogsPath) ? "—" : settings.KspLogsPath;
            return;
        }
        try
        {
            var buildId = Path.Combine(root, "KSP_Data", "buildID.txt");
            var versionTxt = Path.Combine(root, "version.txt");
            // KSP 64 位版本号记录在根目录 buildID64.txt（形如 "build id = 03190"），优先走实例探测
            var probed = new Pcl.Ckan.KspInstanceService().Probe(root)?.Version;
            if (!string.IsNullOrWhiteSpace(probed))
                LabKspVersion.Text = probed!;
            else if (File.Exists(buildId))
                LabKspVersion.Text = File.ReadAllText(buildId).Trim();
            else if (File.Exists(versionTxt))
                LabKspVersion.Text = File.ReadAllText(versionTxt).Trim();
            else
                LabKspVersion.Text = "未知";
        }
        catch
        {
            LabKspVersion.Text = "读取失败";
        }

        LabKspExe.Text = string.IsNullOrEmpty(settings.KspExePath)
            ? (File.Exists(Path.Combine(root, "KSP_x64.exe")) ? Path.Combine(root, "KSP_x64.exe") :
                File.Exists(Path.Combine(root, "ksp.exe")) ? Path.Combine(root, "ksp.exe") : "未找到")
            : settings.KspExePath;
        var gd = Path.Combine(root, "GameData");
        LabKspGameData.Text = string.IsNullOrEmpty(settings.KspGameDataPath)
            ? (Directory.Exists(gd) ? gd : "不存在")
            : settings.KspGameDataPath;
        var logs = Path.Combine(root, "KSP_Data", "output_log.txt");
        LabKspLogs.Text = string.IsNullOrEmpty(settings.KspLogsPath)
            ? (File.Exists(logs) ? logs : Path.Combine(root, "KSP_Data"))
            : settings.KspLogsPath;
    }

    private void SaveInstanceFields(object sender, RoutedEventArgs e)
    {
        var settings = KspSettings.Current;
        if (!string.IsNullOrWhiteSpace(LabKspPath.Text))
            settings.KspRootPath = LabKspPath.Text.Trim();
        settings.KspExePath = LabKspExe.Text.Trim();
        settings.KspGameDataPath = LabKspGameData.Text.Trim();
        settings.KspLogsPath = LabKspLogs.Text.Trim();
        KspSettings.Save();
        RefreshKspInfo();
    }

    private void BtnAddInstance_Click(object sender, MouseButtonEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "添加 KSP 实例目录" };
        if (dialog.ShowDialog() == true)
        {
            if (!KspSettings.Current.Instances.Contains(dialog.FolderName))
                KspSettings.Current.Instances.Add(dialog.FolderName);
            KspSettings.Current.KspRootPath = dialog.FolderName;
            KspSettings.Save();
            RefreshInstancesList();
            RefreshKspInfo();
        }
    }

    private void BtnRenameInstance_Click(object sender, MouseButtonEventArgs e)
    {
        var path = GetSelectedInstancePath();
        if (path is null)
        {
            MessageBox.Show("请先选择一个实例。", "重命名实例", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var currentName = KspSettings.GetDisplayName(path);
        var input = PromptText("重命名实例", "给这个实例起一个显示名（留空则恢复为目录名）：", currentName);
        if (input is null)
        {
            return;
        }
        KspSettings.SetDisplayName(path, input);
        KspSettings.Save();
        RefreshInstancesList();
        MessageBox.Show("已重命名为：" + KspSettings.GetDisplayName(path),
            "重命名实例", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnSetDefaultInstance_Click(object sender, MouseButtonEventArgs e)
    {
        var path = GetSelectedInstancePath();
        if (path is null)
        {
            MessageBox.Show("请先选择一个实例。", "设为默认", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        KspSettings.Current.KspRootPath = path;
        KspSettings.Save();
        RefreshKspInfo();
        MessageBox.Show($"已把「{KspSettings.GetDisplayName(path)}」设为默认实例。",
            "设为默认", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnOpenKspDir_Click(object sender, MouseButtonEventArgs e)
    {
        var root = KspSettings.Current.KspRootPath;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            MessageBox.Show("当前没有设置有效的 KSP 目录。", "打开 KSP 目录", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        PCL.Core.App.Basics.OpenPath(root);
    }

    private void BtnForgetInstance_Click(object sender, MouseButtonEventArgs e)
    {
        var path = GetSelectedInstancePath();
        if (path is null)
        {
            MessageBox.Show("请先选择一个实例。", "忘记实例", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var answer = MessageBox.Show(
            $"从列表里移除「{KspSettings.GetDisplayName(path)}」？\n\n{path}\n\n只移除记录，不会删除硬盘上的任何文件。",
            "忘记此实例", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        KspSettings.Current.Instances.Remove(path);
        KspSettings.SetDisplayName(path, null);
        if (string.Equals(KspSettings.Current.KspRootPath, path, StringComparison.OrdinalIgnoreCase))
        {
            KspSettings.Current.KspRootPath = KspSettings.Current.Instances.FirstOrDefault() ?? "";
        }
        KspSettings.Save();
        RefreshInstancesList();
        RefreshKspInfo();
    }

    /// <summary>简易文本输入框（原版 PCL 的 MsgBox 输入样式在 KSP 版里未移植，此处自建）。</summary>
    private static string? PromptText(string title, string message, string defaultValue)
    {
        var window = AppDialogWindow.Create(title, 460, out var host);

        var root = new StackPanel { Margin = new Thickness(18, 16, 18, 16) };
        root.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });

        var box = new MyTextBox { Text = defaultValue, Height = 34 };
        root.Children.Add(box);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        string? result = null;
        var ok = new MyButton { Text = "确定", Width = 100, Margin = new Thickness(0, 0, 10, 0) };
        ok.Click += (_, _) =>
        {
            result = box.Text;
            window.DialogResult = true;
        };
        buttons.Children.Add(ok);
        var cancel = new MyButton { Text = "取消", Width = 100 };
        cancel.Click += (_, _) => window.DialogResult = false;
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        host.Content = root;
        window.ShowDialog();
        return window.DialogResult == true ? result : null;
    }
}
