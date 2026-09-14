namespace VoiceTodo.Core.Abstractions;

/// <summary>语音识别抽象（平台实现：sherpa-onnx）。</summary>
public interface ISpeechRecognizer
{
    Task<string> RecognizeAsync(CancellationToken ct = default);

    /// <summary>识别已有 WAV 文件（16kHz 16bit 单声道），供自控录音流程使用。</summary>
    Task<string> RecognizeFileAsync(string wavPath, CancellationToken ct = default);
}
