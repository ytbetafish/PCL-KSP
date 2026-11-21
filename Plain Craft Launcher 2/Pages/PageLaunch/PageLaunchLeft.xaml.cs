using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Pcl.Ckan;

namespace PCL;

public partial class PageLaunchLeft
{
    private static readonly string[] LaunchTips =
    {
        "发射前按 F5 快速存档，F9 快速读档，跨大气层前记得先存档。",
        "在跟踪站（Tracking Station）升级设施，可以解锁更多任务与机动节点上限。",
        "物理时间加速会降低仿真精度，长距离转移建议分段加速。",
        "遇到模组冲突时，先看 KSP.log 与 Player.log 里的 Exception 与 ModuleManager 报错。",
        "安装或更新模组前，建议备份 saves 目录与 GameData 目录。"
    };

    private static Process? _activeProcess;

    private bool _launching;
    private int _tipIndex;
    private DispatcherTimer? _tipTimer;
    private string _instanceVersion = "";

    public PageLaunchLeft()
    {
        InitializeComponent();
        LabLauncherVersion.Text = "PCL-KSP v" + PCL.Core.App.Basics.VersionName;
        Loaded += (_, _) =>
        {
            RefreshPath();
            if (IsKspRunning())
            {
                // 页面重建时游戏仍在运行，直接展示启动中视图
                ShowLaunching();
                LabLaunchingTitle.Text = "游戏运行中";
                SetStage("游戏运行中", 100);
                LabLaunchingHint.Text = "关闭游戏后本页会自动恢复。";
            }
        };
        Unloaded += (_, _) => StopTipTimer();
    }

    private static bool IsKspRunning() => _activeProcess is { HasExited: false };

    private void RefreshPath()
    {
        var path = KspSettings.Current.KspRootPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            LabKspVersion.Text = "未设置 KSP";
            LabKspPath.Text = "请到设置页选择 KSP 安装目录";
            return;
        }
        LabKspPath.Text = path;
        try
        {
            var probe = new KspInstanceService().Probe(path);
            _instanceVersion = probe?.Version ?? "";
            LabKspVersion.Text = string.IsNullOrEmpty(_instanceVersion) ? "KSP" : "KSP " + _instanceVersion;
            LabVersion.Text = _instanceVersion;
            LabLaunchingInstance.Text = string.IsNullOrEmpty(_instanceVersion) ? "默认实例" : "KSP " + _instanceVersion;
        }
        catch
        {
            LabKspVersion.Text = "KSP（路径无效）";
            LabVersion.Text = "";
        }
    }

    private async void BtnLaunch_Click(object sender, MouseButtonEventArgs e)
    {
        if (_launching) return;
        var root = KspSettings.Current.KspRootPath;
        if (string.IsNullOrWhiteSpace(root) || !KspInstanceService.IsKspRoot(root))
        {
            MessageBox.Show("请先在设置页选择有效的 KSP 安装路径。", "PCL KSP", MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }
        if (IsKspRunning())
        {
            MessageBox.Show("KSP 已经在运行中。", "PCL KSP", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var exe = Path.Combine(root, "KSP_x64.exe");
        if (!File.Exists(exe)) exe = Path.Combine(root, "KSP.exe");
        if (!File.Exists(exe))
        {
            MessageBox.Show("未找到 KSP_x64.exe / KSP.exe。", "PCL KSP", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        ShowLaunching();
        LabLaunchingTitle.Text = "正在启动 Kerbal Space Program";
        LabLaunchingName.Text = string.IsNullOrEmpty(_instanceVersion) ? "Kerbal Space Program" : "KSP " + _instanceVersion;
        LabLaunchingInstance.Text = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar));
        SetStage("准备启动", 5);
        StartTipTimer();
        await System.Threading.Tasks.Task.Delay(150);

        SetStage("启动游戏进程", 20);
        try
        {
            var psi = new ProcessStartInfo { FileName = exe, WorkingDirectory = root, UseShellExecute = true };
            _activeProcess = Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show("启动失败：" + ex.Message, "PCL KSP", MessageBoxButton.OK, MessageBoxImage.Error);
            HideLaunching();
            return;
        }
        if (_activeProcess is null)
        {
            HideLaunching();
            return;
        }
        try
        {
            _activeProcess.EnableRaisingEvents = true;
            _activeProcess.Exited += OnKspExited;
        }
            catch (System.Exception innerEx)
            {
                PCL.Core.Logging.Swallowed.Report(innerEx);
            }

        // 等待主窗口出现，最长约 2 分钟（KSP 加载较慢）
        SetStage("等待游戏窗口", 40);
        var waited = 0;
        var progress = 40;
        while (waited < 120000)
        {
            if (!_launching || _activeProcess is null || _activeProcess.HasExited)
                return;
            await System.Threading.Tasks.Task.Delay(700);
            waited += 700;
            try
            {
                if (_activeProcess.MainWindowHandle != IntPtr.Zero)
                {
                    SetStage("游戏运行中", 100);
                    LabLaunchingHint.Text = "KSP 已启动，关闭游戏后本页会自动恢复。";
                    StopTipTimer();
                    return;
                }
            }
            catch
            {
                return;
            }
            if (progress < 95)
            {
                progress += 3;
                SetStage("等待游戏窗口", progress);
            }
            else
            {
                SetStage("游戏加载中", 95);
            }
        }
        SetStage("游戏运行中", 100);
    }

    private void BtnLaunchCancel_Click(object sender, MouseButtonEventArgs e)
    {
        if (_activeProcess is { HasExited: false })
        {
            try
            {
                _activeProcess.Kill(true);
            }
                catch (System.Exception innerEx)
                {
                    PCL.Core.Logging.Swallowed.Report(innerEx);
                }
        }
        HideLaunching();
    }

    private void OnKspExited(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_activeProcess is not null)
            {
                try
                {
                    _activeProcess.Exited -= OnKspExited;
                }
                catch (System.Exception innerEx)
                {
                    PCL.Core.Logging.Swallowed.Report(innerEx);
                }
                try
                {
                    _activeProcess.Dispose();
                }
                catch (System.Exception innerEx)
                {
                    PCL.Core.Logging.Swallowed.Report(innerEx);
                }
                _activeProcess = null;
            }
            HideLaunching();
        }));
    }

    #region 启动中视图

    private void ShowLaunching()
    {
        if (_launching) return;
        _launching = true;
        PanInput.Visibility = Visibility.Collapsed;
        PanLaunching.Visibility = Visibility.Visible;
        PanLaunching.Opacity = 0d;
        ScaleLaunching.ScaleX = 0.9d;
        ScaleLaunching.ScaleY = 0.9d;
        LoadLaunching.State.LoadingState = LoadingState.Run;
        SetProgress(0);
        Animations.AniStart(new[]
        {
            Animations.AaOpacity(PanLaunching, 1d, 160, ease: new Animations.AniEaseOutFluent()),
            Animations.AaScaleTransform(PanLaunching, 0.1d, 160, ease: new Animations.AniEaseOutFluent())
        }, "PageLaunchLaunching", true);
    }

    private void HideLaunching()
    {
        if (!_launching)
            return;
        _launching = false;
        StopTipTimer();
        LoadLaunching.State.LoadingState = LoadingState.Stop;
        Animations.AniStart(new[]
        {
            Animations.AaOpacity(PanLaunching, -PanLaunching.Opacity, 140),
            Animations.AaScaleTransform(PanLaunching, 0.9d - ScaleLaunching.ScaleX, 140),
            Animations.AaCode(() =>
            {
                PanLaunching.Visibility = Visibility.Collapsed;
                PanInput.Visibility = Visibility.Visible;
            }, 140, true)
        }, "PageLaunchLaunching", true);
    }

    private void SetStage(string stage, int percent)
    {
        LabLaunchingStage.Text = stage;
        SetProgress(percent);
    }

    private void SetProgress(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        ProgressLaunchingFinished.Width = new GridLength(percent, GridUnitType.Star);
        ProgressLaunchingUnfinished.Width = new GridLength(100 - percent, GridUnitType.Star);
        LabLaunchingProgress.Text = percent + "%";
    }

    private void StartTipTimer()
    {
        StopTipTimer();
        _tipIndex = new Random().Next(LaunchTips.Length);
        LabLaunchingHint.Text = LaunchTips[_tipIndex];
        _tipTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _tipTimer.Tick += (_, _) =>
        {
            _tipIndex = (_tipIndex + 1) % LaunchTips.Length;
            LabLaunchingHint.Text = LaunchTips[_tipIndex];
        };
        _tipTimer.Start();
    }

    private void StopTipTimer()
    {
        if (_tipTimer is null) return;
        _tipTimer.Stop();
        _tipTimer = null;
    }

    #endregion

    private void BtnSelectInstance_Click(object sender, MouseButtonEventArgs e)
    {
        if (Window.GetWindow(this) is FormMain fm)
            fm.PageChange(FormMain.PageType.InstanceSelect);
    }

    private void BtnGotoSettings_Click(object sender, MouseButtonEventArgs e)
    {
        if (Window.GetWindow(this) is FormMain fm)
            fm.PageChange(FormMain.PageType.InstanceSetup);
    }
}