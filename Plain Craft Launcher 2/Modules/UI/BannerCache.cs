using System.Collections.Concurrent;
using System.IO;
using PCL.Core.App;
using PCL.Core.IO.Net.Http;

namespace PCL;

/// <summary>
/// 模组横幅图的按需加载与磁盘缓存。
/// 首次渲染时只把图片排进下载队列（全局最多 2 个并发），下载完成后才显示；
/// 图片落盘在 %LOCALAPPDATA%\PKL\Cache\Banners，之后同一张图直接读本地，不再联网。
/// 缓存总量超过上限时按最后访问时间淘汰最旧的文件。
/// </summary>
public static class BannerCache
{
    private const int MaxConcurrent = 2;
    private const long MaxCacheBytes = 150L * 1024 * 1024;

    private static readonly SemaphoreSlim Gate = new(MaxConcurrent, MaxConcurrent);
    private static readonly ConcurrentDictionary<string, Task<string?>> Pending = new();
    private static string? _dir;

    private static string Dir => _dir ??= Path.Combine(Paths.SharedLocalData, "Cache", "Banners");

    /// <summary>本地已有缓存时返回文件路径，否则返回 null。</summary>
    public static string? TryGetLocal(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        try
        {
            var key = Key(url);
            var dir = Dir;
            if (!Directory.Exists(dir))
                return null;

            foreach (var file in Directory.EnumerateFiles(dir, key + ".*"))
            {
                if (new FileInfo(file).Length > 0)
                    return file;
            }
        }
        catch (Exception ex)
        {
            Common.Log(ex, $"读取横幅缓存失败（url = {url}）", Common.LogLevel.Developer);
        }

        return null;
    }

    /// <summary>确保该横幅已缓存：命中直接返回本地路径，否则排队下载（同 URL 只会下载一次）。</summary>
    public static Task<string?> EnsureAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return Task.FromResult<string?>(null);

        var cached = TryGetLocal(url);
        if (cached is not null)
            return Task.FromResult<string?>(cached);

        return Pending.GetOrAdd(url, key =>
        {
            var task = DownloadAsync(key);
            task.ContinueWith(_ => Pending.TryRemove(key, out _), TaskScheduler.Default);
            return task;
        });
    }

    private static async Task<string?> DownloadAsync(string url)
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Dir);
            var bytes = await HttpRequest.Create(url).SendAsync(addMetedata: false)
                .ContinueWith(async resp =>
                {
                    using (resp.Result)
                    {
                        resp.Result.EnsureSuccessStatusCode();
                        await using var stream = await resp.Result.AsStreamAsync().ConfigureAwait(false);
                        var buffer = new MemoryStream();
                        await stream.CopyToAsync(buffer).ConfigureAwait(false);
                        return buffer.ToArray();
                    }
                }, TaskScheduler.Default).Unwrap().ConfigureAwait(false);

            if (bytes.Length == 0)
                return null;

            var ext = GuessExtension(bytes);
            var path = Path.Combine(Dir, Key(url) + ext);
            var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllBytesAsync(tempPath, bytes).ConfigureAwait(false);
            File.Move(tempPath, path, true);
            PruneIfNeeded();
            return path;
        }
        catch (Exception ex)
        {
            Common.Log(ex, $"下载横幅图失败（url = {url}）", Common.LogLevel.Developer);
            return null;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>按文件头判断扩展名，避免 WPF 因扩展名与实际格式不符而解码失败。</summary>
    private static string GuessExtension(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return ".jpg";
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return ".png";
        if (bytes.Length >= 6 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46)
            return ".gif";
        if (bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
            && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
            return ".webp";
        return ".png";
    }

    private static string Key(string url) => Common.GetStringMd5(url);

    /// <summary>缓存超限时按最后写入时间淘汰，保留 70%。</summary>
    private static void PruneIfNeeded()
    {
        try
        {
            var files = Directory.EnumerateFiles(Dir).Select(f => new FileInfo(f)).ToList();
            var total = files.Sum(f => f.Length);
            if (total <= MaxCacheBytes)
                return;

            var target = (long)(MaxCacheBytes * 0.7);
            foreach (var file in files.OrderBy(f => f.LastWriteTimeUtc))
            {
                if (total <= target)
                    break;
                total -= file.Length;
                file.Delete();
            }
        }
        catch (Exception ex)
        {
            Common.Log(ex, "清理横幅缓存失败", Common.LogLevel.Developer);
        }
    }
}
