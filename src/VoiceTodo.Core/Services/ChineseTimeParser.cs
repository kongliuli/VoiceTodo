using System.Globalization;
using System.Text.RegularExpressions;
using VoiceTodo.Core.Abstractions;

namespace VoiceTodo.Core.Services;

/// <summary>
/// 中文相对时间 / 时长 / 重复规则 解析（C 类功能）。规则 + 正则，离线。
/// 覆盖：X分钟/小时/秒、X分钟后、今天/明天/后天、周X/星期X、每天周一、每天。
/// 口语数词（DEV：普通提醒/倒计时路径）：支持中文数词「五/十五/二十五」与「半」（半小时=30 分钟、半分钟=30 秒），
/// 使「五分钟后」「半小时后」「十五分钟倒计时」等口述与训练计划的数词支持对齐。
/// </summary>
public partial class ChineseTimeParser : ITimeParser
{
    private static readonly Dictionary<string, double> UnitToMinutes = new()
    {
        { "秒", 1.0 / 60 }, { "秒钟", 1.0 / 60 }, { "分", 1 }, { "分钟", 1 },
        { "小时", 60 }, { "时", 60 }
    };

    private static readonly Dictionary<string, DayOfWeek> CnWeekDay = new()
    {
        { "一", DayOfWeek.Monday }, { "二", DayOfWeek.Tuesday }, { "三", DayOfWeek.Wednesday },
        { "四", DayOfWeek.Thursday }, { "五", DayOfWeek.Friday }, { "六", DayOfWeek.Saturday },
        { "日", DayOfWeek.Sunday }, { "天", DayOfWeek.Sunday }
    };

    private static readonly Dictionary<string, double> CnDigit = new()
    {
        ["一"] = 1, ["二"] = 2, ["两"] = 2, ["三"] = 3, ["四"] = 4,
        ["五"] = 5, ["六"] = 6, ["七"] = 7, ["八"] = 8, ["九"] = 9
    };

    public DateTimeOffset? ParseRelative(string text, CultureInfo? culture = null)
    {
        text = text ?? "";

        // "X分钟后" / "X小时后"（X 可为阿拉伯数字、中文数词或「半」）
        var m = DurationRegex().Match(text);
        if (m.Success && text.Contains('后'))
        {
            var num = ParseNumber(m.Groups[1].Value);
            if (num is not null)
            {
                var minutes = num.Value * UnitToMinutes[m.Groups[2].Value];
                return DateTimeOffset.Now.AddMinutes(minutes);
            }
        }

        if (text.Contains("大后天")) return DateTimeOffset.Now.AddDays(3);
        if (text.Contains("后天")) return DateTimeOffset.Now.AddDays(2);
        if (text.Contains("明天")) return DateTimeOffset.Now.AddDays(1);
        if (text.Contains("今天")) return DateTimeOffset.Now;

        // 周X / 星期X
        var wm = WeekRegex().Match(text);
        if (wm.Success)
        {
            var target = CnWeekDay[wm.Groups[1].Value];
            var now = DateTimeOffset.Now;
            int days = ((int)target - (int)now.DayOfWeek + 7) % 7;
            if (days == 0) days = 7; // 默认下一个该星期几
            var date = now.AddDays(days);
            // 尝试解析 "X点"（阿拉伯数字或中文数词）
            int hour = date.Hour, minute = 0;
            var hm = HourRegex().Match(text);
            if (hm.Success)
            {
                if (ParseNumber(hm.Groups[1].Value) is { } h) hour = (int)h;
                if (hm.Groups[2].Success && ParseNumber(hm.Groups[2].Value) is { } mi) minute = (int)mi;
            }
            return new DateTimeOffset(date.Year, date.Month, date.Day, hour, minute, 0, date.Offset);
        }

        return null;
    }

    public bool TryParseDuration(string text, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        var m = DurationRegex().Match(text);
        if (!m.Success) return false;
        var num = ParseNumber(m.Groups[1].Value);
        if (num is null) return false;
        duration = TimeSpan.FromMinutes(num.Value * UnitToMinutes[m.Groups[2].Value]);
        return true;
    }

    public bool TryParseRecurrence(string text, out string rule)
    {
        rule = "";
        if (text.Contains("每个工作日") || text.Contains("每工作日") || text.Contains("工作日"))
        { rule = "every weekday"; return true; }
        if (text.Contains("每天")) { rule = "every day"; return true; }
        var wm = WeekRegex().Match(text);
        if (wm.Success && (text.Contains("每") || text.Contains("周") || text.Contains("星期")))
        {
            rule = "every " + CnWeekDay[wm.Groups[1].Value].ToString().ToLowerInvariant();
            return true;
        }
        return false;
    }

    /// <summary>解析阿拉伯数字 / 中文数词（一~九、十、含「十」的两位数、半）。</summary>
    private static double? ParseNumber(string s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        if (s == "半") return 0.5;
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
        if (s == "十") return 10;
        var idx = s.IndexOf('十');
        if (idx < 0)
            return s.Length == 1 && CnDigit.TryGetValue(s, out var v) ? v : null;
        var tens = idx == 0 ? 1 : CnDigit.TryGetValue(s[..idx], out var t) ? t : -1;
        var ones = idx == s.Length - 1 ? 0 : CnDigit.TryGetValue(s[(idx + 1)..], out var o) ? o : -1;
        if (tens < 0 || ones < 0) return null;
        return tens * 10 + ones;
    }

    // 数词：阿拉伯数字 / 中文数词（1~3 字，覆盖 十/十五/二十五 等）/ 半
    private const string Num = @"(\d+(?:\.\d+)?|[一二两三四五六七八九十]{1,3}|半)";

    [GeneratedRegex(Num + @"\s*(秒钟|秒|分钟|分|小时|时)")]
    private static partial Regex DurationRegex();

    [GeneratedRegex(@"(?:周|星期)([一二三四五六日天])")]
    private static partial Regex WeekRegex();

    [GeneratedRegex(Num + @"\s*点(?:\s*" + Num + @"\s*分)?")]
    private static partial Regex HourRegex();
}
