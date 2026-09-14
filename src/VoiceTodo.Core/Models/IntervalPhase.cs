namespace VoiceTodo.Core.Models;

/// <summary>
/// 间歇阶段（B 类功能：可变间歇序列）。
/// 例如：举杠铃 1 分钟练 / 1 分钟歇，再 2 分钟练 / 2 分钟歇。
/// </summary>
public class IntervalPhase
{
    /// <summary>阶段类型：work（训练）或 rest（休息）。</summary>
    public string Kind { get; set; } = "work";

    /// <summary>该阶段时长。</summary>
    public TimeSpan Duration { get; set; }

    /// <summary>该阶段重复轮次（默认 1）。</summary>
    public int Rounds { get; set; } = 1;
}
