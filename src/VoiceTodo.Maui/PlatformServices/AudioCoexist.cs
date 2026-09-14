#if ANDROID
using Android.Content;
using Android.Media;
#endif

namespace VoiceTodo.Maui.PlatformServices;

#if ANDROID
/// <summary>
/// Android 音频焦点共存（B6）。
/// 播报前用 AudioFocusRequest 以 GainTransientMayDuck 请求音频焦点：
/// 系统会告知后台音乐“仅压低、别暂停”；播报完即放弃焦点，恢复后台音量。
/// 所有焦点操作包 try/catch，失败时静默降级（与项目“绝不拖垮应用”的约定一致）。
/// </summary>
public sealed class AndroidAudioCoexist : IAudioCoexist
{
    private readonly AudioManager _audioManager;
    private readonly AudioManager.IOnAudioFocusChangeListener _listener = new NoopFocusListener();
    private AudioFocusRequestClass? _focusRequest;

    public AndroidAudioCoexist()
    {
        _audioManager = (AudioManager)Android.App.Application.Context
            .GetSystemService(Context.AudioService);
        try
        {
            var attrs = new AudioAttributes.Builder()
                .SetUsage(AudioUsageKind.Media)
                .SetContentType(AudioContentType.Speech)
                .Build();
            _focusRequest = new AudioFocusRequestClass.Builder(AudioFocus.GainTransientMayDuck)
                .SetAudioAttributes(attrs)
                .SetOnAudioFocusChangeListener(_listener)
                .Build();
        }
        catch
        {
            // 焦点请求构造失败即放弃，静默降级为空实现。
            _focusRequest = null;
        }
    }

    public void DuckForSpeech()
    {
        if (_focusRequest is null) return;
        try { _audioManager.RequestAudioFocus(_focusRequest); } catch { }
    }

    public void Restore()
    {
        if (_focusRequest is null) return;
        try { _audioManager.AbandonAudioFocusRequest(_focusRequest); } catch { }
    }

    private sealed class NoopFocusListener : Java.Lang.Object,
        AudioManager.IOnAudioFocusChangeListener
    {
        // 只关心“拿到/释放”副作用，焦点状态变化忽略即可。
        public void OnAudioFocusChange(AudioFocus focusChange) { }
    }
}
#else
/// <summary>非 Android 平台：Windows/其余平台的系统音频不抢占用户播放，空实现即可。</summary>
public sealed class NoopAudioCoexist : IAudioCoexist
{
    public void DuckForSpeech() { }
    public void Restore() { }
}
#endif