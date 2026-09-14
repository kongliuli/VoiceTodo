using System.Text;
using VoiceTodo.Core.Abstractions;
using VoiceTodo.Core.Models;
using Whisper.net;

namespace VoiceTodo.Maui.PlatformServices;

/// <summary>
/// 语音识别实现（whisper.net / whisper.cpp，端侧离线）。
/// 流程：麦克风采集 WAV → 去掉 RIFF 头后以 16kHz 16bit PCM 送入 whisper → 拼接段文本。
/// 模型由 IModelProvider 的“激活 ASR 条目”决定；whisper 使用单个 ggml 模型文件（多语）。
/// </summary>
public class WhisperSpeechRecognizer : ISpeechRecognizer
{
    private readonly IModelProvider _provider;
    private readonly IMicrophoneCapture _capture;
    private string? _modelFile;
    private WhisperFactory? _factory;
    private readonly object _sync = new();

    public WhisperSpeechRecognizer(IModelProvider provider, IMicrophoneCapture capture)
    {
        _provider = provider;
        _capture = capture;
    }

    public async Task<string> RecognizeAsync(CancellationToken ct = default)
    {
        var wavPath = await _capture.CaptureWaveFileAsync(TimeSpan.FromSeconds(8), ct);
        if (!File.Exists(wavPath))
            return string.Empty;
        return await RecognizeFileAsync(wavPath, ct);
    }

    public async Task<string> RecognizeFileAsync(string wavPath, CancellationToken ct = default)
    {
        // 读取 16kHz 16bit 单声道 PCM（跳过 44 字节 WAV 头），whisper 以流方式接收原始 PCM。
        // 使用激活条目的 Resolution（含 manifest 的 ModelFile 覆盖），不再按固定文件名硬探测。
        var entry = _provider.GetActive(ModelKind.Asr);
        var dir = await _provider.ResolveModelDirectoryAsync(entry, ct);
        var factory = GetOrCreate(dir, entry);

        using var pcm = new MemoryStream(ReadPcmBytes(wavPath));
        using var processor = factory.CreateBuilder()
            .WithLanguage("auto") // 自动语言检测，支持中文
            .Build();

        var sb = new StringBuilder();
        var segments = processor.ProcessAsync(pcm, ct);
        await foreach (var seg in segments)
            sb.Append(seg.Text);

        return sb.ToString().Trim();
    }

    private WhisperFactory GetOrCreate(string dir, ModelEntry entry)
    {
        var modelFile = ResolveModelFile(dir, entry);
        if (_factory is not null && _modelFile == modelFile)
            return _factory;

        lock (_sync)
        {
            if (_factory is not null && _modelFile == modelFile)
                return _factory;

            _factory?.Dispose();
            _factory = WhisperFactory.FromPath(modelFile);
            _modelFile = modelFile;
            return _factory;
        }
    }

    /// <summary>
    /// 在模型目录中定位 ggml 模型文件（DEV：对齐 manifest 的 ModelFile 配置）：
    /// 1) manifest 条目 Options["ModelFile"]（如 ggml-tiny.en.bin）——声明即以此为准；
    /// 2) 条目 Files 中声明的 ggml-*.bin；
    /// 3) 目录内任意 ggml-*.bin（含 .en 变体）；
    /// 4) 兜底回退已配置名 / ggml-tiny.bin（让 WhisperFactory 抛出明确错误，不静默错文件）。
    /// </summary>
    private static string ResolveModelFile(string dir, ModelEntry entry)
    {
        var configured = entry.Options is { } opts && opts.TryGetValue("ModelFile", out var c) ? c?.Trim() : null;
        if (!string.IsNullOrEmpty(configured))
        {
            var p = Path.Combine(dir, configured);
            if (File.Exists(p)) return p;
        }

        foreach (var f in entry.Files)
        {
            if (f.StartsWith("ggml", StringComparison.OrdinalIgnoreCase)
                && f.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
            {
                var p = Path.Combine(dir, f);
                if (File.Exists(p)) return p;
            }
        }

        if (Directory.Exists(dir))
        {
            var any = Directory.EnumerateFiles(dir, "ggml-*.bin", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (any is not null) return any;
        }

        return Path.Combine(dir, !string.IsNullOrEmpty(configured) ? configured : "ggml-tiny.bin");
    }

    private static byte[] ReadPcmBytes(string wavPath)
    {
        var bytes = File.ReadAllBytes(wavPath);
        // 标准 RIFF/WAVE 头为 44 字节；若文件更小则原样返回（whisper 会忽略）
        return bytes.Length > 44 ? bytes[44..] : bytes;
    }
}