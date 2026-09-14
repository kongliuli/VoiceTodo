using VoiceTodo.Core.Abstractions;
using VoiceTodo.Core.Models;
using VoiceTodo.Core.Resources;

namespace VoiceTodo.Maui.PlatformServices;

/// <summary>
/// Windows 系统 toast 通知实现。
/// Plugin.LocalNotification 不支持 Windows，此处改走 WindowsAppSDK 的 AppNotifications。
/// unpackaged 场景首次调用会自动注册，失败时静默降级，绝不让通知的初始化拖垮应用。
/// DEV-02：已排 toast 记录 Tag = 实体派生通知 ID（NotificationId），CancelAsync 按 Tag
/// 从系统通知中心历史真实移除（替换原空实现）；删除"自增 _nextId"与实体错位的逻辑。
/// 受限说明：当前 WindowsAppSDK 框架不支持未来定时 toast（AppNotificationManager 无 schedule 能力），
/// 故 <see cref="SupportsFutureScheduling"/> = false；上层据此不再谎报「提醒已安排」，
/// 到点由 MainPage 的应用内到期检查兜底提示。语音管线旧路径（计时结束/即时提醒）保持既有"即时 toast"反馈。
/// </summary>
public sealed class WindowsNotificationScheduler : INotificationScheduler
{
    /// <inheritdoc/>
    public bool SupportsFutureScheduling => false; // WindowsAppSDK 无未来定时 toast 能力

    public Task ScheduleAsync(VoiceCommand cmd, int notificationId)
    {
        ShowTagged(notificationId,
            cmd.Type == CommandType.Todo ? CoreStrings.Reminder : CoreStrings.TimerDone,
            cmd.Title ?? "");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 待办提醒排期（DEV-02）：Windows 框架无未来定时能力，本方法不产生任何未来通知，
    /// 因此不登记槽位（<see cref="TodoItem.NotificationIds"/> 置空），由 <see cref="SupportsFutureScheduling"/> = false
    /// 让上层如实反馈「未安排」。到点由应用内到期检查提示。
    /// </summary>
    public Task ScheduleTodoAsync(TodoItem item)
    {
        item.NotificationIds = new(); // 无系统排期 → 无槽位可取消，保持诚实不谎报
        return Task.CompletedTask;
    }

    /// <summary>真实取消：按 Tag（=派生通知 ID）从系统通知中心历史移除；失败静默降级。</summary>
    public async Task CancelAsync(int todoId)
    {
#if WINDOWS
        foreach (var id in new[]
                 {
                     NotificationId.Main(todoId),
                     NotificationId.PreAlert(todoId),
                     NotificationId.Nag(todoId)
                 })
        {
            try
            {
                await Microsoft.Windows.AppNotifications.AppNotificationManager.Default
                    .RemoveByTagAsync(id.ToString());
            }
            catch
            {
                // 移除失败时静默降级，不影响应用其余功能。
            }
        }
#else
        await Task.CompletedTask;
#endif
    }

    /// <summary>即时弹出一条 toast，Tag 记录派生通知 ID，供 CancelAsync 按其移除。</summary>
    private static void ShowTagged(int notificationId, string title, string description)
    {
#if WINDOWS
        try
        {
            var manager = Microsoft.Windows.AppNotifications.AppNotificationManager.Default;
            // 受限说明：Windows 系统 toast 仅展示标题+正文，动作按钮需走
            // AppNotificationButton + 启动参数并注册 AppNotificationManager.NotificationInvoked，
            // 超出当前保持“不崩”的打磨范围，此处刻意不做；失败静默降级。
            var notification = new Microsoft.Windows.AppNotifications.Builder.AppNotificationBuilder()
                .AddText(title)
                .AddText(description)
                .BuildNotification();
            notification.Tag = notificationId.ToString(); // Tag = 派生 ID
            manager.Show(notification);
        }
        catch
        {
            // 系统 toast 失败时静默降级，不影响应用其余功能。
        }
#else
        _ = notificationId;
        _ = title;
        _ = description;
#endif
    }
}
