namespace VoiceTodo.Core.Models;

/// <summary>计时运行记录（留痕：跑完的计时进日历、可改名）。</summary>
public class TimerSession
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    /// <summary>当次结构快照（work/rest/countdown 阶段）。</summary>
    public List<IntervalPhase> Phases { get; set; } = new();
    public int Rounds { get; set; } = 1;
    /// <summary>completed = 自然跑完；cancelled = 中途结束。</summary>
    public string Outcome { get; set; } = "completed";
}
