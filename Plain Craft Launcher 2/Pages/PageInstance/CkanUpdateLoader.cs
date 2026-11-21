using System.Threading;
using PCL.Core.App.Localization;

namespace PCL;

/// <summary>
/// CKAN 安装 / 更新流程中的单个模组步骤，作为任务管理页卡片里的一行显示
/// （页面对 LoaderCombo 以外的加载器按此列表逐行渲染，沿用原版视觉）。
/// </summary>
public sealed class CkanUpdateStep : Loading.LoaderBase
{
    public CkanUpdateStep(string moduleName)
    {
        name = moduleName;
        State = LoadState.Waiting;
    }

    public void Begin()
    {
        Progress = 0d;
        State = LoadState.Loading;
    }

    public void Report(int percent) => Progress = Math.Clamp(percent, 0, 100) / 100d;


    public void Finish()
    {
        Progress = 1d;
        State = LoadState.Finished;
    }

    public void Fail(Exception ex)
    {
        Error = ex;
        State = LoadState.Failed;
    }

    public override void Start(object? input = null, bool isForceRestart = false) => Begin();

    public override void Abort() => State = LoadState.Aborted;
}

/// <summary>
/// CKAN 批量更新 / 单模组安装在右下角「下载」按钮（任务管理页）里的任务卡片。
/// CKAN 的下载走自己的 NetModuleCache，不经过 PCL 的 DownloadManager，
/// 因此这里用一个独立加载器把 CKAN 的进度报告映射成任务卡片，与其它下载任务并列显示。
/// </summary>
public sealed class CkanUpdateLoader : Loading.LoaderBase
{
    private readonly List<CkanUpdateStep> _steps;
    private int _current;
    private int _finished;
    private bool _cancelled;
    private readonly CancellationTokenSource _cts = new();

    public CkanUpdateLoader(string title, IEnumerable<string> modules)
    {
        name = title;
        _steps = modules.Select(m => new CkanUpdateStep(m)).ToList();
        State = LoadState.Waiting;
    }

    public IReadOnlyList<CkanUpdateStep> Steps => _steps;

    public bool IsCancelled => _cancelled;

    /// <summary>取消令牌：用户点卡片上的 × 时中止 CKAN 的下载与安装。</summary>
    public CancellationToken Token => _cts.Token;

    public CkanUpdateStep? Current => _current >= 0 && _current < _steps.Count ? _steps[_current] : null;

    /// <summary>CKAN 报告的最新下载速率（字节/秒），供任务管理页左边栏统计。</summary>
    public long Speed { get; private set; }

    /// <summary>尚未完成的步骤数，供任务管理页左边栏的「剩余文件」统计。</summary>
    public int RemainingFiles => Math.Max(0, _steps.Count - _finished);

    public void StartTask()
    {
        _current = 0;
        if (_steps.Count == 0)
        {
            Finish();
            return;
        }

        _steps[0].Begin();
        State = LoadState.Loading;
        RefreshProgress();
    }

    /// <summary>当前模组完成，进入下一个；全部完成后加载器结束。</summary>
    public void Advance()
    {
        if (_cancelled)
        {
            return;
        }

        _finished++;
        _current++;
        if (_current >= _steps.Count)
        {
            Finish();
            return;
        }

        _steps[_current].Begin();
        RefreshProgress();
    }

    /// <summary>CKAN 报告的当前模组下载/安装百分比（0-100）。</summary>
    public void Report(int percent)
    {
        if (_cancelled)
        {
            return;
        }

        Current?.Report(percent);
        RefreshProgress();
    }

    /// <summary>更新 CKAN 报告的下载速率（字节/秒）。</summary>
    public void ReportSpeed(long bytesPerSecond) => Speed = Math.Max(0, bytesPerSecond);

    public void Finish()
    {
        foreach (var step in _steps)
        {
            if (step.State is not (LoadState.Finished or LoadState.Failed or LoadState.Aborted))
            {
                step.Finish();
            }
        }

        _finished = _steps.Count;
        Progress = 1d;
        State = LoadState.Finished;
    }

    public void Fail(Exception ex)
    {
        Error = ex;
        State = LoadState.Failed;
    }

    public override void Start(object? input = null, bool isForceRestart = false) => StartTask();

    public override void Abort()
    {
        _cancelled = true;
        try
        {
            _cts.Cancel();
        }
        catch (System.Exception innerEx)
        {
            PCL.Core.Logging.Swallowed.Report(innerEx);
        }
        foreach (var step in _steps)
        {
            if (step.State is LoadState.Waiting or LoadState.Loading)
            {
                step.Abort();
            }
        }

        State = LoadState.Aborted;
    }

    private void RefreshProgress()
    {
        if (_steps.Count == 0)
        {
            return;
        }

        var sum = (double)_finished;
        if (Current is { State: LoadState.Loading } current)
        {
            sum += current.Progress;
        }

        Progress = Math.Clamp(sum / _steps.Count, 0d, 1d);

        // 名称跟随进度刷新，任务卡片标题即当前模组与总进度
        try
        {
            name = Current is null
                ? Lang.Text("Speed.Progress.Total")
                : $"{Current.name}（{_finished + 1}/{_steps.Count}）";
        }
            catch (System.Exception innerEx)
            {
                PCL.Core.Logging.Swallowed.Report(innerEx);
            }
    }
}
