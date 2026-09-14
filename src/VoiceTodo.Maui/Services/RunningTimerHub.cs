using VoiceTodo.Core.Models;

namespace VoiceTodo.Maui.Services;

/// <summary>运行中计时的一条快照（跨页展示用，如首页「正在计时」卡）。</summary>
public sealed class RunningTimerInfo
{
    public required string Title { get; set; }
    /// <summary>当前阶段：work / rest / countdown（倒计时）。</summary>
    public string Phase { get; set; } = "countdown";
    public DateTimeOffset EndAt { get; set; }
    public bool Paused { get; set; }
    /// <summary>暂停时冻结的剩余时长。</summary>
    public TimeSpan RemainingWhenPaused { get; set; }
    /// <summary>总时长（展示「共 X」用）。</summary>
    public TimeSpan Total { get; set; }
    public int Round { get; set; }
    public int Rounds { get; set; }

    // ── DEV-05 扩展：运行页 09 稿要点与首页顶部卡 ──
    /// <summary>动作名（训练计划的 ActionName；普通倒计时为空串，回退用 Title）。</summary>
    public string ActionName { get; set; } = "";
    /// <summary>全程剩余 = 当前段剩余 + 后续各段时长和（预计算，发布前更新；暂停时冻结）。</summary>
    public TimeSpan TotalRemaining { get; set; }

    public TimeSpan Remaining => Paused
        ? RemainingWhenPaused
        : EndAt - DateTimeOffset.Now;

    /// <summary>已过进度 0..1（按剩余计算，暂停时冻结）。</summary>
    public double ElapsedFraction
    {
        get
        {
            if (Total <= TimeSpan.Zero) return 0;
            var f = 1 - Remaining / Total;
            return Math.Clamp(f, 0, 1);
        }
    }
}

/// <summary>
/// 运行中计时共享中枢：TimerPage 运行时发布快照，首页等其它页面订阅展示。
/// MVP 只承载单个活动计时（设计稿多计时器的「其余 N 项入口」后续扩展）。
/// </summary>
public static class RunningTimerHub
{
    private static RunningTimerInfo? _current;

    /// <summary>快照变化（开始/暂停/继续/换阶段/结束）后触发；订阅方需自行切回 UI 线程。</summary>
    public static event Action? Changed;

    public static RunningTimerInfo? Current => _current;

    public static void Publish(RunningTimerInfo? info)
    {
        _current = info;
        Changed?.Invoke();
    }
}
