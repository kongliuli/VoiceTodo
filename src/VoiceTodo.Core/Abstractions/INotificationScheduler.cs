using VoiceTodo.Core.Models;

namespace VoiceTodo.Core.Abstractions;

/// <summary>
/// 通知 / 定时器调度抽象（A/B 类功能）。
/// 平台实现：Plugin.LocalNotification，支撑 Nag Mode、Snooze、预提醒、自定义铃声。
/// DEV-02：通知 ID 由待办实体 ID 确定性派生（见 <see cref="NotificationId"/>），
/// 与实体持久化绑定，重启不漂移；完成/删除/延后按实体 ID 整体取消或替换。
/// </summary>
public interface INotificationScheduler
{
    /// <summary>
    /// 平台是否具备“未来时刻”的系统定时通知能力。
    /// Android（Plugin.LocalNotification）为 true；Windows（WindowsAppSDK AppNotifications）无未来定时能力，为 false。
    /// <para>
    /// 上层（<see cref="ITodoChangeDispatcher"/>）据此决定是否把待办标记为“提醒已安排”，
    /// 避免在不支持排期的平台上谎报成功（见 page-optimization-review §Windows 虚假安排成功）。
    /// 计时的即时反馈（<see cref="ScheduleAsync"/>）不受此标记影响。
    /// </para>
    /// </summary>
    bool SupportsFutureScheduling { get; }

    /// <summary>
    /// 语音管线旧路径：按调用方给定的通知 ID 排一次性提醒 / 计时结束提醒；
    /// 配置 NagMode 时在同一槽位按周期循环，并受 <see cref="NagPolicy"/> 截止约束。
    /// </summary>
    Task ScheduleAsync(VoiceCommand cmd, int notificationId);

    /// <summary>
    /// 待办提醒排期（DEV-02）：按派生 ID 排主提醒（DueAt）+ 预提醒（DueAt-PreAlert）+ Nag（周期循环，带终止）。
    /// 首触发恒为 DueAt（Snooze 不参与排期，延后走 TodoChangeDispatcher.PostponeAsync）；
    /// 实际排上的槽位回填 item.NotificationIds 供持久化。
    /// </summary>
    Task ScheduleTodoAsync(TodoItem item);

    /// <summary>取消指定待办实体派生的全部关联通知（主/预/Nag 三槽位），幂等。计时实体传入其 ID 仅会取消其主槽位。</summary>
    Task CancelAsync(int todoId);
}

/// <summary>
/// 通知 ID 派生规则（DEV-02）：由待办实体 ID 确定性推导，持久化 <see cref="TodoItem.NotificationIds"/> 后
/// 重启也不会与系统通知错位。主提醒 = todoId*10，预提醒 = todoId*10+1，Nag = todoId*10+2
/// （Nag 重排用同 ID 覆盖，停止 = 取消该 ID）。
/// </summary>
public static class NotificationId
{
    /// <summary>每个待办实体占用的通知 ID 槽位数（保证各槽位互不冲突）。</summary>
    public const int SlotsPerTodo = 10;

    /// <summary>主提醒槽位。</summary>
    public static int Main(int todoId) => todoId * SlotsPerTodo;

    /// <summary>预提醒槽位（主提醒前 X 分钟）。</summary>
    public static int PreAlert(int todoId) => todoId * SlotsPerTodo + 1;

    /// <summary>Nag 催促槽位（重排同 ID 覆盖，停止 = 取消该 ID）。</summary>
    public static int Nag(int todoId) => todoId * SlotsPerTodo + 2;
}

/// <summary>
/// Nag 催促策略（DEV-02）：循环必须带终止条件，防止无限催促。
/// 默认节奏：每 5 分钟一次、最多 10 次，且总催促时长不超过 60 分钟（次数与时长取先到者）。
/// 常量可按产品需要调整。
/// </summary>
public static class NagPolicy
{
    /// <summary>Nag 重复间隔。</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    /// <summary>Nag 最多次数（与间隔相乘即次数维度的时间上限）。</summary>
    public const int MaxCount = 10;

    /// <summary>Nag 总时长上限（与次数维度取先到者）。</summary>
    public static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(60);

    /// <summary>Nag 首次触发时刻：主提醒（DueAt）之后一个间隔，避免与主提醒同刻双响。</summary>
    public static DateTimeOffset FirstFire(DateTimeOffset dueAt) => dueAt + Interval;

    /// <summary>循环截止时刻：到期时间 + min(次数上限×间隔, 总时长上限)。</summary>
    public static DateTimeOffset Deadline(DateTimeOffset dueAt)
    {
        var byCount = dueAt + TimeSpan.FromTicks(Interval.Ticks * MaxCount);
        var byDuration = dueAt + MaxDuration;
        return byCount < byDuration ? byCount : byDuration;
    }
}
