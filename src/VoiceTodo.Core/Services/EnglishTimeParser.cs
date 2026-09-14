using System.Globalization;
using System.Text.RegularExpressions;
using VoiceTodo.Core.Abstractions;

namespace VoiceTodo.Core.Services;

/// <summary>
/// 英文相对时间 / 时长 / 重复规则 解析。规则 + 正则，离线。
/// （后续可替换为 nChronic / ChronicNetCore 以获得更强泛化。）
/// 覆盖：in X minutes/hours、tomorrow、next monday、every monday、every day。
/// </summary>
public partial class EnglishTimeParser : ITimeParser
{
    private static readonly Dictionary<string, double> UnitToMinutes = new()
    {
        { "sec", 1.0 / 60 }, { "secs", 1.0 / 60 }, { "second", 1.0 / 60 }, { "seconds", 1.0 / 60 },
        { "min", 1 }, { "mins", 1 }, { "minute", 1 }, { "minutes", 1 },
        { "hr", 60 }, { "hrs", 60 }, { "hour", 60 }, { "hours", 60 }
    };

    public DateTimeOffset? ParseRelative(string text, CultureInfo? culture = null)
    {
        text = text?.ToLowerInvariant() ?? "";

        var m = InRegex().Match(text);
        if (m.Success)
        {
            if (double.TryParse(m.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var num))
                return DateTimeOffset.Now.AddMinutes(num * UnitToMinutes[m.Groups[2].Value]);
        }

        if (text.Contains("day after tomorrow")) return DateTimeOffset.Now.AddDays(2);
        if (text.Contains("tomorrow")) return DateTimeOffset.Now.AddDays(1);
        if (text.Contains("today")) return DateTimeOffset.Now;

        var wm = WeekRegex().Match(text);
        if (wm.Success)
        {
            var target = Enum.Parse<DayOfWeek>(wm.Groups[1].Value, true);
            var now = DateTimeOffset.Now;
            int days = ((int)target - (int)now.DayOfWeek + 7) % 7;
            if (days == 0) days = 7;
            return now.AddDays(days);
        }

        return null;
    }

    public bool TryParseDuration(string text, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        var m = DurationRegex().Match(text);
        if (!m.Success) return false;
        if (!double.TryParse(m.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var num))
            return false;
        duration = TimeSpan.FromMinutes(num * UnitToMinutes[m.Groups[2].Value]);
        return true;
    }

    public bool TryParseRecurrence(string text, out string rule)
    {
        rule = "";
        text = text?.ToLowerInvariant() ?? "";
        if (text.Contains("every day")) { rule = "every day"; return true; }
        var wm = WeekRegex().Match(text);
        if (wm.Success && text.Contains("every"))
        {
            rule = "every " + wm.Groups[1].Value.ToLowerInvariant();
            return true;
        }
        return false;
    }

    [GeneratedRegex(@"(?:in\s+)?(\d+(?:\.\d+)?)\s*(sec|secs|second|seconds|min|mins|minute|minutes|hr|hrs|hour|hours)")]
    private static partial Regex InRegex();

    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s*(sec|secs|second|seconds|min|mins|minute|minutes|hr|hrs|hour|hours)")]
    private static partial Regex DurationRegex();

    [GeneratedRegex(@"(?:next\s+)?(monday|tuesday|wednesday|thursday|friday|saturday|sunday)")]
    private static partial Regex WeekRegex();
}
