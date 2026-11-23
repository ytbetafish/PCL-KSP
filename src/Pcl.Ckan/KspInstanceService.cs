using CKAN;
using CKAN.Configuration;
using CKAN.Games;

namespace Pcl.Ckan;

/// <summary>
/// Detects KSP installations on disk and loads them through CKAN's <see cref="GameInstance"/>
/// machinery. Detection combines CKAN's own Steam/registry scan with a hard-coded priority list
/// of known GOG/Steam locations and a scan of adjacent directories that look like a KSP root.
/// </summary>
public sealed class KspInstanceService
{
    private readonly IUser _user;
    private readonly string _dataDir;
    private readonly IGame _kspGame = KnownGames.GameByShortName("KSP")
        ?? throw new InvalidOperationException("CKAN could not resolve the KSP game definition.");

    public KspInstanceService(IUser? user = null, string? dataDir = null)
    {
        _user = user ?? new NullUser();
        _dataDir = dataDir ?? Path.Combine(Path.GetTempPath(), "Pcl.Ckan");
        CkanDataPaths.RedirectUserCaches(_dataDir);
    }

    /// <summary>
    /// Heuristic check: does <paramref name="dir"/> look like a KSP root (KSP_x64.exe / KSP.exe
    /// plus a GameData folder)?
    /// </summary>
    public static bool IsKspRoot(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            return false;
        }

        bool hasExe = File.Exists(Path.Combine(dir, "KSP_x64.exe"))
                   || File.Exists(Path.Combine(dir, "KSP.exe"));
        bool hasGameData = Directory.Exists(Path.Combine(dir, "GameData"));
        return hasExe && hasGameData;
    }

    /// <summary>
    /// Enumerate candidate KSP root directories in priority order. This never throws: detection
    /// failures (no Steam, no registry access) simply yield fewer candidates.
    /// </summary>
    public IReadOnlyList<string> FindCandidateRoots()
    {
        var candidates = new List<string>();

        // 0) Explicitly known install (GOG bundle on this machine).
        

        // 1) Common GOG / Steam locations.
        foreach (var path in WellKnownPaths())
        {
            TryAdd(candidates, path);
        }

        // 2) CKAN's own detection (Steam registry + libraryfolder vdfs).
        try
        {
            Directory.CreateDirectory(_dataDir);
            var config = new JsonConfiguration(Path.Combine(_dataDir, "config.json"));
            using var mgr = new GameInstanceManager(_user, config);
            foreach (var inst in mgr.FindDefaultInstances())
            {
                if (!string.IsNullOrWhiteSpace(inst.GameDir))
                {
                    TryAdd(candidates, inst.GameDir);
                }
            }
        }
            catch (System.Exception innerEx)
            {
                Swallow.Report(innerEx);
            }

        // 3) Scan every fixed drive for adjacent "Kerbal Space Program*" directories that
        //    contain KSP_x64.exe + GameData (covers GOG copies next to the primary install).
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed))
        {
            try
            {
                var root = drive.Name.TrimEnd('\\');
                foreach (var sub in FindCandidatesUnder(root))
                {
                    TryAdd(candidates, sub);
                }
            }
                catch (System.Exception innerEx)
                {
                    Swallow.Report(innerEx);
                }
        }

        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Scan the direct children of <paramref name="root"/> for "Kerbal Space Program*" directories
    /// that are a real install (KSP_x64.exe/KSP.exe + GameData). Never throws.
    /// </summary>
    public IReadOnlyList<string> FindCandidatesUnder(string root)
    {
        var found = new List<string>();
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return found;
        }
        foreach (var sub in SafeEnumerateDirectories(root))
        {
            // sub 是完整路径（如 C:\Kerbal Space Program），只能比较末段目录名。
            var name = Path.GetFileName(sub);
            if (name.StartsWith("Kerbal Space Program", StringComparison.OrdinalIgnoreCase) && IsKspRoot(sub))
            {
                found.Add(sub);
            }
        }
        return found;
    }

    /// <summary>
    /// Probe a directory and return its <see cref="KspInstanceInfo"/> if it looks like a KSP root.
    /// The version comes from CKAN's build map; if that is unavailable, we fall back to the
    /// build id recorded in buildID64.txt.
    /// </summary>
    public KspInstanceInfo? Probe(string rootPath)
    {
        if (!IsKspRoot(rootPath))
        {
            return null;
        }

        string? version = null;
        try
        {
            var inst = Load(rootPath, Path.GetFileName(rootPath));
            version = inst.Version()?.ToString();
        }
            catch (System.Exception innerEx)
            {
                Swallow.Report(innerEx);
            }

        version ??= ReadBuildId(rootPath);
        return new KspInstanceInfo(rootPath, version);
    }

    /// <summary>
    /// Build a CKAN <see cref="GameInstance"/> for an already-validated KSP root.
    /// </summary>
    public GameInstance Load(string rootPath, string? name = null)
    {
        if (!IsKspRoot(rootPath))
        {
            throw new DirectoryNotFoundException($"Not a KSP root: {rootPath}");
        }

        return new GameInstance(_kspGame, rootPath, name ?? Path.GetFileName(rootPath));
    }

    /// <summary>
    /// Read the build id from <c>buildID64.txt</c> (e.g. "build id = 03190").
    /// </summary>
    internal static string? ReadBuildId(string rootPath)
    {
        try
        {
            var file = Path.Combine(rootPath, "buildID64.txt");
            if (!File.Exists(file))
            {
                return null;
            }

            foreach (var line in File.ReadLines(file))
            {
                const string needle = "build id =";
                var idx = line.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    return line[(idx + needle.Length)..].Trim();
                }
            }
        }
            catch (System.Exception innerEx)
            {
                Swallow.Report(innerEx);
            }

        return null;
    }

    private static IEnumerable<string> WellKnownPaths()
    {
        var progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrEmpty(progFiles))
        {
            yield return Path.Combine(progFiles, "Steam", "steamapps", "common", "Kerbal Space Program");
        }

        var progFiles64 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrEmpty(progFiles64))
        {
            yield return Path.Combine(progFiles64, "Steam", "steamapps", "common", "Kerbal Space Program");
        }

        // GOG / manual layouts：遍历各固定磁盘的常见安装目录，避免硬编码具体盘符。
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType != DriveType.Fixed || !drive.IsReady)
            {
                continue;
            }
            var root = drive.RootDirectory.FullName;
            yield return Path.Combine(root, "Steam", "steamapps", "common", "Kerbal Space Program");
            yield return Path.Combine(root, "Games", "Kerbal Space Program");
            yield return Path.Combine(root, "Kerbal Space Program");
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static void TryAdd(List<string> list, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }
        try
        {
            // CKAN 自己的检测会给出 "D:Kerbal Space Program" 这种盘符相对路径，归一化成绝对路径，
            // 否则同一个实例会以两种写法重复出现在候选列表里（显示与去重都会乱）。
            path = Path.GetFullPath(path);
        }
        catch
        {
            return;
        }
        if (IsKspRoot(path))
        {
            list.Add(path);
        }
    }
}
