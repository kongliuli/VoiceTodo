using System.Globalization;
using VoiceTodo.Core.Models;

namespace VoiceTodo.Core.Abstractions;

/// <summary>意图分类抽象：将文本归类为 待办 / 定时器。</summary>
public interface IIntentParser
{
    VoiceCommand Parse(string text, CultureInfo? culture = null);
}
