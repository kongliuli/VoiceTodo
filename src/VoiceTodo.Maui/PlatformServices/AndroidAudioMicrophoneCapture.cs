#if ANDROID
using Android.Media;
#endif

namespace VoiceTodo.Maui.PlatformServices;

#if ANDROID
/// <summary>
/// Android 真实采集（DEV-03）：AudioRecord 16kHz 单声道 16bit PCM 边录边写 WAV，
/// 落盘 AppData/captures（文件带时间戳）；停止时回填 RIFF/data 长度。
/// 权限申请由页面层（Permissions.Microphone）负责，这里只负责采集本身；
/// 所有设备操作包 try/catch，失败静默收口为空结果，绝不拖垮应用。
/// </summary>
public sealed class AndroidAudioMicrophoneCapture : IMicrophoneCapture
{
    private const int SampleRate = 16000;

    private AudioRecord? _recorder;
    private BinaryWriter? _writer;
    private FileStream? _file;
    private string? _path;
    private TaskCompletionSource<string>? _tcs;
    private CancellationTokenRegistration _ctr;
    private volatile bool _recording;
    private long _pcmBytes;

    public Task<string> CaptureWaveFileAsync(TimeSpan maxDuration, CancellationToken ct = default)
    {
        if (_tcs is { Task.IsCompleted: false })
            return Task.FromResult(""); // 已在录音，拒绝重入

        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var dir = Path.Combine(FileSystem.AppDataDirectory, "captures");
            Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, $"capture-{DateTime.Now:yyyyMMdd-HHmmss}.wav");

            _file = File.Create(_path);
            _writer = new BinaryWriter(_file);
            WriteWavHeader(_writer, 0); // 先写占位头，停止时回填真实长度

            var minBuffer = AudioRecord.GetMinBufferSize(SampleRate, ChannelIn.Mono, Encoding.Pcm16bit);
            _recorder = new AudioRecord(AudioSource.Mic, SampleRate,
                ChannelIn.Mono, Encoding.Pcm16bit, Math.Max(minBuffer, 2048) * 4);
            if (_recorder.State != State.Initialized)
                throw new InvalidOperationException("AudioRecord 初始化失败（权限未授予或麦克风被占用）");
            _recorder.StartRecording();

            _recording = true;
            _pcmBytes = 0;
            _tcs = tcs;
            _ = Task.Run(ReadLoop);
            // 超时/外部取消令牌 → 自然结束并保留文件（区别于 CancelAsync 的丢弃语义）
            _ctr = ct.Register(() => Finish());
        }
        catch
        {
            Cleanup(discard: true);
            tcs.TrySetResult(""); // 启动失败：页面据此进入「麦克风不可用」状态
        }
        return tcs.Task;
    }

    public Task<string> StopAsync()
    {
        var tcs = _tcs;
        if (tcs is null || tcs.Task.IsCompleted) return Task.FromResult("");
        Finish(); // 同步收口：停读、释放设备、回填 WAV 头
        return tcs.Task;
    }

    public Task CancelAsync()
    {
        var tcs = _tcs;
        if (tcs is null || tcs.Task.IsCompleted) return Task.CompletedTask;
        Cleanup(discard: true); // 取消本句：丢弃文件
        tcs.TrySetResult("");
        return Task.CompletedTask;
    }

    /// <summary>后台读循环：持续把 PCM 写入文件，直到停止标志位。</summary>
    private void ReadLoop()
    {
        var buffer = new byte[4096]; // 128ms @ 16kHz 16bit，停止后单次 Read 很快返回
        while (_recording)
        {
            try
            {
                var read = _recorder?.Read(buffer, 0, buffer.Length) ?? 0;
                if (read > 0)
                {
                    _writer?.Write(buffer, 0, read);
                    _pcmBytes += read;
                }
            }
            catch
            {
                break; // 采集异常即结束循环，收口由 Finish/Cleanup 负责
            }
        }
    }

    /// <summary>完成本句：停读、释放设备、回填 WAV 头、以路径收口（保留文件）。</summary>
    private void Finish()
    {
        if (_tcs is null || _tcs.Task.IsCompleted) return;
        _recording = false;
        try { _ctr.Dispose(); } catch { }
        try { _recorder?.Stop(); } catch { }
        try { _recorder?.Release(); } catch { }
        _recorder = null;
        try { _writer?.Flush(); } catch { }
        try { if (_writer is not null) WriteWavHeader(_writer, _pcmBytes); } catch { }
        try { _writer?.Dispose(); } catch { }
        try { _file?.Dispose(); } catch { }
        _writer = null;
        _file = null;
        var tcs = _tcs;
        var path = _path;
        _tcs = null;
        _path = null;
        tcs!.TrySetResult(path ?? "");
    }

    /// <summary>释放设备并可选删除文件（取消本句 / 启动失败路径）。</summary>
    private void Cleanup(bool discard)
    {
        _recording = false;
        try { _ctr.Dispose(); } catch { }
        try { _recorder?.Stop(); } catch { }
        try { _recorder?.Release(); } catch { }
        _recorder = null;
        try { _writer?.Dispose(); } catch { }
        try { _file?.Dispose(); } catch { }
        _writer = null;
        _file = null;
        var path = _path;
        _tcs = null;
        _path = null;
        if (discard && path is not null)
        {
            try { File.Delete(path); } catch { }
        }
    }

    /// <summary>写 44 字节标准 WAV 头；pcmBytes 为实际 PCM 字节数（占位 0，停止后重写回填）。</summary>
    private static void WriteWavHeader(BinaryWriter bw, long pcmBytes)
    {
        bw.Seek(0, SeekOrigin.Begin);
        int dataSize = (int)Math.Min(pcmBytes, int.MaxValue - 36);
        bw.Write("RIFF".ToCharArray());
        bw.Write(36 + dataSize);
        bw.Write("WAVE".ToCharArray());
        bw.Write("fmt ".ToCharArray());
        bw.Write(16);
        bw.Write((short)1);       // PCM
        bw.Write((short)1);       // 单声道
        bw.Write(SampleRate);
        bw.Write(SampleRate * 2); // 字节率 = 采样率 × 16bit
        bw.Write((short)2);       // 块对齐
        bw.Write((short)16);      // 位深
        bw.Write("data".ToCharArray());
        bw.Write(dataSize);
    }
}
#else
/// <summary>非 Android 平台占位：真实采集由 WindowsAudioMicrophoneCapture 提供（与 AudioCoexist 同模式）。</summary>
public sealed class AndroidAudioMicrophoneCapture : IMicrophoneCapture
{
    public Task<string> CaptureWaveFileAsync(TimeSpan maxDuration, CancellationToken ct = default)
        => Task.FromResult("");
    public Task<string> StopAsync() => Task.FromResult("");
    public Task CancelAsync() => Task.CompletedTask;
}
#endif
