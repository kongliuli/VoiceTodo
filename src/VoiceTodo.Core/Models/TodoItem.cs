namespace VoiceTodo.Core.Models;

/// <summary>本地持久化的待办事项。</summary>
public class TodoItem
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public bool IsDone { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public bool IsRecurring { get; set; }
    public string? RecurrenceRule { get; set; }
    public ReminderSettings Reminder { get; set; } = new();
    /// <summary>备注（编辑器可编辑，可空）。</summary>
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    /// <summary>系统通知关联 ID（持久化，防重启错位）。</summary>
    public List<int> NotificationIds { get; set; } = new();
}
