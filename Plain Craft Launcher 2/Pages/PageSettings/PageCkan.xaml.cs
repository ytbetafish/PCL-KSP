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
            var dlg = new SaveFileDialog { Filter = "CKAN metapackage|*.ckan|JSON|*.json", FileName = CkanModpack.DefaultIdentifier + ".ckan" };
            if (dlg.ShowDialog() == true)
            {
                var identifier = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);
                if (string.IsNullOrWhiteSpace(identifier))
                {
                    identifier = CkanModpack.DefaultIdentifier;
                }
                var json = CkanModpack.Build(installed, null, identifier);
                System.IO.File.WriteAllText(dlg.FileName, json);
                var count = installed.Count(m => m.IsInstalled && !m.IsAutodetected);
                LabCkanStatus.Text = $"已导出标准 CKAN metapackage（{count} 个已装模组）到 {dlg.FileName}";
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
            var identifiers = CkanModpack.Parse(json);
            if (identifiers.Count == 0)
            {
                LabCkanStatus.Text = "清单为空或无法识别（支持 CKAN metapackage / 标识数组）。";
                return;
            }

            var svc = new ModService(dataDir: KspSettings.Current.CkanDataDir);
            var ksp = new KspInstanceService();
            svc.AttachInstance(ksp.Load(KspSettings.Current.KspRootPath));

            LabCkanStatus.Text = $"正在解析 {identifiers.Count} 项的依赖…";
            var plan = await Task.Run(() => svc.GetMultiInstallPlanAsync(identifiers));
            if (!string.IsNullOrEmpty(plan.FailureReason))
            {
                LabCkanStatus.Text = "无法生成安装计划。";
                MessageBox.Show("无法生成安装计划：\n" + plan.FailureReason, "导入 CKAN 清单", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var preview = plan.ToInstall.Take(12).ToList();
            var text = $"将安装 {plan.ToInstall.Count} 个、升级 {plan.ToUpgrade.Count} 个模组：\n\n" +
                       string.Join("\n", preview) +
                       (plan.ToInstall.Count > preview.Count ? "\n……" : "") +
                       "\n\n是否继续？";
            if (MessageBox.Show(text, "导入 CKAN 清单", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            {
                LabCkanStatus.Text = "已取消导入。";
                return;
            }

            var progress = new Progress<string>(msg => LabCkanStatus.Text = msg);
            await svc.ExecutePlanAsync(plan, progress);
            LabCkanStatus.Text = $"清单导入完成：安装 {plan.ToInstall.Count} 个、升级 {plan.ToUpgrade.Count} 个。";
        }
        catch (System.Exception ex)
        {
            LabCkanStatus.Text = "导入失败：" + ex.Message;
        }
    }
}
