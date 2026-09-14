namespace VoiceTodo.Maui.PlatformServices;

/// <summary>
/// 麦克风采集抽象（DEV-03 真实采集）。平台实现：AndroidAudioMicrophoneCapture / WindowsAudioMicrophoneCapture；
/// DemoMicrophoneCapture 保留用于无麦克风环境的端到端演示。
/// 空字符串作为「未获得音频」的收口结果（取消本句 / 启动失败），与页面既有 IsNullOrEmpty 处理一致。
/// </summary>
public interface IMicrophoneCapture
{
    /// <summary>
    /// 开始采集，最长 maxDuration（到点自动停止）。返回的任务在「完成 / 超时 / 取消」后结束：
    /// 成功返回 WAV 文件路径（16kHz 单声道 16bit PCM）；取消本句或启动失败返回空字符串。
    /// </summary>
    Task<string> CaptureWaveFileAsync(TimeSpan maxDuration, CancellationToken ct = default);

    /// <summary>「完成本句」：停止采集并保留音频用于识别。返回当前采集的收口结果；未在录音时返回空字符串。</summary>
    Task<string> StopAsync();

    /// <summary>「取消本句」：停止采集并丢弃已录文件（与完成是两个独立信号，不共用取消令牌）。未在录音时为空操作。</summary>
    Task CancelAsync();
}
