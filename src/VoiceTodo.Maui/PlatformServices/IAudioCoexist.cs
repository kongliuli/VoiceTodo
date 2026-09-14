namespace VoiceTodo.Maui.PlatformServices;

/// <summary>
/// 背景音乐共存抽象（B6）。
/// 定时器阶段引导用 TTS 播报时，避免抢占/暂停用户的背景音乐：
/// 播报前 <see cref="DuckForSpeech"/> 用“短暂+压低”（MAY_DUCK）请求音频焦点，
/// 后台音乐仅短暂降低音量而非停止；播报结束 <see cref="Restore"/> 放弃焦点、恢复音量。
/// 非 Android 平台以空实现（Noop）静默生效。
/// </summary>
public interface IAudioCoexist
{
    /// <summary>播报前调用：压低后台声音但不暂停。</summary>
    void DuckForSpeech();

    /// <summary>播报结束调用：放弃焦点，恢复后台音量。</summary>
    void Restore();
}