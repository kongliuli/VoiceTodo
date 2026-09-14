using VoiceTodo.Core.Models;

namespace VoiceTodo.Core.Abstractions;

/// <summary>
/// 待办变更统一入口（DEV-02）：所有落库变更与提醒调度在同一处闭环，
/// 页面（新建/编辑/完成/删除/延后）不得绕开本入口直接调仓库，
/// 否则提醒不会被安排或取消失效（见 page-optimization-review §P0-A）。
/// </summary>
public interface ITodoChangeDispatcher
{
    /// <summary>新建待办：落库 + 按 item.Reminder 排期。</summary>
    Task<TodoChangeResult> CreateAsync(TodoItem item);

    /// <summary>编辑保存：取消旧关联 + 重排。</summary>
    Task<TodoChangeResult> RescheduleAsync(TodoItem item);

    /// <summary>标记完成并取消全部关联通知。</summary>
    Task CompleteAsync(int id);

    /// <summary>
    /// 设置完成状态（支持列表勾选/取消勾选，通知同步）：
    /// done=true → 落库完成 + 取消全部关联通知；
    /// done=false → 落库未完成 + 按当前 DueAt/Reminder 重新排期。
    /// </summary>
    Task<TodoChangeResult> SetDoneAsync(int id, bool done);

    /// <summary>删除并取消全部关联通知。</summary>
    Task DeleteAsync(int id);

    /// <summary>延后 = 替换：改 DueAt 为新时刻，取消旧提醒并按新时刻重排一次。</summary>
    Task<TodoChangeResult> PostponeAsync(TodoItem item, DateTimeOffset newDueAt);

    /// <summary>
    /// 停止该任务的催促（A4）：取消实体派生的全部关联通知（主/预/Nag，按既有 CancelAsync 路径，不新造 ID 规则），
    /// 并关闭该任务的 NagMode 持久化，避免后续编辑/重排再次唤起催促。调用方据界面反馈让用户感知已停止。
    /// </summary>
    Task StopNagAsync(int id);
}

/// <summary>待办变更结果：Saved=落库成功；ReminderScheduled=提醒是否已安排；ReminderMessage=未安排原因或调度失败摘要。</summary>
public record TodoChangeResult(int TodoId, bool Saved, bool ReminderScheduled, string? ReminderMessage);
