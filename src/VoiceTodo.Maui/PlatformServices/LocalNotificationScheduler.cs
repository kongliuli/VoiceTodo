using System.Collections.Concurrent;
using Plugin.LocalNotification;
using Plugin.LocalNotification.EventArgs;
using VoiceTodo.Core;
using VoiceTodo.Core.Abstractions;
using VoiceTodo.Core.Models;
using VoiceTodo.Core.Resources;

namespace VoiceTodo.Maui.PlatformServices;

/// <summary>
/// 本地通知 / 定时器调度实现（Plugin.LocalNotification，端侧、离线）。
/// 支撑 A 的 Nag Mode（周期重复直到确认）、Snooze、预提醒、自定义铃声；
/// 以及 B 的定时器触发。通知与定时器共享 ReminderSettings。
/// DEV-02：通知 ID 改由待办实体 ID 确定性派生（NotificationId），删除"自增 _nextId"与实体错位的逻辑；
/// Snooze 不再使首触发后移（首触发恒为 DueAt，延后 = TodoChangeDispatcher.PostponeAsync 取消旧 + 排新）；
/// Nag 循环带终止条件（NagPolicy：次数上限×间隔与总时长上限取先到，超截止即取消该槽位）。
/// </summary>
public class LocalNotificationScheduler : INotificationScheduler
{
    // 动作 Id 约定：为后续在 MauiProgram 注册 NotificationCategory 时预留，标题语义“完成/忽略”。
    private const int CompleteActionId = 1;
    private const int IgnoreActionId = 3;

    /// <summary>Nag 请求 ReturningData 载荷前缀：收到通知时可解析截止时刻并自动终止循环。</summary>
    private const string NagPayloadPrefix = "nag|";

    private readonly ITodoRepository _repo;
    private readonly IQuietHours _quiet;

    // 待办提醒的标题队列：调度提醒时压入，收到“完成”动作时取出回查并标记完成。
    // 受限说明：Plugin.LocalNotification 13.0.0 的 NotificationActionTapped 事件参数
    // 仅暴露 ActionId / IsTapped / IsDismissed，不携带是“哪个待办/通知”的信息，
    // 因此只能按调度顺序做尽力而为的匹配（见 OnNotificationActionTapped 注释）。
    private readonly ConcurrentQueue<string> _pendingTodoTitles = new();

    // 由 DI 注入 ITodoRepository 与 IQuietHours：MauiProgram 中注册，DI 自动解析依赖，不破坏既有结构。
    public LocalNotificationScheduler(ITodoRepository repo, IQuietHours quiet)
    {
        _repo = repo;
        _quiet = quiet;
        WireActionTappedHandler();
        WireReceivedHandler();
    }

    /// <inheritdoc/>
    public bool SupportsFutureScheduling => true; // Plugin.LocalNotification 支持未来时刻排期与 Daily/Weekly 循环

    /// <summary>
    /// 为通知动作点击注册事件处理（13.0.0 支持 NotificationActionTapped）。
    /// 受限说明：该版本的动作按钮只能通过 MauiProgram 里
    /// UseLocalNotification(builder => builder.AddCategory(...)) 全局注册分类来渲染；
    /// NotificationRequest 本身没有可直接附加动作列表的公开 API（无 Actions 属性），
    /// 而 MauiProgram 不属于可改动范围，故按钮可能不显示。
    /// 此处仍把事件挂好（幂等）：一旦后续在 MauiProgram 注册了含 CompleteActionId 的 Category，点击即会生效。
    /// </summary>
    private void WireActionTappedHandler()
    {
        try
        {
            var service = LocalNotificationCenter.Current;
            if (service is null) return;
            // 先移除再添加，避免重复调度时重复订阅。
            service.NotificationActionTapped -= OnNotificationActionTapped;
            service.NotificationActionTapped += OnNotificationActionTapped;
        }
        catch
        {
            // 事件注册失败绝不影响调度本身，静默降级。
        }
    }

    /// <summary>
    /// 订阅通知到达事件（幂等）：Nag 请求携带截止时刻载荷，超过截止立即取消该槽位。
    /// 与 Schedule 上的 NotifyAutoCancelTime 构成双保险，保证 Nag 循环确定终止。
    /// </summary>
    private void WireReceivedHandler()
    {
        try
        {
            var service = LocalNotificationCenter.Current;
            if (service is null) return;
            service.NotificationReceived -= OnNotificationReceived;
            service.NotificationReceived += OnNotificationReceived;
        }
        catch
        {
            // 事件注册失败不影响调度本身，静默降级。
        }
    }

    /// <summary>收到通知时检查 Nag 截止：超过截止时刻则取消，终止重复循环。</summary>
    private void OnNotificationReceived(NotificationEventArgs e)
    {
        try
        {
            var data = e.Request?.ReturningData;
            if (data is null || !data.StartsWith(NagPayloadPrefix, StringComparison.Ordinal)) return;
            if (!long.TryParse(data[NagPayloadPrefix.Length..], out var utcTicks)) return;
            if (DateTimeOffset.Now < new DateTimeOffset(utcTicks, TimeSpan.Zero)) return;
            // 超过截止：取消该 Nag 槽位，停止后续催促。
            LocalNotificationCenter.Current.Cancel(e.Request!.NotificationId);
        }
        catch
        {
            // 终止失败不影响宿主应用。
        }
    }

    /// <summary>
    /// 通知动作点击回调。收到“完成”（CompleteActionId）且非“忽略/消除”（IsDismissed）时，
    /// 按调度顺序回查并标记该待办完成。
    /// </summary>
    private void OnNotificationActionTapped(NotificationActionEventArgs e)
    {
        try
        {
            if (e.ActionId != CompleteActionId || e.IsDismissed) return;
            if (!_pendingTodoTitles.TryDequeue(out var title) || string.IsNullOrWhiteSpace(title)) return;

            // 事件可能不回调到 Main(Maui) 线程；此处仅做仓库 I/O、无 UI 刷新，安全。
            _ = Task.Run(async () =>
            {
                try
                {
                    var todos = await _repo.GetTodosAsync();
                    var match = todos.FirstOrDefault(t => !t.IsDone &&
                        t.Title.Contains(title, StringComparison.OrdinalIgnoreCase));
                    if (match is not null)
                        await _repo.MarkDoneAsync(match.Id);
                }
                catch { /* 单个通知动作处理失败不影响宿主。 */ }
            });
        }
        catch
        {
            // 通知动作处理异常不影响宿主应用。
        }
    }

    /// <summary>
    /// 语音管线旧路径：一次性提醒 / 计时结束提醒。
    /// Snooze 不再顺延首触发（延后语义由延后动作承担）；NagMode 时在同一槽位循环并受 NagPolicy 截止约束。
    /// </summary>
    public async Task ScheduleAsync(VoiceCommand cmd, int notificationId)
    {
        var title = cmd.Type == CommandType.Todo
            ? CoreStrings.Reminder
            : CoreStrings.TimerDone;
        var description = cmd.Title ?? "";

        var notifyTime = cmd.TriggerAt
            ?? (cmd.Type == CommandType.Timer
                ? DateTimeOffset.Now.Add(cmd.Duration ?? TimeSpan.Zero)
                : DateTimeOffset.Now);

        try
        {
            await ShowRequestAsync(
                notificationId,
                title,
                description,
                notifyTime,
                cmd.Reminder.CustomSoundPath,
                cmd.Reminder.NagMode ? NagPolicy.Deadline(notifyTime) : null);

            // 待办提醒登记到“完成”匹配队列（仅记录标题，用于动作回调回查）。
            if (cmd.Type == CommandType.Todo && !string.IsNullOrWhiteSpace(cmd.Title))
                _pendingTodoTitles.Enqueue(cmd.Title);
        }
        catch
        {
            // 通知调度失败绝不拖垮应用（与既有约定一致）。
        }
    }

    /// <summary>
    /// 待办提醒排期（DEV-02）：主提醒（DueAt）+ 预提醒（DueAt-PreAlert，仍需在未来）+ Nag（DueAt 后按周期，带截止）。
    /// 首触发恒为 DueAt（Snooze 不参与排期）；实际排上的槽位回填 item.NotificationIds 供持久化。
    /// 重复任务（IsRecurring + RecurrenceRule）：主提醒槽位按 Daily/Weekly 循环（见 <see cref="RecurrenceOf"/>）。
    /// 主提醒失败向上抛（由 TodoChangeDispatcher 转 ReminderScheduled=false）；预提醒/Nag 尽力而为不拖垮主提醒。
    /// </summary>
    public async Task ScheduleTodoAsync(TodoItem item)
    {
        var due = item.DueAt!.Value; // TodoChangeDispatcher 已保证有效且未来
        var sound = item.Reminder.CustomSoundPath;
        var ids = new List<int>();

        // C7 静默时段顺延：主提醒落到静默窗口内 → 顺延到窗口结束（不丢弃）
        var mainTime = QuietHoursPolicy.Defer(due, _quiet);

        // 1) 主提醒：ID = todoId*10，时刻 = 顺延后 DueAt；重复任务按 RecurrenceRule 循环
        var mainId = NotificationId.Main(item.Id);
        await ShowRequestAsync(mainId, CoreStrings.Reminder, item.Title, mainTime, sound, null,
            repeat: RecurrenceOf(item));
        ids.Add(mainId);

        // 待办提醒登记到“完成”匹配队列（每个待办登记一次，供动作回调回查）。
        if (!string.IsNullOrWhiteSpace(item.Title))
            _pendingTodoTitles.Enqueue(item.Title);

        // 2) 预提醒：ID = todoId*10+1，时刻 = DueAt - PreAlert，同样经静默顺延；仍未来才排
        if (item.Reminder.PreAlert is { } pre)
        {
            var preTime = QuietHoursPolicy.Defer(due - pre, _quiet);
            if (preTime > DateTimeOffset.Now)
            {
                try
                {
                    await ShowRequestAsync(NotificationId.PreAlert(item.Id), CoreStrings.Reminder, item.Title,
                        preTime, sound, null);
                    ids.Add(NotificationId.PreAlert(item.Id));
                }
                catch
                {
                    // 预提醒失败不影响主提醒。
                }
            }
        }

        // 3) Nag：ID = todoId*10+2，主提醒顺延后按 NagPolicy.Interval 循环；
        //    截止 = min(次数上限×间隔, 总时长上限)；重排同 ID 覆盖，停止 = 取消该 ID。
        if (item.Reminder.NagMode)
        {
            try
            {
                var firstFire = QuietHoursPolicy.Defer(NagPolicy.FirstFire(due), _quiet); // 与主提醒一致顺延
                var deadline = NagPolicy.Deadline(mainTime); // 截止随主提醒顺延后时刻
                await ShowRequestAsync(NotificationId.Nag(item.Id), CoreStrings.Reminder, item.Title,
                    firstFire, sound, deadline, NagPolicy.Interval);
                ids.Add(NotificationId.Nag(item.Id));
            }
            catch
            {
                // Nag 失败不影响主提醒。
            }
        }

        item.NotificationIds = ids; // 回填实际槽位，由 TodoChangeDispatcher 统一持久化
    }

    /// <summary>取消该待办实体派生的全部通知槽位（主/预/Nag），幂等；取消失败静默降级。</summary>
    public async Task CancelAsync(int todoId)
    {
        foreach (var id in new[]
                 {
                     NotificationId.Main(todoId),
                     NotificationId.PreAlert(todoId),
                     NotificationId.Nag(todoId)
                 })
        {
            try
            {
                LocalNotificationCenter.Current.Cancel(id);
            }
            catch
            {
                // 取消失败静默降级。
            }
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// 排一条请求（一次性 / 重复 / Nag 循环）。
    /// - deadline 非空（Nag）：RepeatType=TimeInterval + NotifyAutoCancelTime 到截止自动取消，
    ///   ReturningData 携带截止时刻（UtcTicks），进程存活时收到通知后由 OnNotificationReceived 兜底终止；
    /// - repeat=Daily/Weekly（重复任务主提醒）：按天/周循环；
    /// - 其余：一次性（NotificationRepeat.No）。
    /// </summary>
    private async Task ShowRequestAsync(int id, string title, string description, DateTimeOffset notifyTime,
        string? soundPath, DateTimeOffset? deadline, TimeSpan? repeatInterval = null,
        NotificationRepeat repeat = NotificationRepeat.No)
    {
        var schedule = new NotificationRequestSchedule
        {
            NotifyTime = notifyTime.LocalDateTime,
        };

        if (deadline is { } d && repeatInterval is { } interval)
        {
            // A 类：Nag Mode 周期重复，带终止条件（截止时刻自动取消，防无限催促）。
            schedule.RepeatType = NotificationRepeat.TimeInterval;
            schedule.NotifyRepeatInterval = interval;
            schedule.NotifyAutoCancelTime = d.LocalDateTime;
        }
        else if (repeat is not NotificationRepeat.No)
        {
            // 重复任务（每天/每周）：主提醒槽位按周期循环，由系统在每周期重投同一通知 ID。
            schedule.RepeatType = repeat;
        }

        var request = new NotificationRequest
        {
            NotificationId = id,
            Title = title,
            Description = description,
            Schedule = schedule,
            ReturningData = deadline is { } dl ? NagPayloadPrefix + dl.UtcTicks : null,
        };

        if (!string.IsNullOrEmpty(soundPath))
            request.Sound = soundPath;

        await LocalNotificationCenter.Current.Show(request);
    }

    /// <summary>
    /// 重复规则 → 系统重复类型（DEV：重复任务真正循环）。
    /// 仅支持插件可表达的 Daily/Weekly：每天/daily → Daily；每周/weekly/每星期X → Weekly；
    /// 每工作日（周一~周五）插件无法只排工作日，退化为 Daily（近似，见 README/文档说明）。
    /// 未识别规则不循环（退化为一次性），避免排错周期。
    /// </summary>
    private static NotificationRepeat RecurrenceOf(TodoItem item)
    {
        if (!item.IsRecurring || string.IsNullOrWhiteSpace(item.RecurrenceRule))
            return NotificationRepeat.No;
        var rule = item.RecurrenceRule.Trim().ToLowerInvariant();
        if (rule.Contains("weekday")) return NotificationRepeat.Daily;   // 工作日近似为每天
        if (rule.Contains("day")) return NotificationRepeat.Daily;       // every day / daily / 每天
        if (rule.Contains("week")) return NotificationRepeat.Weekly;     // every week / weekly / 每周
        if (rule.Contains("monday") || rule.Contains("tuesday") || rule.Contains("wednesday")
            || rule.Contains("thursday") || rule.Contains("friday") || rule.Contains("saturday")
            || rule.Contains("sunday"))
            return NotificationRepeat.Weekly;                            // every monday 等
        return NotificationRepeat.No;
    }
}
