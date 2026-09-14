using System.Globalization;
using VoiceTodo.Core.Abstractions;
using VoiceTodo.Core.Resources;

namespace VoiceTodo.Core.Services;

/// <summary>Core 本地化实现，文案集中在 RESX（CoreStrings）。</summary>
public class CoreLocalizer : ILocalizer
{
    public CultureInfo CurrentCulture => CoreStrings.CurrentCulture;

    public void SetCulture(CultureInfo culture) => CoreStrings.CurrentCulture = culture;

    public string this[string key] => CoreStrings.Get(key);
}
