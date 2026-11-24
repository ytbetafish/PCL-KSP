using System.ComponentModel;
using System.Diagnostics;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.Utils;
using PCL.Core.Utils.OS;

namespace PCL;

public static class UpdateManager
{
    public static bool isUpdateWaitingRestart;

    public static UpdatesWrapperModel remoteServer = new(
        new List<IUpdateSource>
        {
            new UpdatesMirrorChyanModel(),
            new UpdatesRandomModel(
            [
                new UpdatesMinioModel("https://s3.pysio.online/pcl2-ce/", "Pysio"),
                new UpdatesMinioModel(
                    "https://staticassets.naids.com/resources/pclce/",
                    "Naids")
            ]),
            new UpdatesMinioModel(
                "https://github.com/PCL-Community/PCL2_CE_Server/raw/main/",
                "GitHub")
        });

    public static Loading.LoaderCombo<JsonObject> updateLoader;

    public static Loading.LoaderTask<int, int> serverLoader =
        new(
            Lang.Text("Update.Service.PclCe"),
            _ => LoadOnlineInfo(),
            priority: ThreadPriority.BelowNormal);

    public static bool IsCurrentVersionBeta
    {
        get
        {
            if (LauncherCommon.VersionBaseName.Contains("beta"))
                return true;
            return (int)Config.Update.UpdateChannel == 1;
        }
    }

    public static UpdateEnums.VersionStatus GetVersionStatus()
    {
        // PCL-KSP: 不再联网检查 PCL-CE 官方更新服务器，固定返回未知状态（不触发更新提示与反馈可用性判断）。
        return UpdateEnums.VersionStatus.Unknown;
    }

    public static void UpdateStart(
        UpdateEnums.UpdateType type,
        string? receivedKey = null,
        bool forceValidated = false)
    {
        // PCL-KSP: 已掐断从 PCL-CE 官方服务器下载/安装更新。
        return;
    }

    public static void UpdateRestart(bool triggerRestartAndByEnd, bool triggerRestart = true)
    {
        try
        {
            var fileName = LauncherPaths.ExecutableDirectory
                + @"PCL-KSP.exe";
            if (!File.Exists(fileName))
            {
                // PCL-KSP 未随包分发 PCL-CE 的更新器：直接重启自身，不要卡在"更新失败"。
                LauncherCommon.Log("[System] 未找到更新器，改为重启当前程序");
                if (triggerRestart)
                {
                    try { Process.Start(new ProcessStartInfo(Basics.ExecutablePath) { UseShellExecute = true }); }
                    catch (Exception ex) { LauncherCommon.Log("[System] 重启失败：" + ex.Message); return; }
                    Environment.Exit(0);
                }
                return;
            }

            // id old new restart
            var text =
                $"update {Process.GetCurrentProcess().Id} \"{Basics.ExecutablePath}\" \"{fileName}\" {(triggerRestart ? "true" : "false")}";
            LauncherCommon.Log("[System] 更新程序启动，参数：" + text);
            Process.Start(
                new ProcessStartInfo(fileName)
                {
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    Arguments = text
                });
            if (triggerRestartAndByEnd)
            {
                if (triggerRestart)
                {
                    try
                    {
                        Process.Start(
                            new ProcessStartInfo(Basics.ExecutablePath)
                            {
                                UseShellExecute = true
                            });
                    }
                    catch (Exception ex)
                    {
                        LauncherCommon.Log(ex, "重启启动器失败");
                    }
                }

                Environment.Exit(0);
                LauncherCommon.Log("[System] 已由于更新强制结束程序");
            }
        }
        catch (Win32Exception ex)
        {
            LauncherCommon.Log(ex, "自动更新时触发 Win32 错误，疑似被拦截");
            LauncherUi.Interaction?.ShowMessageBox(
                Lang.Text("Update.Error.UpdateBlockedMessage", LauncherPaths.ExecutableDirectory),
                Lang.Text("Update.Error.UpdateBlocked"),
                Lang.Text("Common.Action.Confirm"),
                true);
        }
    }

    /// <summary>
    ///     PCL-KSP: 已掐断下载官方最新版 PCL（整合包打包用途），方法保留为空实现以兼容残留调用。
    /// </summary>
    internal static void DownloadLatestPcl(Loading.LoaderBase? loaderToSyncProgress = null)
    {
        return;
    }

    private static void LoadOnlineInfo()
    {
        // PCL-KSP: 禁用 PCL-CE 自动更新检查与公告拉取
        return;
    }

    private static void ScheduleBasedOnConfig()
    {
        switch (Config.Update.UpdateMode)
        {
            case LauncherAutoUpdateBehavior.DownloadAndInstall:
                LauncherCommon.Log("[Update] 更新设置: 自动下载并安装更新");
                if (GetVersionStatus() != UpdateEnums.VersionStatus.Latest)
                    UpdateStart(UpdateEnums.UpdateType.Silent);
                break;

            case LauncherAutoUpdateBehavior.DownloadAndAnnounce:
                LauncherCommon.Log("[Update] 更新设置: 自动下载并提示更新");
                UpdateStart(UpdateEnums.UpdateType.DownloadAndPrompt);
                break;

            case LauncherAutoUpdateBehavior.AnnounceOnly:
                LauncherCommon.Log("[Update] 更新设置: 提示更新");
                UpdateStart(UpdateEnums.UpdateType.PromptOnly);
                break;

            case LauncherAutoUpdateBehavior.Disable:
            default:
                LauncherCommon.Log("[Update] 更新设置: 不自动检查更新");
                return;
        }
    }

    /// <summary>
    ///     展示社区版提示
    /// </summary>
    /// <param name="IsUpdate">是否为更新时启动</param>
    public static void ShowCeAnnounce()
    {
        LauncherUi.Interaction?.ShowMessageBox(
            Lang.Text("Update.CommunityNotice.Body"),
            Lang.Text("Update.CommunityNotice.Title"),
            Lang.Text("Update.CommunityNotice.Confirm"));
    }
}