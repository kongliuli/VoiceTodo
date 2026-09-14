using Microsoft.Maui.Storage;
using VoiceTodo.Core.Abstractions;

namespace VoiceTodo.Maui.PlatformServices;

/// <summary>
/// 静默时段配置（Maui 实现）：从 <see cref="Preferences"/> 读取，确保持久化（重启不丢）。
/// 注意：不得使用 AppSettings 静态字段（已知不持久化缺口），配置必须由 Preferences 承载。
/// 默认关闭；启用时默认 22:00–07:00（跨午夜）。
/// </summary>
public sealed class QuietHoursSettings : IQuietHours
{
    private const string KeyEnabled = "quiet.enabled";
    private const string KeyStart = "quiet.start";
    private const string KeyEnd = "quiet.end";
    private static readonly TimeSpan DefaultStart = new(22, 0, 0);
    private static readonly TimeSpan DefaultEnd = new(7, 0, 0);

    /// <inheritdoc/>
    public bool Enabled => Preferences.Default.Get(KeyEnabled, false);

    /// <inheritdoc/>
    public TimeSpan Start => TimeSpan.FromTicks(Preferences.Default.Get(KeyStart, DefaultStart.Ticks));

    /// <inheritdoc/>
    public TimeSpan End => TimeSpan.FromTicks(Preferences.Default.Get(KeyEnd, DefaultEnd.Ticks));
}
