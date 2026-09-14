using System.Globalization;
using Microsoft.Maui.Storage;
using VoiceTodo.Core.Abstractions;

namespace VoiceTodo.Maui.PlatformServices;

/// <summary>
/// 语音合成实现（平台原生，无模型体积）。
/// Android 走 Android.Speech.TextToSpeech；Windows 走 Windows.Media.SpeechSynthesis；
/// 其余平台无原生实现时静默返回（不影响管线）。
/// 语种解析优先级：SettingsPage 写入的 ttsVoice 偏好 &gt; 调用方 culture &gt; zh。
/// </summary>
public sealed class NativeTextToSpeech : VoiceTodo.Core.Abstractions.ITextToSpeech
{
    public async Task SpeakAsync(string text, CultureInfo? culture = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        var lang = ResolveLang(culture);

#if ANDROID
        await SpeakAndroidAsync(text, lang, ct);
#elif WINDOWS
        await SpeakWindowsAsync(text, lang, ct);
#endif
        // 其它平台：暂无原生 TTS，静默返回。
    }

    /// <summary>解析播报语种：偏好值优先，回退 culture，再回退 zh；坏值清洗后不致命。</summary>
    private static string ResolveLang(CultureInfo? culture)
    {
        var pref = Preferences.Default.Get("ttsVoice", "zh");
        var resolved = SanitizeLang(pref);
        resolved ??= SanitizeLang(culture?.Name) ?? "zh";
        return resolved;
    }

    /// <summary>基本清洗：trim、长度上限 35、仅允许字母/数字/-/_；含非法字符返回 null（交由上层回退）。</summary>
    private static string? SanitizeLang(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        if (s.Length > 35) s = s.Substring(0, 35);
        foreach (var c in s)
            if (!char.IsLetterOrDigit(c) && c != '-' && c != '_') return null;
        return s.ToLowerInvariant();
    }

#if ANDROID
    private static async Task SpeakAndroidAsync(string text, string lang, CancellationToken ct)
    {
        var context = Android.App.Application.Context;
        var listener = new OnInitListener { Text = text, Lang = lang };
        var tts = new Android.Speech.Tts.TextToSpeech(context, listener);
        listener.Tts = tts;

        try
        {
            // 等待初始化完成并播报（短超时，避免阻塞）。
            await Task.Delay(1500, ct);
        }
        finally
        {
            tts.Shutdown();
        }
    }

    /// <summary>Android TTS 初始化监听：初始化成功后在指定语言下播报文本。</summary>
    private sealed class OnInitListener : Java.Lang.Object, Android.Speech.Tts.TextToSpeech.IOnInitListener
    {
        public Android.Speech.Tts.TextToSpeech? Tts { get; set; }
        public string Text { get; set; } = "";
        public string Lang { get; set; } = "";

        public void OnInit(Android.Speech.Tts.OperationResult status)
        {
            if (status == Android.Speech.Tts.OperationResult.Success && Tts is not null)
            {
                Tts.SetLanguage(Java.Util.Locale.ForLanguageTag(Lang));
                Tts.Speak(Text, Android.Speech.Tts.QueueMode.Flush, null, $"tts-{Guid.NewGuid():N}");
            }
        }
    }
#endif

#if WINDOWS
    private static async Task SpeakWindowsAsync(string text, string lang, CancellationToken ct)
    {
        using var synthesizer = new Windows.Media.SpeechSynthesis.SpeechSynthesizer();
        // 按偏好语种前缀匹配嗓音（如 zh 匹配 zh-CN）；匹配不到保持默认嗓音（静默降级，不抛）
        try
        {
            if (!string.IsNullOrEmpty(lang))
            {
                var match = Windows.Media.SpeechSynthesis.SpeechSynthesizer.AllVoices.FirstOrDefault(v =>
                    v.Language.StartsWith(lang, StringComparison.OrdinalIgnoreCase));
                if (match is not null) synthesizer.Voice = match;
            }
        }
        catch
        {
            // 匹配/赋值失败：保持默认嗓音
        }
        var stream = await synthesizer.SynthesizeTextToStreamAsync(text);
        var player = new Windows.Media.Playback.MediaPlayer { Source = Windows.Media.Core.MediaSource.CreateFromStream(stream, stream.ContentType) };
        player.Play();
        await Task.Delay(TimeSpan.FromSeconds(Math.Max(2, text.Length / 12)), ct);
        player.Pause();
        player.Dispose();
    }
#endif
}