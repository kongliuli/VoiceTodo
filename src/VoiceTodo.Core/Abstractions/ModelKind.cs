namespace VoiceTodo.Core.Abstractions;

/// <summary>端侧模型类别，用于在 IModelProvider 中按种类 + 语种定位模型目录。</summary>
public enum ModelKind
{
    /// <summary>语音识别（ASR），如 sherpa-onnx 的 SenseVoice / Whisper / Paraformer。</summary>
    Asr,
    /// <summary>语音合成（TTS），如 Piper / VITS / Kokoro。</summary>
    Tts,
    /// <summary>可选的端侧小语言模型（意图/实体增强），如 Phi-3。</summary>
    Nlu
}
