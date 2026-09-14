#if ANDROID
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Microsoft.Extensions.DependencyInjection;
#endif

namespace VoiceTodo.Maui.PlatformServices;

/// <summary>
/// 计时运行保活控制抽象（DEV-05）：训练运行期间保进程 + 常驻通知（剩余时间 + 暂停/停止动作）。
/// 普通计时只保活计时，不占用麦克风（麦克风 FGS 属 DEV-06 常驻会话）。
/// 页面不可见时计时本身靠 EndAt 绝对时刻继续（现有机制），FGS 只负责降低进程被杀概率；
/// 进程仍被杀的场景由 running-timer.json 快照恢复兜底。Windows 空实现（窗口在即活）。
/// </summary>
public interface ITimerForegroundController
{
    /// <summary>训练开跑时启动保活（含首条通知）。</summary>
    void Start(string title);

    /// <summary>每秒刷新通知内容（剩余时间/暂停态）。未启动成功时静默忽略。</summary>
    void Update(string title, string remaining, bool paused);

    /// <summary>训练结束/取消时停止服务并清通知。</summary>
    void Stop();

    /// <summary>通知「暂停/继续」按钮回调（订阅方需自行切回 UI 线程）。Windows 端永不触发。</summary>
    event Action? PauseToggled;

    /// <summary>通知「停止」按钮回调。Windows 端永不触发。</summary>
    event Action? StopRequested;
}

#if ANDROID

/// <summary>
/// 计时前台服务（FGS）。选型按 review §6 平台结论：specialUse —— 纯倒计时不满足
/// media/dataSync 等类型语义，也不得为此声明 microphone 类型；targetSdk=36 要求
/// FOREGROUND_SERVICE_SPECIAL_USE 权限 + PROPERTY_SPECIAL_USE_FGS_SUBTYPE 声明
/// （后者由下方 [Property] 特性生成，无需手写 Manifest service 条目——.NET 构建自动合并）。
/// </summary>
[Service(Enabled = true, Exported = false, ForegroundServiceType = ForegroundService.TypeSpecialUse)]
[Property("android.app.PROPERTY_SPECIAL_USE_FGS_SUBTYPE", Value = "Timer training keep-alive with remaining time notification")]
public sealed class TimerForegroundService : Service
{
    internal const int NotificationId = 4701;
    internal const string ChannelId = "voicetodo_timer";
    internal const string ActionPause = "voicetodo.intent.TIMER_PAUSE";
    internal const string ActionStop = "voicetodo.intent.TIMER_STOP";

    // 通知当前内容（静态缓存：控制器 Update 与服务重建通知共用同一份状态）
    internal static string Title = "";
    internal static string Text = "";
    internal static bool Paused;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        // 每次 StartForegroundService 后必须及时 StartForeground；重复调用无害
        TryStartForeground();

        switch (intent?.Action)
        {
            case ActionPause:
                ResolveController()?.RaisePause();
                break;
            case ActionStop:
                ResolveController()?.RaiseStop();
                // 停止动作兜底自停 + 清通知（EndRun 侧还会走 controller.Stop，重复无害）
                try
                {
                    StopSelf();
                    (GetSystemService(Context.NotificationService) as NotificationManager)
                        ?.Cancel(NotificationId);
                }
                catch { /* 忽略 */ }
                return StartCommandResult.NotSticky;
        }
        return StartCommandResult.Sticky;
    }

    private void TryStartForeground()
    {
        try
        {
            var notification = TimerForegroundController.BuildNotification(
                this, Title, Text, Paused);
            if (Android.OS.Build.VERSION.SdkInt >= BuildVersionCodes.Q)
                StartForeground(NotificationId, notification, ForegroundService.TypeSpecialUse);
            else
                StartForeground(NotificationId, notification);
        }
        catch
        {
            // 通知构建失败静默降级：计时继续靠 EndAt + 快照恢复
        }
    }

    private static TimerForegroundController? ResolveController()
    {
        try
        {
            return Microsoft.Maui.IPlatformApplication.Current?.Services
                .GetService<ITimerForegroundController>() as TimerForegroundController;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>Android 前台服务控制器：启动/刷新/停止 FGS，桥接通知按钮事件。</summary>
public sealed class TimerForegroundController : ITimerForegroundController
{
    private static readonly PendingIntentFlags PiFlags =
        // API 31+ 强制 Immutable
        (Android.OS.Build.VERSION.SdkInt >= BuildVersionCodes.S ? PendingIntentFlags.Immutable : PendingIntentFlags.UpdateCurrent)
        | PendingIntentFlags.UpdateCurrent;

    private readonly Context _ctx = Android.App.Application.Context;
    private readonly Intent _serviceIntent;
    private bool _active;

    public event Action? PauseToggled;
    public event Action? StopRequested;

    public TimerForegroundController()
    {
        _serviceIntent = new Intent(_ctx, typeof(TimerForegroundService));
    }

    internal void RaisePause()
    {
        try { PauseToggled?.Invoke(); } catch { /* 订阅方异常不拖垮服务 */ }
    }

    internal void RaiseStop()
    {
        try { StopRequested?.Invoke(); } catch { /* 订阅方异常不拖垮服务 */ }
    }

    public void Start(string title)
    {
        TimerForegroundService.Title = title;
        TimerForegroundService.Text = "";
        TimerForegroundService.Paused = false;
        try
        {
            _active = true;
            if (Android.OS.Build.VERSION.SdkInt >= BuildVersionCodes.O)
                _ctx.StartForegroundService(_serviceIntent);
            else
                _ctx.StartService(_serviceIntent);
        }
        catch
        {
            // 后台启动限制等异常：降级为无保活（计时靠 EndAt + 快照恢复兜底），如实不报成功
            _active = false;
        }
    }

    public void Update(string title, string remaining, bool paused)
    {
        if (!_active) return;
        TimerForegroundService.Title = title;
        TimerForegroundService.Text = remaining;
        TimerForegroundService.Paused = paused;
        try
        {
            var nm = _ctx.GetSystemService(Context.NotificationService) as NotificationManager;
            nm?.Notify(TimerForegroundService.NotificationId,
                BuildNotification(_ctx, title, remaining, paused));
        }
        catch { /* 刷新失败忽略，下秒重试 */ }
    }

    public void Stop()
    {
        if (!_active) return;
        _active = false;
        try
        {
            (_ctx.GetSystemService(Context.NotificationService) as NotificationManager)
                ?.Cancel(TimerForegroundService.NotificationId);
            _ctx.StopService(new Intent(_ctx, typeof(TimerForegroundService)));
        }
        catch { /* 忽略 */ }
    }

    internal static Notification BuildNotification(Context ctx, string title, string text, bool paused)
    {
        if (Android.OS.Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            var nm = ctx.GetSystemService(Context.NotificationService) as NotificationManager;
            if (nm?.GetNotificationChannel(TimerForegroundService.ChannelId) is null)
            {
                var channel = new NotificationChannel(
                    TimerForegroundService.ChannelId, "VoiceTodo timer", NotificationImportance.Low);
                channel.SetShowBadge(false);
                nm?.CreateNotificationChannel(channel);
            }
        }

        // 点通知回到应用
        var launchIntent = ctx.PackageManager?.GetLaunchIntentForPackage(ctx.PackageName!) ?? new Intent();
        var contentPi = PendingIntent.GetActivity(ctx, 0, launchIntent, PiFlags);
        var pausePi = PendingIntent.GetService(ctx, 1,
            new Intent(ctx, typeof(TimerForegroundService)).SetAction(TimerForegroundService.ActionPause),
            PiFlags);
        var stopPi = PendingIntent.GetService(ctx, 2,
            new Intent(ctx, typeof(TimerForegroundService)).SetAction(TimerForegroundService.ActionStop),
            PiFlags);

        var builder = Android.OS.Build.VERSION.SdkInt >= BuildVersionCodes.O
            ? new Notification.Builder(ctx, TimerForegroundService.ChannelId)
            : new Notification.Builder(ctx);
        builder.SetContentTitle(title)
            .SetContentText(text)
            .SetSmallIcon(ctx.ApplicationInfo.Icon)
            .SetOngoing(true)
            .SetOnlyAlertOnce(true)
            .SetContentIntent(contentPi)
            .AddAction(0, paused ? Resources.AppResources.Resume : Resources.AppResources.Pause, pausePi)
            .AddAction(0, Resources.AppResources.StopTimer, stopPi);
        return builder.Build();
    }
}

#else

/// <summary>Windows 端空实现：窗口在即计时可见，进程保活由快照恢复兜底。</summary>
public sealed class NoopTimerForegroundController : ITimerForegroundController
{
    public event Action? PauseToggled { add { } remove { } }
    public event Action? StopRequested { add { } remove { } }
    public void Start(string title) { }
    public void Update(string title, string remaining, bool paused) { }
    public void Stop() { }
}

#endif
