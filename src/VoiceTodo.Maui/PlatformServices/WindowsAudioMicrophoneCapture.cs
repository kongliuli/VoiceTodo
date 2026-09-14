#if WINDOWS
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;
#endif

namespace VoiceTodo.Maui.PlatformServices;

#if WINDOWS
/// <summary>
/// Windows 真实采集（DEV-03）：WinRT MediaCapture（audio-only）经 MediaEncodingProfile.CreateWav
/// 录 16kHz 单声道 16bit PCM 的 WAV 到内存流，停止后落盘 AppData/captures（文件带时间戳）。
/// unpackaged + WindowsAppSDKSelfContained 环境可用；麦克风隐私由页面层用 AppCapability(microphone) 检查/申请。
/// 所有 WinRT 操作包 try/catch，失败静默收口为空结果，绝不拖垮应用。
/// </summary>
public sealed class WindowsAudioMicrophoneCapture : IMicrophoneCapture
{
    private TaskCompletionSource<bool>? _stopSignal;   // 停止信号：完成本句 / 取消本句 / 超时
    private Task<string>? _captureTask;
    private volatile bool _cancelRequested;

    public Task<string> CaptureWaveFileAsync(TimeSpan maxDuration, CancellationToken ct = default)
    {
        if (_stopSignal is { Task.IsCompleted: false })
            return Task.FromResult(""); // 已在录音，拒绝重入

        var task = CaptureCoreAsync(ct);
        _captureTask = task;
        return task;
    }

    public Task<string> StopAsync()
    {
        _stopSignal?.TrySetResult(true); // 完成本句：保留音频
        return _captureTask ?? Task.FromResult("");
    }

    public Task CancelAsync()
    {
        var signal = _stopSignal;
        if (signal is null || signal.Task.IsCompleted) return Task.CompletedTask;
        _cancelRequested = true; // 取消本句：丢弃，不落盘
        signal.TrySetResult(true);
        return Task.CompletedTask;
    }

    /// <summary>采集主体：初始化 → 录音 → 等停止信号 → 落盘 / 丢弃。</summary>
    private async Task<string> CaptureCoreAsync(CancellationToken ct)
    {
        _cancelRequested = false;
        var stopSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _stopSignal = stopSignal;

        var dir = Path.Combine(FileSystem.AppDataDirectory, "captures");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"capture-{DateTime.Now:yyyyMMdd-HHmmss}.wav");

        // 超时/外部取消令牌 → 自然结束并保留文件（区别于 CancelAsync 的丢弃语义）
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var reg = timeoutCts.Token.Register(() => stopSignal.TrySetResult(true));

        MediaCapture? capture = null;
        InMemoryRandomAccessStream? stream = null;
        var failed = false;
        try
        {
            capture = new MediaCapture();
            await capture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                StreamingCaptureMode = StreamingCaptureMode.Audio, // 仅音频
                MediaCategory = MediaCategory.Speech                // 语音场景 DSP 链
            });

            // whisper 侧按「44 字节 WAV 头 + 16kHz PCM」裸读，编码档必须固定 16kHz 单声道 16bit
            var profile = MediaEncodingProfile.CreateWav(AudioEncodingQuality.Auto);
            if (profile.Audio is not null)
            {
                profile.Audio.SampleRate = 16000;
                profile.Audio.ChannelCount = 1;
                profile.Audio.BitsPerSample = 16;
            }

            stream = new InMemoryRandomAccessStream();
            await capture.StartRecordToStreamAsync(profile, stream);
            await stopSignal.Task; // 等待 完成本句 / 取消本句 / 超时 信号
            if (!_cancelRequested)
                await capture.StopRecordAsync(); // 收尾 WAV 容器
        }
        catch
        {
            failed = true; // 初始化或录音失败（权限被系统拒绝 / 麦克风被占用等）
        }
        finally
        {
            try { capture?.Dispose(); } catch { }
            capture = null;
        }

        string result;
        if (failed || _cancelRequested || stream is null)
        {
            result = ""; // 失败 / 取消本句：不落盘
        }
        else
        {
            try
            {
                result = await WriteStreamToFileAsync(stream, path);
            }
            catch
            {
                result = "";
            }
        }
        try { stream?.Dispose(); } catch { }
        _stopSignal = null;
        _captureTask = null;
        return result;
    }

    /// <summary>把内存 WAV 流整体读出并写盘（≤60s ≈ 1.9MB，一次 LoadAsync 可承受）。</summary>
    private static async Task<string> WriteStreamToFileAsync(InMemoryRandomAccessStream stream, string path)
    {
        var size = (uint)Math.Min(stream.Size, int.MaxValue);
        var reader = new DataReader(stream.GetInputStreamAt(0));
        try
        {
            await reader.LoadAsync(size);
            var bytes = new byte[reader.UnconsumedBufferLength];
            reader.ReadBytes(bytes);
            await File.WriteAllBytesAsync(path, bytes);
            return path;
        }
        finally
        {
            reader.Dispose();
        }
    }
}
#else
/// <summary>非 Windows 平台占位：真实采集由 AndroidAudioMicrophoneCapture 提供（与 AudioCoexist 同模式）。</summary>
public sealed class WindowsAudioMicrophoneCapture : IMicrophoneCapture
{
    public Task<string> CaptureWaveFileAsync(TimeSpan maxDuration, CancellationToken ct = default)
        => Task.FromResult("");
    public Task<string> StopAsync() => Task.FromResult("");
    public Task CancelAsync() => Task.CompletedTask;
}
#endif
