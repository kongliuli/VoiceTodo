using System.Globalization;
using System.Resources;

namespace VoiceTodo.Core.Resources;

/// <summary>
/// Core 本地化字符串。中性（英文）资源在 CoreStrings.resx，中文卫星在 CoreStrings.zh.resx。
/// TTS 语音短语与解析提示均从此读取，新增语言只需补对应 .resx。
/// </summary>
public static class CoreStrings
{
    private static readonly ResourceManager _rm =
        new("VoiceTodo.Core.Resources.CoreStrings", typeof(CoreStrings).Assembly);

    private static CultureInfo? _culture;

    public static CultureInfo CurrentCulture
    {
        get => _culture ?? CultureInfo.CurrentUICulture;
        set => _culture = value;
    }

    public static string Get(string key) => _rm.GetString(key, CurrentCulture) ?? key;

    public static string AppName => Get(nameof(AppName));
    public static string UnknownCommand => Get(nameof(UnknownCommand));
    public static string CreatedTodo => Get(nameof(CreatedTodo));
    public static string CreatedTimer => Get(nameof(CreatedTimer));
    public static string Listening => Get(nameof(Listening));
    public static string Reminder => Get(nameof(Reminder));
    public static string NagModeOn => Get(nameof(NagModeOn));
    public static string Work => Get(nameof(Work));
    public static string Rest => Get(nameof(Rest));
    public static string Start => Get(nameof(Start));
    public static string Stop => Get(nameof(Stop));
    public static string Confirm => Get(nameof(Confirm));
    public static string Snooze => Get(nameof(Snooze));
    public static string Dismiss => Get(nameof(Dismiss));
    public static string TimerDone => Get(nameof(TimerDone));

    // DEV-04 plan：统一计划口述反馈（已开始 / 缺槽追问）
    public static string PlanStartedFormat => Get(nameof(PlanStartedFormat));
    public static string PlanMissingAskFormat => Get(nameof(PlanMissingAskFormat));
    public static string SlotAction => Get(nameof(SlotAction));
    public static string SlotRounds => Get(nameof(SlotRounds));
    public static string SlotWork => Get(nameof(SlotWork));
    public static string SlotRest => Get(nameof(SlotRest));
    public static string SlotCountHint => Get(nameof(SlotCountHint));
}
