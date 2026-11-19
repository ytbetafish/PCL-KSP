using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PCL;
using PCL.Core.App;
using PCL.Core.Logging;
using Microsoft.Win32;

namespace PCL.Pages;

public partial class PageLog
{
    private static string LogDir =>
        LogService.Logger?.Configuration.StoreFolder
        ?? Path.Combine(Basics.ExecutableDirectory, "PCL", "Log");

    public PageLog()
    {
        InitializeComponent();
        LoadList();
    }

    private void LoadList()
    {
        PanList.Children.Clear();
        var files = Directory.Exists(LogDir)
            ? Directory.GetFiles(LogDir).OrderByDescending(File.GetLastWriteTime).ToList()
            : [];
        if (files.Count == 0)
        {
            PanList.Children.Add(new TextBlock
            {
                Text = "暂无日志文件",
                Margin = new Thickness(15, 10, 0, 10),
                Opacity = 0.6,
                FontSize = 13
            });
            return;
        }

        var current = new HashSet<string>(
            LogService.Logger?.CurrentLogFiles ?? [], StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var full = Path.GetFullPath(file);
            var name = Path.GetFileName(file);
            var title = current.Contains(full) ? name + "（当前）" : name;
            var item = new MyListItem
            {
                Title = title,
                Info = full,
                tag = full,
                Height = 46d,
                Type = MyListItem.CheckType.Clickable
            };
            item.Click += LogItem_Click;
            PanList.Children.Add(item);
        }
    }

    private void LogItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is MyListItem item && item.tag is string path && File.Exists(path))
            Basics.OpenPath(path);
    }

    private void BtnOpenDir_Click(object sender, MouseButtonEventArgs e) => Basics.OpenPath(LogDir);

    private void BtnExportCurrent_Click(object sender, MouseButtonEventArgs e)
        => ExportLogs(LogService.Logger?.CurrentLogFiles.ToList() ?? []);

    private void BtnExportAll_Click(object sender, MouseButtonEventArgs e)
        => ExportLogs(Directory.Exists(LogDir) ? Directory.GetFiles(LogDir).ToList() : []);

    private void BtnClean_Click(object sender, MouseButtonEventArgs e)
    {
        if (MessageBox.Show("确定删除所有非当前会话的历史日志吗？", "清理日志",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            return;
        var current = new HashSet<string>(
            LogService.Logger?.CurrentLogFiles ?? [], StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var file in Directory.GetFiles(LogDir))
                if (!current.Contains(Path.GetFullPath(file)))
                    File.Delete(file);
            LoadList();
        }
        catch (Exception ex)
        {
            MessageBox.Show("清理失败：" + ex.Message, "清理日志", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportLogs(List<string> files)
    {
        if (files.Count == 0)
        {
            MessageBox.Show("没有可导出的日志文件。", "导出日志", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dlg = new SaveFileDialog
        {
            Title = "导出日志",
            FileName = "PCL-KSP_Logs_" + DateTime.Now.ToString("yyyyMMddHHmmss"),
            Filter = "日志压缩包|*.zip"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            if (File.Exists(dlg.FileName)) File.Delete(dlg.FileName);
            using var zip = ZipFile.Open(dlg.FileName, ZipArchiveMode.Create);
            foreach (var file in files)
                if (File.Exists(file))
                    zip.CreateEntryFromFile(file, Path.GetFileName(file), CompressionLevel.Fastest);
            MessageBox.Show("已导出 " + files.Count + " 个日志文件。", "导出日志",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("导出失败：" + ex.Message, "导出日志", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
