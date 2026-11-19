using Microsoft.Win32;
using System.IO;
using System.Windows;

namespace PCL.Pages;

public partial class PageKspInstance
{
    public PageKspInstance()
    {
        InitializeComponent();
        RefreshInstancesList();
        RefreshKspInfo();
    }

    private void RefreshInstancesList()
    {
        CboInstances.Items.Clear();
        foreach (var p in KspSettings.Current.Instances) CboInstances.Items.Add(p);
        if (!string.IsNullOrEmpty(KspSettings.Current.KspRootPath)
            && !KspSettings.Current.Instances.Contains(KspSettings.Current.KspRootPath))
        {
            CboInstances.Items.Insert(0, KspSettings.Current.KspRootPath);
        }
        CboInstances.SelectedItem = KspSettings.Current.KspRootPath;
        CboInstances.SelectionChanged += (_, _) =>
        {
            if (CboInstances.SelectedItem is string s && !string.IsNullOrEmpty(s))
            {
                KspSettings.Current.KspRootPath = s;
                KspSettings.Save();
                RefreshKspInfo();
            }
        };
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
            if (File.Exists(buildId))
                LabKspVersion.Text = File.ReadAllText(buildId).Trim();
            else if (File.Exists(versionTxt))
                LabKspVersion.Text = File.ReadAllText(versionTxt).Trim();
            else
                LabKspVersion.Text = "未知";
        } catch { LabKspVersion.Text = "读取失败"; }

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

    private void BtnAddInstance_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
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
}
