using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PCL.Core.Utils.Secret;

namespace PCL;

/// <summary>
///     PCL-KSP 自己的设置（KSP 路径 + 翻译服务选项）。
///     不走 PCL 的 source-generator Config 系统，避免动 PCL.Core。
///     持久化到 %APPDATA%\PCL-KSP\pclksp-settings.json，保证 exe 位于只读目录（如 Program Files、
///     解压到受保护位置的 Release zip）时也能读写，避免 Access denied。
///     若旧版本曾把配置写在 exe 同目录，启动时自动迁移一次。
/// </summary>
public static class KspSettings
{
    private static readonly string _path = ResolvePath();

    private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

    public static SettingsData Current { get; private set; } = new();

    public class SettingsData
    {
        public string KspRootPath { get; set; } = "";
        public string TranslateBaseUrl { get; set; } = "https://api.deepseek.com/v1";
        /// <summary>内存中的明文密钥。不参与序列化：落盘只写 <see cref="TranslateApiKeyEnc"/>。</summary>
        [JsonIgnore]
        public string TranslateApiKey { get; set; } = "";
        /// <summary>
        /// 旧版设置文件里的明文密钥，仅用于一次性迁移读取；空值时不再序列化到文件，
        /// 只有 DPAPI 不可用、必须退回明文时才会重新出现。
        /// </summary>
        [JsonPropertyName("TranslateApiKey")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? TranslateApiKeyPlainLegacy { get; set; }
        /// <summary>DPAPI 加密后的密钥密文（v1: 前缀标识加密格式）。</summary>
        [JsonPropertyName("TranslateApiKeyEnc")]
        public string TranslateApiKeyEnc { get; set; } = "";
        public string TranslateModel { get; set; } = "deepseek-chat";

        public string CkanDataDir { get; set; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCL-KSP", "Ckan");
        public string CkanMasterRepoUrl { get; set; } = "https://github.com/KSP-CKAN/CKAN-meta/archive/master.tar.gz";
        public List<string> CkanMirrorUrls { get; set; } = new() { DefaultMirrorUrl };
        // MirrorOnly / OfficialFirst / OfficialOnly
        public string CkanRepoStrategy { get; set; } = "MirrorOnly";
        /// <summary>是否已做过「补默认镜像」的一次性迁移，避免用户清空镜像列表后又被写回来。</summary>
        public bool CkanMirrorSeeded { get; set; }
        public string CkanStabilityTolerance { get; set; } = "Stable";

        /// <summary>是否对 CKAN 的 GitHub 下载链接套用公共加速前缀。</summary>
        public bool GitHubAccelerationEnabled { get; set; } = true;
        /// <summary>加速前缀，直接拼在原地址前。默认 ghfast.top。</summary>
        public string GitHubAccelerationPrefix { get; set; } = "https://ghfast.top/";

        public string HttpProxy { get; set; } = "";
        public int DownloadConcurrency { get; set; } = 4;
        public int HttpTimeoutSeconds { get; set; } = 30;

        public List<string> Instances { get; set; } = new();
        /// <summary>实例的自定义显示名（键为实例根目录，值为用户起的名字）。</summary>
        public Dictionary<string, string> InstanceNames { get; set; } = new();
        public string KspExePath { get; set; } = "";
        public string KspGameDataPath { get; set; } = "";
        public string KspLogsPath { get; set; } = "";

        /// <summary>主页显示方式：0 空白、1 预设、2 本地 XAML 文件、3 自定义网址。</summary>
        public int HomePageMode { get; set; } = 0;
        /// <summary>预设主页编号：0 PCL-KSP 内置主页、1 你知道吗。</summary>
        public int HomePagePreset { get; set; } = 0;
        /// <summary>「本地文件」模式下的主页 XAML 路径。</summary>
        public string HomePageLocalFile { get; set; } = "";
        /// <summary>「自定义网址」模式下的主页 XAML 地址。</summary>
        public string HomePageUrl { get; set; } = "";
    }

    /// <summary>国内公开的 CKAN 元数据镜像（坎喵 KerbCat 公共加速源）。</summary>
    private const string DefaultMirrorUrl = "https://api.kerbcat.cn/ckan/public/master.tar.gz";

    private static string ResolvePath()
    {
        // 首选 %APPDATA%\PCL-KSP（每个用户、必然可写）。
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCL-KSP");
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, "pclksp-settings.json");

        // 迁移旧版（工程名为 PKL 时期）的 exe 同目录配置。
        try
        {
            var legacy = Path.Combine(LauncherPaths.ExecutableDirectory, "pkl-settings.json");
            if (!File.Exists(target) && File.Exists(legacy))
                File.Copy(legacy, target, true);
            // 旧版配置目录（%APPDATA%\PKL）也迁移一次。
            var legacyShared = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PKL", "pkl-settings.json");
            if (!File.Exists(target) && File.Exists(legacyShared))
                File.Copy(legacyShared, target, true);
        }
        catch
        {
            // 迁移失败不致命，继续使用默认/现有路径。
        }
        return target;
    }

    static KspSettings()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                Current = JsonSerializer.Deserialize<SettingsData>(json) ?? new SettingsData();

                // 明文密钥迁移：有密文先解回内存；旧版明文则在下次 Save 时转成密文。
                Current.TranslateApiKeyPlainLegacy ??= "";
                if (!string.IsNullOrEmpty(Current.TranslateApiKeyEnc))
                    Current.TranslateApiKey = DecryptForStore(Current.TranslateApiKeyEnc) ?? Current.TranslateApiKeyPlainLegacy ?? "";
                else
                    Current.TranslateApiKey = Current.TranslateApiKeyPlainLegacy;
            }
        }
        catch
        {
            Current = new SettingsData();
        }

        // 一次性迁移：老配置里镜像列表为空时补上默认国内镜像；用户之后手动清空则不再回填。
        try
        {
            if (!Current.CkanMirrorSeeded)
            {
                Current.CkanMirrorUrls ??= new List<string>();
                if (Current.CkanMirrorUrls.Count == 0)
                    Current.CkanMirrorUrls.Add(DefaultMirrorUrl);
                Current.CkanMirrorSeeded = true;
                Save();
            }
        }
        catch
        {
            // 回填失败不致命
        }

        // 一次性迁移：旧版（工程名 PKL 时期）把 CKAN 数据目录放在 %APPDATA%\PKL\Ckan。
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var legacyCkan = Path.Combine(appData, "PKL", "Ckan");
            var newCkan = Path.Combine(appData, "PCL-KSP", "Ckan");
            var current = (Current.CkanDataDir ?? "").TrimEnd('\\', '/');
            if (current.Length > 0
                && current.Equals(legacyCkan.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            {
                // 旧目录还在、新目录还没有时才整体搬过去，避免覆盖新目录里已有的数据。
                if (Directory.Exists(legacyCkan) && !Directory.Exists(newCkan))
                {
                    try { Directory.Move(legacyCkan, newCkan); }
                    catch { /* 搬家失败就用新路径重新初始化 */ }
                }
                Current.CkanDataDir = newCkan;
                Save();
            }
        }
        catch
        {
            // 迁移失败不致命
        }
    }

    /// <summary>实例在下拉框/列表里显示的名字：有自定义名用自定义名，否则用目录名。</summary>
    public static string GetDisplayName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "（未设置）";
        }
        if (Current.InstanceNames != null
            && Current.InstanceNames.TryGetValue(path, out var name)
            && !string.IsNullOrWhiteSpace(name))
        {
            return name;
        }
        var dirName = Path.GetFileName(path.TrimEnd('\\', '/'));
        return string.IsNullOrWhiteSpace(dirName) ? path : dirName;
    }

    /// <summary>设置实例显示名；传空表示恢复为目录名。</summary>
    public static void SetDisplayName(string path, string? name)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }
        Current.InstanceNames ??= new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(name))
        {
            Current.InstanceNames.Remove(path);
        }
        else
        {
            Current.InstanceNames[path] = name.Trim();
        }
    }

    /// <summary>设置文件路径（自测用）。</summary>
    internal static string SettingsFilePath => _path;

    /// <summary>把密钥转成落盘密文；空值返回空串，加密不可用返回 null。</summary>
    internal static string? EncryptForStore(string? plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";
        try { return "v1:" + EncryptHelper.SecretEncrypt(plain); }
        catch { return null; }
    }

    /// <summary>解回密文；格式不认识或解不开返回 null，由调用方决定回退。</summary>
    internal static string? DecryptForStore(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return "";
        try
        {
            var payload = stored.StartsWith("v1:", StringComparison.Ordinal) ? stored.Substring(3) : stored;
            return EncryptHelper.SecretDecrypt(payload);
        }
        catch { return null; }
    }

    public static void Save()
    {
        try
        {
            // 密钥只落盘密文；未填密钥时两个密钥字段都不写。DPAPI 不可用时退回明文，避免已填的钥匙升级后失效。
            var key = Current.TranslateApiKey ?? "";
            if (key.Length == 0)
            {
                Current.TranslateApiKeyEnc = "";
                Current.TranslateApiKeyPlainLegacy = null;
            }
            else
            {
                var encrypted = EncryptForStore(key);
                if (encrypted is null)
                {
                    Current.TranslateApiKeyEnc = "";
                    Current.TranslateApiKeyPlainLegacy = key;
                }
                else
                {
                    Current.TranslateApiKeyEnc = encrypted;
                    Current.TranslateApiKeyPlainLegacy = null;
                }
            }

            // 原子写：先写临时文件再替换，避免写一半或被占用导致的损坏/权限异常。
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, _jsonOpts));
            if (File.Exists(_path))
                File.Replace(tmp, _path, _path + ".bak");
            else
                File.Move(tmp, _path);
        }
        catch
        {
            // 写盘失败不致命，但不静默吞掉——至少留个痕迹。
            try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "pclksp-settings-save-error.log"),
                $"[{DateTime.Now}] save to {_path} failed{Environment.NewLine}"); } catch { }
        }
    }
}
