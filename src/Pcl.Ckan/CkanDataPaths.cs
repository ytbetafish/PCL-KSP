using System;
using System.IO;
using CKAN.Games.KerbalSpaceProgram.GameVersionProviders;

namespace Pcl.Ckan;

/// <summary>
///     CKAN.Core 里有几处把缓存路径写死在用户目录（<c>%LOCALAPPDATA%\CKAN</c>），
///     这里在启动时统一改指到本启动器的数据目录，避免在用户目录下生成 CKAN 专用文件夹。
/// </summary>
public static class CkanDataPaths
{
    private static string? _redirectedTo;

    /// <summary>
    ///     把 CKAN.Core 写死到用户目录的缓存文件改指到 <paramref name="dataDir" />。
    /// </summary>
    /// <param name="dataDir">CKAN 数据目录，通常是 %APPDATA%\PCL-KSP\Ckan。</param>
    public static void RedirectUserCaches(string? dataDir)
    {
        if (string.IsNullOrWhiteSpace(dataDir)) return;
        try
        {
            var full = Path.GetFullPath(dataDir);
            if (string.Equals(_redirectedTo, full, StringComparison.OrdinalIgnoreCase)) return;

            var target = Path.Combine(full, "builds-ksp.json");
            // 首次切换：旧位置已经有下载好的版本映射就直接搬过来，省一次联网。
            if (!File.Exists(target) && File.Exists(KspBuildMap.DefaultCachedBuildMapPath))
            {
                Directory.CreateDirectory(full);
                File.Copy(KspBuildMap.DefaultCachedBuildMapPath, target, true);
            }

            KspBuildMap.CachedBuildMapPath = target;
            _redirectedTo = full;
        }
        catch (Exception ex)
        {
            Swallow.Report(ex);
        }
    }
}
