using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace Pcl.Ckan;

/// <summary>
///     空 catch 的统一出口（CKAN 适配层用；这一层不引用 PCL.Core，所以单独实现一份）。
///     与 PCL.Core 的 <c>Swallowed</c> 同一套策略：带调用点写日志，同一调用点 60 秒内只落一次，
///     其余只累计合并数。输出走 <see cref="Sink" />，由主程序在初始化时接到自己的日志文件；
///     没人接的时候退化为 <see cref="Debug.WriteLine" />，不会静默丢失。
/// </summary>
public static class Swallow
{
    /// <summary>日志输出通道。主程序启动 CKAN 时接到自己的日志实现。</summary>
    public static Action<string>? Sink { get; set; }

    /// <summary>同一调用点两次落盘的最小间隔。</summary>
    public static TimeSpan QuietWindow { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>最多记住多少个不同调用点；超出后只计数不落盘。</summary>
    public static int MaxSites { get; set; } = 512;

    private const int MaxMessageChars = 400;

    private sealed class Site
    {
        public long LastTicks;
        public int Merged;
    }

    private static readonly ConcurrentDictionary<string, Site> _sites = new();
    private static long _mergedTotal;

    /// <summary>被限流合并掉的累计条数，供自测断言使用。</summary>
    public static long MergedTotal => Interlocked.Read(ref _mergedTotal);

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
            Emit("[swallow] " + text);
        }
        catch
        {
            // 日志本身绝不允许再抛异常（也不回写日志，避免递归）。
        }
    }

    private static void Emit(string message)
    {
        var sink = Sink;
        if (sink is not null)
        {
            try
            {
                sink(message);
                return;
            }
            catch
            {
                // 接日志时出错就退化到 Debug，不影响调用方，也不回写日志。
            }
        }

        Debug.WriteLine(message);
    }

    private static string Build(string fileName, int line, string member, Exception? ex)
    {
        var sb = new StringBuilder(160);
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
