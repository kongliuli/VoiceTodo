using System.Globalization;
using VoiceTodo.Core.Abstractions;

namespace VoiceTodo.Core.Services;

/// <summary>
/// 组合时间解析器：按注册顺序尝试各语言解析器，返回首个非空结果。
/// 对"中英混说"输入鲁棒（如 "5分钟后 remind me"）。
/// </summary>
public class CompositeTimeParser : ITimeParser
{
    private readonly IEnumerable<ITimeParser> _parsers;

    public CompositeTimeParser(IEnumerable<ITimeParser> parsers)
    {
        _parsers = parsers;
    }

    public DateTimeOffset? ParseRelative(string text, CultureInfo? culture = null)
    {
        foreach (var p in _parsers)
        {
            var r = p.ParseRelative(text, culture);
            if (r.HasValue) return r;
        }
        return null;
    }

    public bool TryParseDuration(string text, out TimeSpan duration)
    {
        foreach (var p in _parsers)
            if (p.TryParseDuration(text, out duration))
                return true;
        duration = TimeSpan.Zero;
        return false;
    }

    public bool TryParseRecurrence(string text, out string rule)
    {
        foreach (var p in _parsers)
            if (p.TryParseRecurrence(text, out rule))
                return true;
        rule = "";
        return false;
    }
}
