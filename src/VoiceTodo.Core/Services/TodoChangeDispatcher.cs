using VoiceTodo.Core.Abstractions;
using VoiceTodo.Core.Models;

namespace VoiceTodo.Core.Services;

/// <summary>
/// 待办变更统一调度（DEV-02）：仓库变更与提醒排期在同一入口完成，
/// 保证新建/编辑/完成/删除/延后都会同步安排或取消系统通知。
/// 排期规则：DueAt 有效且未来 → 排主提醒，PreAlert 配置且时刻有效 → 加排预提醒，
/// NagMode → 主提醒外按 <see cref="NagPolicy"/> 周期循环 Nag（带次数上限与截止时间）；
/// Past / 无 DueAt → 不排且 ReminderScheduled=false（Message 说明原因）。
/// 调度失败不回滚保存（Saved=true、ReminderScheduled=false、Message=错误摘要）。
/// </summary>
public class TodoChangeDispatcher : ITodoChangeDispatcher
{
    private readonly ITodoRepository _repo;
    private readonly INotificationScheduler _scheduler;

    public TodoChangeDispatcher(ITodoRepository repo, INotificationScheduler scheduler)
    {
        _repo = repo;
        _scheduler = scheduler;
    }

    /// <inheritdoc/>
    public async Task<TodoChangeResult> CreateAsync(TodoItem item)
    {
        await _repo.AddTodoAsync(item); // 落库并分配实体 ID，通知 ID 由其确定性派生
        return await ScheduleAfterSaveAsync(item);
    }

    /// <inheritdoc/>
    public async Task<TodoChangeResult> RescheduleAsync(TodoItem item)
    {
        await _scheduler.CancelAsync(item.Id); // 编辑保存：先取消旧关联再重排，避免残留
        return await ScheduleAfterSaveAsync(item);
    }

    /// <inheritdoc/>
    public async Task CompleteAsync(int id)
    {
        await _repo.SetTodoDoneAsync(id, true);
        await _scheduler.CancelAsync(id); // 完成即取消主/预/Nag 全部关联通知
    }

    /// <inheritdoc/>
    public async Task<TodoChangeResult> SetDoneAsync(int id, bool done)
    {
        await _repo.SetTodoDoneAsync(id, done);
        if (done)
        {
            await _scheduler.CancelAsync(id); // 勾选完成：取消全部关联通知（不再响）
            return new TodoChangeResult(id, Saved: true, ReminderScheduled: false, ReminderMessage: null);
        }

        // 取消勾选：重新读取实体并按当前 DueAt/Reminder 重排（列表/首页/日历勾选均走此路径）
        var item = (await _repo.GetTodosAsync()).FirstOrDefault(t => t.Id == id);
        if (item is null)
            return new TodoChangeResult(id, Saved: true, ReminderScheduled: false, ReminderMessage: "任务不存在");
        await _scheduler.CancelAsync(id); // 幂等：先清旧槽位再重排，避免残留
        return await ScheduleAfterSaveAsync(item);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(int id)
    {
        await _repo.DeleteTodoAsync(id);
        await _scheduler.CancelAsync(id); // 删除即取消全部关联通知
    }

    /// <inheritdoc/>
    public async Task StopNagAsync(int id)
    {
        // 取消该实体派生的全部关联通知（主/预/Nag），按既有 CancelAsync 路径，不新造 ID 规则。
        await _scheduler.CancelAsync(id);
        // 持久化：关闭该任务的 NagMode，避免后续编辑/重排再次唤起催促（通知已取消，故不会立即再响）。
        try
        {
            var item = (await _repo.GetTodosAsync()).FirstOrDefault(t => t.Id == id);
            if (item is not null && item.Reminder.NagMode)
            {
                item.Reminder.NagMode = false;
                await _repo.UpdateTodoAsync(item);
            }
        }
        catch
        {
            // 持久化失败不阻断：通知已取消，下次编辑仍可再停（取消动作本身就是主效应）
        }
    }

    /// <inheritdoc/>
    public async Task<TodoChangeResult> PostponeAsync(TodoItem item, DateTimeOffset newDueAt)
    {
        item.DueAt = newDueAt;                 // 延后 = 替换：改时刻后整体重排（旧提醒已取消）
        await _scheduler.CancelAsync(item.Id);
        return await ScheduleAfterSaveAsync(item);
    }

    /// <summary>
    /// 保存后的排期决策：无 DueAt / Past 不排并说明原因；有效则交调度器按派生 ID 排期；
    /// 结束后统一回写实体（含 NotificationIds 持久化，防重启后通知 ID 错位）。
    /// </summary>
    private async Task<TodoChangeResult> ScheduleAfterSaveAsync(TodoItem item)
    {
        string? message = null;
        var scheduled = false;
        item.NotificationIds = new(); // 先清空，成功后由调度器回填实际排上的槽位
        try
        {
            if (item.DueAt is null)
            {
                message = "未设置提醒时间"; // 无 DueAt：仅保存，不排提醒
            }
            else if (item.DueAt.Value <= DateTimeOffset.Now)
            {
                message = "提醒时间已过去"; // Past：排了也不会响，明确不排并说明
            }
            else if (!_scheduler.SupportsFutureScheduling)
            {
                // 平台无未来定时能力（Windows）：不谎报成功；应用打开时由 MainPage 的应用内到期检查兜底提示。
                message = "当前平台不支持系统定时提醒，到点需打开应用";
            }
            else
            {
                await _scheduler.ScheduleTodoAsync(item);
                scheduled = true;
            }
        }
        catch (Exception ex)
        {
            scheduled = false;
            message = ex.Message; // 调度失败摘要；保存不回滚
        }
        finally
        {
            await _repo.UpdateTodoAsync(item); // 持久化实体（含 NotificationIds 回填/清空）
        }
        return new TodoChangeResult(item.Id, Saved: true, ReminderScheduled: scheduled, ReminderMessage: message);
    }
}
