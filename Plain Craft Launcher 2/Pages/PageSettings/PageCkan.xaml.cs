using Microsoft.Win32;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Pcl.Ckan;

namespace PCL.Pages;

public partial class PageCkan
{
    public PageCkan()
    {
        InitializeComponent();
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
        LoadGitHubAcceleration();
    }

    private bool _loadingGitHubAccel;

    private void LoadGitHubAcceleration()
    {
        _loadingGitHubAccel = true;
        try
        {
            var prefix = KspSettings.Current.GitHubAccelerationPrefix ?? "";
            ChkGitHubAccel.Checked = KspSettings.Current.GitHubAccelerationEnabled;
            TxtGitHubPrefix.Text = prefix;
            CboGitHubPrefix.SelectedItem = null;
            foreach (var item in CboGitHubPrefix.Items)
            {
                if (item is System.Windows.Controls.ComboBoxItem ci && ci.Tag is string tag && tag.Length > 0 &&
                    string.Equals(tag, prefix, System.StringComparison.OrdinalIgnoreCase))
                {
                    CboGitHubPrefix.SelectedItem = item;
                    break;
                }
            }
            if (CboGitHubPrefix.SelectedItem == null)
            {
                CboGitHubPrefix.SelectedIndex = CboGitHubPrefix.Items.Count - 1;
            }
        }
        finally
        {
            _loadingGitHubAccel = false;
        }
    }

    private void ChkGitHubAccel_Change(object sender, bool user)
    {
        if (_loadingGitHubAccel || !user) return;
        KspSettings.Current.GitHubAccelerationEnabled = ChkGitHubAccel.Checked == true;
        KspSettings.Save();
        LabCkanStatus.Text = ChkGitHubAccel.Checked == true ? "已启用 GitHub 下载加速。" : "已关闭 GitHub 下载加速。";
    }

    private void CboGitHubPrefix_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loadingGitHubAccel) return;
        if (CboGitHubPrefix.SelectedItem is System.Windows.Controls.ComboBoxItem ci && ci.Tag is string tag && tag.Length > 0)
        {
            TxtGitHubPrefix.Text = tag;
            SaveGitHubPrefix(tag);
        }
    }

    private void TxtGitHubPrefix_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loadingGitHubAccel) return;
        SaveGitHubPrefix(TxtGitHubPrefix.Text);
    }

    private void SaveGitHubPrefix(string value)
    {
        var prefix = value.Trim();
        if (prefix.Length == 0) prefix = "https://ghfast.top/";
        if (!prefix.EndsWith("/", System.StringComparison.Ordinal)) prefix += "/";
        KspSettings.Current.GitHubAccelerationPrefix = prefix;
        KspSettings.Save();
        LabCkanStatus.Text = "加速前缀已保存：" + prefix;
    }

    private void SaveDataDir(object sender, RoutedEventArgs e)
    {
        var dir = LabCkanDataDir.Text.Trim();
        if (!string.IsNullOrEmpty(dir))
        {
            KspSettings.Current.CkanDataDir = dir;
            KspSettings.Save();
            LabCkanStatus.Text = "数据目录已保存。";
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
            KspSettings.Current.CkanDataDir = LabCkanDataDir.Text.Trim();
            KspSettings.Save();
            var svc = new ModService(dataDir: KspSettings.Current.CkanDataDir);
            svc.MirrorUrls = TxtMirrorUrls.Text.Split(new[]{'\r','\n'}, System.StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            svc.OfficialRepoUrl = KspSettings.Current.CkanMasterRepoUrl;
            svc.RepoStrategy = (CboStrategy.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string ?? "OfficialFirst";
            await svc.RefreshMasterRegistryAsync();
            LabCkanStatus.Text = "索引刷新完成。";
        }
        catch (System.Exception ex)
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
                System.IO.File.WriteAllText(dlg.FileName, System.Text.Json.JsonSerializer.Serialize(rows, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                LabCkanStatus.Text = $"已导出 {rows.Count} 个已安装模组到 {dlg.FileName}";
            }
        }
        catch (System.Exception ex)
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
            var json = System.IO.File.ReadAllText(dlg.FileName);
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
        catch (System.Exception ex)
        {
            LabCkanStatus.Text = "导入失败：" + ex.Message;
        }
    }

    private sealed class ImportRow
    {
        public string name { get; set; } = "";
        public string version { get; set; } = "";
    }
}
