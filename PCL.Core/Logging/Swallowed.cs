using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;

namespace PCL.Core.Logging;

/// <summary>
///     空 catch 的统一出口：写入一条带调用点（文件:行 + 方法名）的 Info 日志。
///     对同一调用点做限流：<see cref="QuietWindow" /> 之内重复发生只累计、不再落盘，
///     下次落盘时把被合并的条数一起带出来。这样高频路径也不会把日志刷满，
///     配合 <see cref="LoggerConfiguration.MaxFileSize" /> 的单文件上限，磁盘占用是有界的。
/// </summary>
public static class Swallowed
{
    /// <summary>同一调用点两次落盘的最小间隔。</summary>
    public static TimeSpan QuietWindow { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>最多记住多少个不同调用点；超出后只计数不落盘，避免字典无限增长。</summary>
    public static int MaxSites { get; set; } = 512;

    private const int MaxMessageChars = 400;
    private const string Module = "swallow";

    private sealed class Site
    {
        public long LastTicks;
        public int Merged;
    }

    private static readonly ConcurrentDictionary<string, Site> _sites = new();
    private static long _mergedTotal;

    /// <summary>被限流合并掉的累计条数，供自测断言使用。</summary>
    public static long MergedTotal => Interlocked.Read(ref _mergedTotal);

    /// <summary>当前已记录的调用点数量。</summary>
    public static int SiteCount => _sites.Count;

    public static void Report(
        Exception? ex,
        [CallerMemberName] string member = "",
        [CallerLineNumber] int line = 0,
        [CallerFilePath] string file = "")
    {
        try
        {
            var fileName = string.IsNullOrEmpty(file) ? "?" : Path.GetFileName(file);
            var key = fileName + ":" + line;

            var site = _sites.TryGetValue(key, out var known)
                ? known
                : _sites.Count < MaxSites
                    ? _sites.GetOrAdd(key, static _ => new Site())
                    : null;

            if (site is null)
            {
                // 站点数已达上限：只计数，不落盘，也不让字典继续长。
                Interlocked.Increment(ref _mergedTotal);
                return;
            }

            string text;
            int merged;
            lock (site)
            {
                var now = DateTime.UtcNow.Ticks;
                if (site.LastTicks != 0 && now - site.LastTicks < QuietWindow.Ticks)
                {
                    if (site.Merged < int.MaxValue) site.Merged++;
                    Interlocked.Increment(ref _mergedTotal);
                    return;
                }

                merged = site.Merged;
                site.Merged = 0;
                site.LastTicks = now;
                text = Build(fileName, line, member, ex);
            }

            if (merged > 0) text += $"（本窗口另有 {merged} 次同类异常已合并）";
            LogWrapper.Info(Module, text);
        }
        catch
        {
            // 日志本身绝不允许再抛异常。
        }
    }

    private static string Build(string fileName, int line, string member, Exception? ex)
    {
        var sb = new System.Text.StringBuilder(160);
        sb.Append($"{fileName}:{line} {member} -> ");

        if (ex is null)
        {
            sb.Append("catch 未拿到异常对象");
            return sb.ToString();
        }

        sb.Append(ex.GetType().Name).Append(": ").Append(Trim(ex.Message));

        var frame = FirstFrame(ex);
        if (frame.Length > 0) sb.Append(" @ ").Append(frame);

        var inner = ex.InnerException;
        if (inner is not null)
            sb.Append(" <- ").Append(inner.GetType().Name).Append(": ").Append(Trim(inner.Message));

        return sb.ToString();
    }

    private static string Trim(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var one = s.Replace("\r", " ").Replace("\n", " ");
        if (one.Length > MaxMessageChars) one = one.Substring(0, MaxMessageChars) + "…";
        return one;
    }

    private static string FirstFrame(Exception ex)
    {
        var stack = ex.StackTrace;
        if (string.IsNullOrEmpty(stack)) return "";
        foreach (var raw in stack.Split('\n'))
        {
            var f = raw.Trim();
            if (f.Length > 0) return f.Length > 120 ? f.Substring(0, 120) : f;
        }
        return "";
    }
}
