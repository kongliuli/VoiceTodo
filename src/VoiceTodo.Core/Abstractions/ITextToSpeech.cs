using System.Globalization;

namespace VoiceTodo.Core.Abstractions;

/// <summary>语音合成抽象（平台实现：sherpa-onnx TTS）。</summary>
public interface ITextToSpeech
{
    Task SpeakAsync(string text, CultureInfo? culture = null, CancellationToken ct = default);
}
