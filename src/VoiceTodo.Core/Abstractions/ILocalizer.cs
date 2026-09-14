using System.Globalization;

namespace VoiceTodo.Core.Abstractions;

/// <summary>本地化抽象。Core 与 MAUI 各自提供实现，文案集中在 RESX。</summary>
public interface ILocalizer
{
    CultureInfo CurrentCulture { get; }
    void SetCulture(CultureInfo culture);
    string this[string key] { get; }
}
