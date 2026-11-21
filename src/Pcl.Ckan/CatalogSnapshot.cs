using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Pcl.Ckan;

/// <summary>
/// 全量目录投影的磁盘快照。
/// CKAN 首次访问仓库数据时要解析本地 ~94 MB 的 repo-data（实测 19 秒），这段时间下载页只能空转。
/// 快照让页面先把上一次的全量结果画出来，真实目录随后在后台重算并覆盖；仓库文件或游戏版本一变，快照自动失效。
/// 快照只是加速手段：读失败、写失败、解析失败都静默退化为"没有快照"，绝不阻断正常流程。
/// </summary>
internal static class CatalogSnapshot
{
    private const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    private sealed record Envelope(int SchemaVersion, string Signature, List<ModSearchResult>? Items);

    /// <summary>由 repo-data 目录下每个文件的 名称/大小/写入时间 + 游戏版本 合成的指纹；任何一项变化都说明目录该重算。</summary>
    public static string BuildSignature(string dataDir, string? gameVersion)
    {
        var sb = new StringBuilder();
        sb.Append(SchemaVersion).Append('|').Append(gameVersion ?? "-").Append('|');
        try
        {
            var dir = Path.Combine(dataDir, "repo-data");
            if (Directory.Exists(dir))
            {
                foreach (var fi in Directory.EnumerateFiles(dir)
                             .Select(p => new FileInfo(p))
                             .OrderBy(f => f.Name, StringComparer.Ordinal))
                {
                    sb.Append(fi.Name).Append(':')
                      .Append(fi.Length).Append(':')
                      .Append(fi.LastWriteTimeUtc.Ticks).Append(';');
                }
            }
        }
        catch
        {
            // 取指纹失败就当作没有快照，宁可慢一次也不拿过期数据糊弄用户。
            return string.Empty;
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    /// <summary>读取快照；指纹不符、文件缺失、结构不兼容都返回 null。</summary>
    public static IReadOnlyList<ModSearchResult>? TryLoad(string path, string signature)
    {
        if (string.IsNullOrEmpty(signature) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var envelope = JsonSerializer.Deserialize<Envelope>(stream, Options);
            if (envelope is null || envelope.SchemaVersion != SchemaVersion)
            {
                return null;
            }
            if (!string.Equals(envelope.Signature, signature, StringComparison.Ordinal))
            {
                return null;
            }
            return envelope.Items is { Count: > 0 } ? envelope.Items : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>原子写入快照：先写临时文件再改名，避免进程中途退出留下半个 JSON。</summary>
    public static void Save(string path, string signature, IReadOnlyList<ModSearchResult> items)
    {
        if (string.IsNullOrEmpty(signature) || items.Count == 0)
        {
            return;
        }

        var temp = path + ".tmp";
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using (var stream = File.Create(temp))
            {
                JsonSerializer.Serialize(stream, new Envelope(SchemaVersion, signature, items.ToList()), Options);
            }
            File.Move(temp, path, true);
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }
}
