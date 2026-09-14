namespace VoiceTodo.Core.Models;

/// <summary>本地持久化的临时定时器。</summary>
public class TimerItem
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public TimeSpan Duration { get; set; }
    public DateTimeOffset TriggerAt { get; set; }
    public List<IntervalPhase>? Phases { get; set; }

    /// <summary>训练计划快照（模板套用/历史复用/最近使用按此重建执行段，避免退化为普通倒计时）。</summary>
    public TrainingPlan? Plan { get; set; }
    public ReminderSettings Reminder { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}
