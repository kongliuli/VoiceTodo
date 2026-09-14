using System.Text;

namespace VoiceTodo.Maui.PlatformServices;

/// <summary>
/// 演示用麦克风采集：返回应用数据目录下的示例 WAV（若存在），否则生成一段静音 WAV。
/// 使语音管线在无真机麦克风环境下也能端到端跑通；生产环境由平台实现替换（实现 IMicrophoneCapture）。
/// DEV-03：适配「完成本句 / 取消本句」语义 —— Stop 保留文件、Cancel 丢弃，超时令牌视为自然结束。
/// </summary>
public class DemoMicrophoneCapture : IMicrophoneCapture
{
    private readonly string _directory;
    private TaskCompletionSource<string>? _tcs;   // 当前采集会话的收口
    private string? _pendingPath;
    private CancellationTokenRegistration _ctr;

    public DemoMicrophoneCapture(string? directory = null)
    {
        _directory = directory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoiceTodo", "audio");
        Directory.CreateDirectory(_directory);
    }

    public Task<string> CaptureWaveFileAsync(TimeSpan maxDuration, CancellationToken ct = default)
    {
        if (_tcs is { Task.IsCompleted: false })
            return Task.FromResult(""); // 已在录音，拒绝重入

        var path = Path.Combine(_directory, "capture.wav");

        // 若已放置示例音频则直接复用（便于演示识别结果）
        if (!File.Exists(path))
        {
            // 否则写一段 1 秒静音 WAV（保证文件有效，识别结果为空字符串，pipeline 走 UnknownCommand 分支）
            WriteSilentWav(path, 16000, 1);
        }

        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _tcs = tcs;
        _pendingPath = path;
        // 超时/外部取消令牌 → 自然结束并保留文件（与真实实现的超时语义一致）
        _ctr = ct.Register(() => tcs.TrySetResult(path));
        return tcs.Task;
    }

    public Task<string> StopAsync()
    {
        var tcs = _tcs;
        if (tcs is null || tcs.Task.IsCompleted) return Task.FromResult("");
        try { _ctr.Dispose(); } catch { }
        tcs.TrySetResult(_pendingPath ?? ""); // 完成本句：保留文件
        return tcs.Task;
    }

    public Task CancelAsync()
    {
        var tcs = _tcs;
        if (tcs is null || tcs.Task.IsCompleted) return Task.CompletedTask;
        try { _ctr.Dispose(); } catch { }
        _tcs = null;
        _pendingPath = null;
        tcs.TrySetResult(""); // 取消本句：丢弃，采集任务以空结果收口
        return Task.CompletedTask;
    }

    private static void WriteSilentWav(string path, int sampleRate, int seconds)
    {
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs, Encoding.ASCII, true);
        int dataSize = sampleRate * seconds * 2; // 16-bit mono
        bw.Write("RIFF".ToCharArray());
        bw.Write(36 + dataSize);
        bw.Write("WAVE".ToCharArray());
        bw.Write("fmt ".ToCharArray());
        bw.Write(16);
        bw.Write((short)1);
        bw.Write((short)1);
        bw.Write(sampleRate);
        bw.Write(sampleRate * 2);
        bw.Write((short)2);
        bw.Write((short)16);
        bw.Write("data".ToCharArray());
        bw.Write(dataSize);
        bw.Write(new byte[dataSize], 0, dataSize);
    }
}
