namespace VoiceTodo.Core.Models;

/// <summary>
/// 提醒设置（A 类功能）：Nag Mode、Snooze、预提醒、自定义/语音播报铃声。
/// 通知与定时器共享此配置，避免两套调度逻辑。
/// </summary>
public class ReminderSettings
{
    /// <summary>持续催促直到用户确认（ADHD / 健忘场景）。</summary>
    public bool NagMode { get; set; }

    /// <summary>延后时长（Snooze）。为空表示不自动延后。</summary>
    public TimeSpan? Snooze { get; set; }

    /// <summary>预提醒提前量（Pre-alert）。为空表示无预提醒。</summary>
    public TimeSpan? PreAlert { get; set; }

    /// <summary>自定义提醒铃声路径；或留空让 TTS 用语音播报标题。</summary>
    public string? CustomSoundPath { get; set; }
}
