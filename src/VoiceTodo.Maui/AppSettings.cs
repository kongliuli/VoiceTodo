using System.Globalization;
using Microsoft.Maui.Storage;
using VoiceTodo.Core.Resources;
using VoiceTodo.Maui.Resources;

namespace VoiceTodo.Maui;

/// <summary>
/// 全局设置（DEV-08：全量持久化 + 启动回读）。
/// 全部属性直接读写 <see cref="Preferences"/>，进程重启后保持；避免原先"只存内存静态属性、重启丢失"。
/// 语言偏好亦在此持久化，由 <see cref="Load"/> 在应用启动时回读生效（不再等到打开设置页才读）。
/// </summary>
public static class AppSettings
{
    // ── 首选项 key（集中管理，便于迁移/清理）──
    private const string KeyAnim = "useAnimations";
    private const string KeyAudioCoexist = "audioCoexist";
    private const string KeyScene = "scene";
    private const string KeyNag = "nagMode";
    private const string KeySnoozeMinutes = "snoozeMinutes";
    private const string KeyPreAlertMinutes = "preAlertMinutes";
    private const string KeyCustomSound = "customSoundPath";
    private const string KeyLang = "lang";

    /// <summary>是否启用界面动效（A6 开关控制）。</summary>
    public static bool UseAnimations
    {
        get => Preferences.Default.Get(KeyAnim, true);
        set => Preferences.Default.Set(KeyAnim, value);
    }

    /// <summary>背景音乐共存（B6 开关控制）：播报时压低而非暂停后台音乐。默认开启。</summary>
    public static bool AudioCoexist
    {
        get => Preferences.Default.Get(KeyAudioCoexist, true);
        set => Preferences.Default.Set(KeyAudioCoexist, value);
    }

    /// <summary>当前免提场景：general / driving / cooking / fitness（B3）。</summary>
    public static string Scene
    {
        get => Preferences.Default.Get(KeyScene, "general");
        set => Preferences.Default.Set(KeyScene, value);
    }

    public static bool NagMode
    {
        get => Preferences.Default.Get(KeyNag, false);
        set => Preferences.Default.Set(KeyNag, value);
    }

    /// <summary>默认延后（Snooze）；null = 不自动延后。</summary>
    public static TimeSpan? Snooze
    {
        get => ReadMinutes(KeySnoozeMinutes);
        set => WriteMinutes(KeySnoozeMinutes, value);
    }

    /// <summary>默认预提醒提前量；null = 无预提醒。</summary>
    public static TimeSpan? PreAlert
    {
        get => ReadMinutes(KeyPreAlertMinutes);
        set => WriteMinutes(KeyPreAlertMinutes, value);
    }

    /// <summary>自定义提醒铃声路径；null = 由 TTS 播报。</summary>
    public static string? CustomSoundPath
    {
        get
        {
            var v = Preferences.Default.Get(KeyCustomSound, "");
            return string.IsNullOrWhiteSpace(v) ? null : v;
        }
        set => Preferences.Default.Set(KeyCustomSound, value ?? "");
    }

    /// <summary>界面语言（zh / en）；持久化并即时生效。</summary>
    public static string Language
    {
        get => Preferences.Default.Get(KeyLang, "zh");
        set => Preferences.Default.Set(KeyLang, value);
    }

    /// <summary>
    /// 应用启动回读：把持久化的语言偏好生效到 Core/MAUI 两层文案（原实现只在打开设置页时读，启动仍是设备语言）。
    /// </summary>
    public static void Load()
    {
        try
        {
            var culture = new CultureInfo(Language == "en" ? "en" : "zh");
            CoreStrings.CurrentCulture = culture;
            AppResources.CurrentCulture = culture;
        }
        catch
        {
            // 首选项不可用等异常绝不阻塞应用启动：退回设备文化
        }
    }

    /// <summary>根据免提场景返回该场景下的默认提醒策略（B3 的轻量实现）。</summary>
    public static ReminderDefaults SceneDefaults =>
        Scene switch
        {
            "driving" => new ReminderDefaults { NagMode = true, PreAlert = TimeSpan.FromMinutes(2) },
            "fitness" => new ReminderDefaults { NagMode = false, Snooze = TimeSpan.FromMinutes(1) },
            "cooking" => new ReminderDefaults { NagMode = true, Snooze = TimeSpan.FromMinutes(1) },
            _ => new ReminderDefaults { NagMode = NagMode, Snooze = Snooze, PreAlert = PreAlert }
        };

    private static TimeSpan? ReadMinutes(string key)
    {
        var minutes = Preferences.Default.Get(key, -1);
        return minutes < 0 ? null : TimeSpan.FromMinutes(minutes);
    }

    private static void WriteMinutes(string key, TimeSpan? value)
        => Preferences.Default.Set(key, value is null ? -1 : (int)value.Value.TotalMinutes);
}

public sealed class ReminderDefaults
{
    public bool NagMode { get; init; }
    public TimeSpan? Snooze { get; init; }
    public TimeSpan? PreAlert { get; init; }
}
