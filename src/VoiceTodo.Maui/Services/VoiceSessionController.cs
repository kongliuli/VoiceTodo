namespace VoiceTodo.Maui.Services;

/// <summary>
/// 常驻语音会话控制器（DEV-06 的轻量落地）。
/// <para>
/// 会话层与页面解耦：<see cref="SessionState.NotStarted"/> / <see cref="SessionState.Active"/>。
/// 会话一旦开启即跨页面维持——授权结果缓存、在飞录音句柄、已录秒数都挂在这里，
/// 因此离开并重新进入录音页不会丢掉当前这一句，也不会重复弹权限申请。
/// </para>
/// <para>
/// 控制分层：「完成本句 / 取消本句」只作用于当前一句；「结束会话」才是生命周期终点，
/// 会取消在飞录音并清空会话状态（见 page-optimization-review §常驻语音会话尚未完成）。
/// </para>
/// </summary>
public static class VoiceSessionController
{
    public enum SessionState
    {
        /// <summary>未开启。</summary>
        NotStarted,

        /// <summary>已开启（常驻，跨页面维持）。</summary>
        Active
    }

    public static SessionState State { get; private set; } = SessionState.NotStarted;

    public static bool IsActive => State == SessionState.Active;

    /// <summary>会话内是否已取得麦克风授权（避免每次回到页面重复申请）。</summary>
    public static bool PermissionGranted { get; set; }

    /// <summary>在飞录音句柄（页面重建后可接管继续收口）。</summary>
    public static Task<string>? PendingWav { get; set; }

    /// <summary>在飞录音的取消源（结束会话时取消）。</summary>
    public static CancellationTokenSource? CaptureCts { get; set; }

    /// <summary>在飞录音已录秒数（页面重建后恢复计时显示）。</summary>
    public static int ElapsedSeconds { get; set; }

    /// <summary>会话层级变化通知（用于全局状态条等）。</summary>
    public static event Action? Changed;

    /// <summary>开启会话（幂等）。首次进入录音流时自动调用，亦可由入口显式开启。</summary>
    public static void Start()
    {
        if (State == SessionState.Active) return;
        State = SessionState.Active;
        Changed?.Invoke();
    }

    /// <summary>登记当前这一句的录音句柄（进入采集时调用）。</summary>
    public static void TrackCapture(Task<string> wav, CancellationTokenSource cts)
    {
        PendingWav = wav;
        CaptureCts = cts;
        ElapsedSeconds = 0;
    }

    /// <summary>当前句已收口（识别/丢弃），清空句柄但保持会话开启。</summary>
    public static void ClearCapture()
    {
        PendingWav = null;
        CaptureCts = null;
        ElapsedSeconds = 0;
    }

    /// <summary>结束会话：取消在飞录音、清空句柄与授权缓存，回到未开启。</summary>
    public static void End()
    {
        try { CaptureCts?.Cancel(); } catch { /* 取消失败忽略 */ }
        CaptureCts = null;
        PendingWav = null;
        ElapsedSeconds = 0;
        PermissionGranted = false;
        State = SessionState.NotStarted;
        Changed?.Invoke();
    }
}
