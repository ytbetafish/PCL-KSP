using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Pcl.Ckan;

namespace PCL.Pages;

public partial class PageSettings
{
    private bool _isInitialized = false;

    public PageSettings()
    {
        InitializeComponent();
        LabSettingsVersion.Text = "PCL-KSP v" + PCL.Core.App.Basics.VersionName;
        TxtBaseUrl.Text = KspSettings.Current.TranslateBaseUrl;
        TxtApiKey.Text = KspSettings.Current.TranslateApiKey;
        TxtModel.Text = KspSettings.Current.TranslateModel;
        LabCkanDataDir.Text = KspSettings.Current.CkanDataDir;
        TxtMirrorUrls.Text = string.Join("\n", KspSettings.Current.CkanMirrorUrls);
        foreach (var item in CboStrategy.Items)
        {
            if (item is System.Windows.Controls.ComboBoxItem ci && (string)ci.Tag == KspSettings.Current.CkanRepoStrategy) { CboStrategy.SelectedItem = item; break; }
        }
        if (CboStrategy.SelectedItem == null) CboStrategy.SelectedIndex = 0;
        switch (KspSettings.Current.CkanStabilityTolerance)
        {
            case "Testing": OptTesting.Checked = true; break;
            case "PreRelease": OptPre.Checked = true; break;
            default: OptStable.Checked = true; break;
        }

        TxtProxy.Text = KspSettings.Current.HttpProxy;
        TxtConcurrency.Text = KspSettings.Current.DownloadConcurrency.ToString();
        TxtTimeout.Text = KspSettings.Current.HttpTimeoutSeconds.ToString();

        RefreshInstancesList();
        RefreshKspInfo();
    }
    public void SelectNav(string key)
    {
        PanKsp.Visibility = key == "ksp" ? Visibility.Visible : Visibility.Collapsed;
        PanCkan.Visibility = key == "ckan" ? Visibility.Visible : Visibility.Collapsed;
        PanTranslate.Visibility = key == "translate" ? Visibility.Visible : Visibility.Collapsed;
        PanAbout.Visibility = key == "about" ? Visibility.Visible : Visibility.Collapsed;
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
                LabSaved.Text = "已切换 KSP 实例。";
            }
        };
    }

    private void RefreshKspInfo()
    {
        var root = KspSettings.Current.KspRootPath;
        LabKspPath.Text = string.IsNullOrEmpty(root) ? "（未设置）" : root;
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            LabKspVersion.Text = "—";
            LabKspExe.Text = "—";
            LabKspGameData.Text = "—";
            LabKspLogs.Text = "—";
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
        }
        catch { LabKspVersion.Text = "读取失败"; }
        LabKspExe.Text = File.Exists(Path.Combine(root, "KSP_x64.exe")) ? Path.Combine(root, "KSP_x64.exe") :
                         File.Exists(Path.Combine(root, "ksp.exe")) ? Path.Combine(root, "ksp.exe") : "未找到";
        var gd = Path.Combine(root, "GameData");
        LabKspGameData.Text = Directory.Exists(gd) ? gd : "不存在";
        var logs = Path.Combine(root, "KSP_Data", "output_log.txt");
        LabKspLogs.Text = File.Exists(logs) ? logs : Path.Combine(root, "KSP_Data");
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
            LabSaved.Text = "实例已添加。";
            RefreshInstancesList();
            RefreshKspInfo();
        }
    }

    private void BtnPickCkanDir_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择 CKAN 数据目录（保存索引缓存）"
        };
        if (dialog.ShowDialog() == true)
        {
            KspSettings.Current.CkanDataDir = dialog.FolderName;
            KspSettings.Save();
            LabCkanDataDir.Text = dialog.FolderName;
            LabCkanStatus.Text = "数据目录已保存。";
        }
    }

    private async void BtnRefreshRepo_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        try
        {
            BtnRefreshRepo.IsEnabled = false;
            LabCkanStatus.Text = "正在刷新模组索引…";
            var svc = new ModService(dataDir: KspSettings.Current.CkanDataDir);
            svc.MirrorUrls = TxtMirrorUrls.Text.Split(new[]{'\r','\n'}, StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            svc.OfficialRepoUrl = KspSettings.Current.CkanMasterRepoUrl;
            svc.RepoStrategy = (CboStrategy.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string ?? "OfficialFirst";
            await svc.RefreshMasterRegistryAsync();
            LabCkanStatus.Text = "索引刷新完成。";
        }
        catch (Exception ex)
        {
            LabCkanStatus.Text = "刷新失败：" + ex.Message;
        }
        finally
        {
            BtnRefreshRepo.IsEnabled = true;
        }
    }

    private async void BtnExportMods_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        try
        {
            var svc = new ModService(dataDir: KspSettings.Current.CkanDataDir);
            var ksp = new KspInstanceService();
            svc.AttachInstance(ksp.Load(KspSettings.Current.KspRootPath));
            var installed = await svc.ListInstalledAsync();
            var rows = installed.Select(m => new { name = m.Identifier, version = m.Version ?? "" }).ToList();
            var dlg = new SaveFileDialog { Filter = "CKAN 清单|*.ckan|JSON|*.json", FileName = "ksp-modlist.ckan" };
            if (dlg.ShowDialog() == true)
            {
                File.WriteAllText(dlg.FileName, System.Text.Json.JsonSerializer.Serialize(rows, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                LabCkanStatus.Text = $"已导出 {rows.Count} 个已安装模组到 {dlg.FileName}";
            }
        }
        catch (Exception ex)
        {
            LabCkanStatus.Text = "导出失败：" + ex.Message;
        }
    }

    private async void BtnImportMods_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        try
        {
            var dlg = new OpenFileDialog { Filter = "CKAN 清单|*.ckan;*.json" };
            if (dlg.ShowDialog() != true) return;
            var json = File.ReadAllText(dlg.FileName);
            var rows = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<ImportRow>>(json);
            if (rows is not { Count: > 0 }) { LabCkanStatus.Text = "清单为空。"; return; }

            var svc = new ModService(dataDir: KspSettings.Current.CkanDataDir);
            var ksp = new KspInstanceService();
            svc.AttachInstance(ksp.Load(KspSettings.Current.KspRootPath));

            var progress = new Progress<string>(msg => LabCkanStatus.Text = msg);
            foreach (var row in rows)
            {
                LabCkanStatus.Text = $"正在安装 {row.name}…";
                var plan = await Task.Run(() => CkanPlanHelper.ResolvePlan(svc, row.name));
                if (plan is not null) await svc.ExecutePlanAsync(plan, progress);
            }
            LabCkanStatus.Text = $"清单导入完成（{rows.Count} 项）。";
        }
        catch (Exception ex)
        {
            LabCkanStatus.Text = "导入失败：" + ex.Message;
        }
    }

    private void BtnSave_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        KspSettings.Current.TranslateBaseUrl = TxtBaseUrl.Text.Trim();
        KspSettings.Current.TranslateApiKey = TxtApiKey.Text.Trim();
        KspSettings.Current.TranslateModel = TxtModel.Text.Trim();
        KspSettings.Current.CkanMirrorUrls = TxtMirrorUrls.Text.Split(new[]{'\r','\n'}, StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        KspSettings.Current.CkanRepoStrategy = (CboStrategy.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string ?? "MirrorOnly";
        KspSettings.Current.CkanStabilityTolerance =
            OptPre.Checked ? "PreRelease" : OptTesting.Checked ? "Testing" : "Stable";
        KspSettings.Current.HttpProxy = TxtProxy.Text.Trim();
        int.TryParse(TxtConcurrency.Text.Trim(), out var conc);
        if (conc > 0) KspSettings.Current.DownloadConcurrency = conc;
        int.TryParse(TxtTimeout.Text.Trim(), out var to);
        if (to > 0) KspSettings.Current.HttpTimeoutSeconds = to;
        KspSettings.Save();
        LabSaved.Text = "已保存。";
    }

    private sealed class ImportRow
    {
        public string name { get; set; } = "";
        public string version { get; set; } = "";
    }
}














