namespace VoiceTodo.Core.Models;

/// <summary>
/// 语音解析后的统一意图实体。由 IIntentParser + ITimeParser 协同产出。
/// </summary>
public class VoiceCommand
{
    /// <summary>意图类型：待办 或 定时器。</summary>
    public CommandType Type { get; set; } = CommandType.Todo;

    /// <summary>命令动作：新增 / 完成 / 删除 / 查询（B1 语音管理待办）。</summary>
    public CommandAction Action { get; set; } = CommandAction.Add;

    /// <summary>原始语音识别文本（便于调试/可观测）。</summary>
    public string? RawText { get; set; }

    /// <summary>标题（待办内容 / 定时器主题）。</summary>
    public string? Title { get; set; }

    /// <summary>定时器时长（当 Type=Timer 且为"持续 N 分钟"时）。</summary>
    public TimeSpan? Duration { get; set; }

    /// <summary>提醒触发时间（相对时间解析结果，如"5分钟后"）。</summary>
    public DateTimeOffset? TriggerAt { get; set; }

    /// <summary>是否重复。</summary>
    public bool IsRecurring { get; set; }

    /// <summary>重复规则文本（如 "every monday" / "每天周一"）。</summary>
    public string? RecurrenceRule { get; set; }

    /// <summary>可变间歇阶段序列（B 类：多段 work/rest）。</summary>
    public List<IntervalPhase>? Phases { get; set; }

    /// <summary>统一训练计划（Type=Timer 且口述含完整计划时填充；缺槽见 MissingSlots）。</summary>
    public TrainingPlan? Plan { get; set; }

    /// <summary>创建训练缺失的关键槽位（动作名/组数/每组时长/休息时长）；非空 = 需要追问，不得自动开始。</summary>
    public List<string> MissingSlots { get; set; } = new();

    /// <summary>提醒设置（NagMode / Snooze / 预提醒 / 自定义铃声）。</summary>
    public ReminderSettings Reminder { get; set; } = new();

    public bool IsValid => Action == CommandAction.Query || !string.IsNullOrWhiteSpace(Title);
}

public enum CommandType
{
    Todo,
    Timer
}

public enum CommandAction
{
    Add,
    Complete,
    Delete,
    Query
}
