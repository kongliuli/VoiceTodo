using VoiceTodo.Core.Abstractions;

namespace VoiceTodo.Core;

/// <summary>
/// 静默时段纯函数换算（无平台依赖）：给定「应触发时刻」，若落入静默时段内，
/// 顺延到该静默时段结束后立即触发；不丢弃、不提前。时段跨午夜（End &lt; Start）正确判断。
/// 关闭静默时段时原样返回，绝不产生任何顺延副作用。
/// </summary>
public static class QuietHoursPolicy
{
    /// <summary>
    /// 把触发时刻 <paramref name="when"/> 换算为最终触发时刻。
    /// - 未启用 → 原样返回；
    /// - 落在静默窗口内 → 顺延到窗口结束（跨午夜时结束时刻可能落在次日）；
    /// - 不落在窗口内 → 原样返回。
    /// </summary>
    public static DateTimeOffset Defer(DateTimeOffset when, IQuietHours quiet)
    {
        if (quiet is null || !quiet.Enabled)
            return when;

        var t = when.TimeOfDay;
        bool inWindow;
        if (quiet.Start <= quiet.End)
            inWindow = t >= quiet.Start && t < quiet.End;      // 不跨午夜：[Start, End)
        else
            inWindow = t >= quiet.Start || t < quiet.End;       // 跨午夜：[Start,24:00) ∪ [00:00, End)

        if (!inWindow)
            return when; // 不在静默窗口：立即触发，无副作用

        // 在窗口内：顺延到最近一次窗口结束时刻
        var day = when.Date;
        if (quiet.Start <= quiet.End)
        {
            // 窗口结束在同一天
            return new DateTimeOffset(day + quiet.End, when.Offset);
        }

        // 跨午夜：Start..24:00 段 → 结束是次日 End；00:00..End 段 → 结束是当天 End
        return t >= quiet.Start
            ? new DateTimeOffset(day.AddDays(1) + quiet.End, when.Offset)
            : new DateTimeOffset(day + quiet.End, when.Offset);
    }
}
