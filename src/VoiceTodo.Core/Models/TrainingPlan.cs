namespace VoiceTodo.Core.Models;

/// <summary>
/// 统一训练计划：口述创建 / 文字创建 / 模板套用 / 历史复用四入口共用的语义表示。
/// 见 docs/plan/1330/05-spoken-training-model.md。执行段由 <see cref="Expand"/> 派生，
/// 各处不再各自维护扁平阶段序列（根治轮次显示不一致与「最近使用」丢失间歇结构）。
/// </summary>
public class TrainingPlan
{
    /// <summary>动作名称（用户文本，如「深蹲」），不要求匹配预设动作或模板。</summary>
    public string ActionName { get; set; } = "";

    /// <summary>组数（≥1）。注意「10 次」不等于「10 组」，计次语法不得落入本字段。</summary>
    public int Rounds { get; set; } = 1;

    /// <summary>每组运动时长。</summary>
    public TimeSpan WorkDuration { get; set; }

    /// <summary>组间休息时长（可为 0 = 无休息）。</summary>
    public TimeSpan RestDuration { get; set; } = TimeSpan.Zero;

    /// <summary>末组休息：默认 false（深蹲例 10×5s＋9×10s = 140s = 02:20；开启则 02:30）。</summary>
    public bool RestAfterLastRound { get; set; }

    /// <summary>阶段末倒数窗口（秒）：5 / 3 / 0 = 关闭。作用于运动与休息阶段末尾。</summary>
    public int CountdownWindowSeconds { get; set; }

    /// <summary>阶段指示播报开关（「第 1 组，深蹲」「休息 10 秒」等）。</summary>
    public bool PhaseAnnouncements { get; set; } = true;

    /// <summary>总时长 = 组数×运动 ＋（组数-1＋末组休息?1:0）×休息。</summary>
    public TimeSpan TotalDuration =>
        TimeSpan.FromTicks(Rounds * WorkDuration.Ticks
            + (Rounds - 1 + (RestAfterLastRound ? 1 : 0)) * RestDuration.Ticks);

    /// <summary>计划是否已具备自动开始的完整参数（动作名非空、组数≥1、运动时长&gt;0）。</summary>
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(ActionName) && Rounds >= 1 && WorkDuration > TimeSpan.Zero;

    /// <summary>
    /// 展开为执行段序列：work×Rounds ＋ rest×（RestAfterLastRound ? Rounds : Rounds-1）。
    /// rest 轮次为 0 或时长为 0 时不产出休息段。
    /// </summary>
    public List<IntervalPhase> Expand()
    {
        var phases = new List<IntervalPhase>
        {
            new() { Kind = "work", Duration = WorkDuration, Rounds = Math.Max(1, Rounds) }
        };
        int restRounds = RestAfterLastRound ? Rounds : Rounds - 1;
        if (RestDuration > TimeSpan.Zero && restRounds > 0)
            phases.Add(new IntervalPhase { Kind = "rest", Duration = RestDuration, Rounds = restRounds });
        return phases;
    }

    /// <summary>展示用摘要（如「深蹲 · 10 组 · 运动 5 秒 · 休息 10 秒 · 02:20」）。</summary>
    public string Describe() =>
        $"{ActionName} · {Rounds} 组 · 运动 {Format(WorkDuration)} · 休息 {Format(RestDuration)} · {TotalDuration:hh\\:mm\\:ss}";

    private static string Format(TimeSpan t) =>
        t == TimeSpan.Zero ? "无"
        : t.TotalSeconds is >= 1 and < 60 ? $"{(int)t.TotalSeconds} 秒"
        : $"{(int)t.TotalMinutes} 分钟";
}
