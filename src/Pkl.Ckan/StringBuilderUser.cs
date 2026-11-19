using System.Text;

namespace Pkl.Ckan;

/// <summary>
/// IUser implementation that accumulates all CKAN log/progress/error messages into a StringBuilder.
/// Used to back the "view log" window after install/uninstall runs.
/// </summary>
public sealed class StringBuilderUser : CKAN.IUser
{
    private readonly StringBuilder _sb = new();
    private readonly object _lock = new();

    public string Dump() { lock (_lock) return _sb.ToString(); }
    public void Clear() { lock (_lock) _sb.Clear(); }

    /// <summary>CKAN 报告的进度（消息原文 + 0-100 的百分比），供任务管理页展示。</summary>
    public event Action<string, int>? ProgressReported;

    /// <summary>CKAN 报告的一般消息（下载开始、安装完成等），供任务管理页展示。</summary>
    public event Action<string>? MessageReported;

    public bool Headless => true;

    public bool RaiseYesNoDialog(string question)
    {
        Append($"[YesNo] {question}");
        return true;
    }

    public int RaiseSelectionDialog(string message, params object[] args)
    {
        Append($"[Select] {message} :: {string.Join(", ", args)}");
        return 0;
    }

    public void RaiseError(string message, params object[] args)
    {
        Append($"[Error] {string.Format(message, args)}");
    }

    public void RaiseProgress(string message, int percent)
    {
        Append($"[Progress {percent}%] {message}");
        try { ProgressReported?.Invoke(message, percent); } catch { /* 回调异常不得影响 CKAN */ }
    }

    /// <summary>CKAN 的下载速率（字节/秒），供任务管理页显示左边栏的「下载速度」。</summary>
    public event Action<long>? SpeedReported;

    /// <summary>最近一次 CKAN 报告的下载速率与剩余字节数。</summary>
    public long LastBytesPerSecond { get; private set; }
    public long LastBytesLeft { get; private set; }

    /// <summary>
    /// 本次安装预估的总字节数（下载 + 安装）。CKAN 报上来的 ByteRateCounter 在某些模组上
    /// Size 为 0（元数据缺 download_size / install_size），此时 Percent 恒为 0，
    /// 用这个预估值把已传输字节换算成百分比，避免任务卡片一直停在 0%。
    /// </summary>
    public long ExpectedBytes { get; set; }

    public void RaiseProgress(CKAN.ByteRateCounter rateCounter)
    {
        LastBytesPerSecond = Math.Max(0L, rateCounter.BytesPerSecond);
        LastBytesLeft = rateCounter.BytesLeft;

        var percent = rateCounter.Percent;
        if (percent <= 0 && ExpectedBytes > 0)
        {
            // Size 未知时 BytesLeft 会变成负数，取反即为已传输字节
            var transferred = Math.Max(0L, -rateCounter.BytesLeft);
            percent = (int)Math.Clamp(transferred * 100d / ExpectedBytes, 0d, 99d);
        }

        try { ProgressReported?.Invoke(rateCounter.Summary, percent); } catch { /* 回调异常不得影响 CKAN */ }
        try { SpeedReported?.Invoke(LastBytesPerSecond); } catch { /* 回调异常不得影响 CKAN */ }
    }

    public void RaiseMessage(string message, params object[] args)
    {
        var text = string.Format(message, args);
        Append($"[Msg] {text}");
        try { MessageReported?.Invoke(text); } catch { /* 回调异常不得影响 CKAN */ }
    }

    private void Append(string line)
    {
        lock (_lock) _sb.AppendLine(line);
    }
}
