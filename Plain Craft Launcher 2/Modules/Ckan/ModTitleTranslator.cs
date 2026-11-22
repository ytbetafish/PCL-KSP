using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.Logging;
using PCL.Translation;

namespace PCL;

/// <summary>
///     「一键翻译模组标题」的公共实现：把英文模组名批量翻成中文，显示时写成「中文译文 · 英文原名」，
///     英文原名始终保留在后面，不覆盖原文。
///     译文按原文标题落盘缓存（%APPDATA%\PCL-KSP\mod-title-zh.json），重启后仍然生效，
///     也避免对同一个标题反复请求免费接口。
/// </summary>
internal static class ModTitleTranslator
{
    /// <summary>单次点击最多翻译多少条：免费接口有频率限制，不该把几千条的目录一次性打满。</summary>
    private const int MaxPerPress = 150;

    /// <summary>并发请求数：小并发即可，过多容易被腾讯/MyMemory 限流。</summary>
    private const int Concurrency = 3;

    private static readonly object Sync = new();
    private static Dictionary<string, string>? _cache;

    private static string CacheFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCL-KSP", "mod-title-zh.json");

    /// <summary>取已缓存的中文标题；没有译文、或标题本身已含中文时返回 null。</summary>
    public static string? TryGet(string? rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
            return null;
        var raw = rawName.Trim();
        if (ContainsChinese(raw))
            return null;
        lock (Sync)
        {
            var map = LoadUnsafe();
            return map.TryGetValue(raw, out var zh) && !string.IsNullOrWhiteSpace(zh) ? zh : null;
        }
    }

    /// <summary>列表里用的标题：有译文时「中文 · 英文原名」，否则原样返回原文。</summary>
    public static string Prefix(string? rawName)
    {
        var raw = (rawName ?? string.Empty).Trim();
        var zh = TryGet(raw);
        return zh is null ? raw : zh + " · " + raw;
    }

    /// <summary>
    ///     批量补翻缺失的标题。onProgress(done, total) 来自后台线程，调用方需自行切回 UI 线程。
    /// </summary>
    /// <returns>本次实际新增的译文条数与失败条数。</returns>
    public static async Task<(int Translated, int Failed)> TranslateMissingAsync(
        IReadOnlyList<string> rawNames,
        Action<int, int>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        var candidates = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        lock (Sync)
        {
            var map = LoadUnsafe();
            foreach (var name in rawNames)
            {
                var raw = (name ?? string.Empty).Trim();
                if (raw.Length == 0 || ContainsChinese(raw) || !seen.Add(raw) || map.ContainsKey(raw))
                    continue;
                candidates.Add(raw);
                if (candidates.Count >= MaxPerPress)
                    break;
            }
        }

        var total = candidates.Count;
        onProgress?.Invoke(0, total);
        if (total == 0)
            return (0, 0);

        var translated = 0;
        var failed = 0;
        var done = 0;
        var service = TranslationServiceFactory.Create();
        await Parallel.ForEachAsync(
            candidates,
            new ParallelOptions { MaxDegreeOfParallelism = Concurrency, CancellationToken = cancellationToken },
            async (raw, token) =>
            {
                if (token.IsCancellationRequested)
                    return;

                string? zh = null;
                try
                {
                    zh = await service.TranslateAsync(raw, "zh-CN").ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Swallowed.Report(ex);
                }

                zh = zh?.Trim().Trim('"', '\'', '“', '”').Trim();
                var ok = !string.IsNullOrWhiteSpace(zh)
                         && ContainsChinese(zh!)
                         && !string.Equals(zh, raw, StringComparison.OrdinalIgnoreCase);
                lock (Sync)
                {
                    if (ok)
                    {
                        LoadUnsafe()[raw] = zh!;
                        translated++;
                    }
                    else
                    {
                        failed++;
                    }
                }

                onProgress?.Invoke(Interlocked.Increment(ref done), total);
            }).ConfigureAwait(false);

        Save();
        return (translated, failed);
    }

    /// <summary>把内存里的译文缓存写回磁盘。</summary>
    public static void Save()
    {
        try
        {
            lock (Sync)
            {
                var map = LoadUnsafe();
                if (map.Count == 0)
                    return;
                var dir = Path.GetDirectoryName(CacheFile);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(CacheFile, json, new UTF8Encoding(false));
            }
        }
        catch (Exception ex)
        {
            Swallowed.Report(ex);
        }
    }

    /// <summary>标题是否已经含中文（已是中文标题就跳过翻译）。</summary>
    private static bool ContainsChinese(string text)
    {
        foreach (var ch in text)
        {
            if (ch is >= '\u4e00' and <= '\u9fff')
                return true;
        }
        return false;
    }

    /// <summary>惰性读取磁盘缓存（调用方必须持有 <see cref="Sync" />）。</summary>
    private static Dictionary<string, string> LoadUnsafe()
    {
        if (_cache is not null)
            return _cache;

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(CacheFile))
            {
                var json = File.ReadAllText(CacheFile, new UTF8Encoding(false));
                var stored = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (stored is not null)
                {
                    foreach (var pair in stored)
                    {
                        if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                            map[pair.Key.Trim()] = pair.Value.Trim();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Swallowed.Report(ex);
        }

        _cache = map;
        return map;
    }
}
