using VoiceTodo.Core.Models;

namespace VoiceTodo.Core.Abstractions;

/// <summary>本地存储抽象（D 类功能：离线持久化）。</summary>
public interface ITodoRepository
{
    Task<int> AddTodoAsync(TodoItem item);
    Task<int> AddTimerAsync(TimerItem item);
    /// <summary>按 Id 整体更新待办（编辑器保存）。</summary>
    Task UpdateTodoAsync(TodoItem item);
    /// <summary>按 Id 整体更新计时（编辑器保存）。</summary>
    Task UpdateTimerAsync(TimerItem item);
    Task<List<TodoItem>> GetTodosAsync();
    Task<List<TimerItem>> GetTimersAsync();
    Task MarkDoneAsync(int id);
    /// <summary>设置完成状态（支持取消勾选）。</summary>
    Task SetTodoDoneAsync(int id, bool done);
    Task DeleteTodoAsync(int id);
    Task DeleteTimerAsync(int id);
    Task<int> AddSessionAsync(TimerSession session);
    Task<List<TimerSession>> GetSessionsAsync();
    Task UpdateSessionAsync(TimerSession session);
}
