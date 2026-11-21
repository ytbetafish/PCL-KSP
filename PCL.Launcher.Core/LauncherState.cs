namespace PCL;

/// <summary>
///     业务层共享的启动器运行状态。
/// </summary>
public static class LauncherState
{
    /// <summary>当前选中的 KSP 安装路径。</summary>
    public static string? SelectedKspDirectory { get; set; }
}
