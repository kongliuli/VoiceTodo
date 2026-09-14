using System.Globalization;

namespace VoiceTodo.Core.Abstractions;

/// <summary>
/// 相对时间 / 时长 / 重复规则 解析抽象（C 类功能：自然度）。
/// 不同语言有不同实现（中文自写、英文 nChronic 包装等）。
/// </summary>
public interface ITimeParser
{
    /// <summary>解析相对时间（如 "5分钟后"、"后天"、"周四3点"）。</summary>
    DateTimeOffset? ParseRelative(string text, CultureInfo? culture = null);

    /// <summary>尝试从文本提取时长（如 "20分钟"）。</summary>
    bool TryParseDuration(string text, out TimeSpan duration);

    /// <summary>尝试解析重复规则（如 "每天周一"、"every monday"）。</summary>
    bool TryParseRecurrence(string text, out string rule);
}
