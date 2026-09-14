using System.Globalization;
using System.Text.Json;
using VoiceTodo.Core.Abstractions;

namespace VoiceTodo.Core.Models;

/// <summary>单个模型条目的元数据。Core 与下载/打包脚本共用同一份结构。</summary>
public sealed class ModelEntry
{
    /// <summary>全局唯一 ID，用于运行时切换与打包清单引用（如 "asr-sensevoice-int8"）。</summary>
    public string Id { get; set; } = "";

    /// <summary>展示名（设置页选择器用）。</summary>
    public string Name { get; set; } = "";

    /// <summary>模型种类。</summary>
    public ModelKind Kind { get; set; }

    /// <summary>语种（如 "zh" / "en"）；null 表示语种无关（多语 ASR 如 SenseVoice、双语 TTS 如 Melo）。</summary>
    public string? Culture { get; set; }

    /// <summary>sherpa-onnx 引擎标识，决定如何实例化：sense-voice / whisper / paraformer / moonshine / vits / kokoro。</summary>
    public string Engine { get; set; } = "";

    /// <summary>相对 Models/ 根目录的路径，如 "asr/sensevoice-int8"。</summary>
    public string RelativePath { get; set; } = "";

    /// <summary>该模型包含的（顶层）文件名列表，用于就绪校验；子目录由 .filelist 处理。</summary>
    public List<string> Files { get; set; } = new();

    /// <summary>逐文件下载基址（下载脚本用）。实际 URL = DownloadBaseUrl + 文件名。</summary>
    public string? DownloadBaseUrl { get; set; }

    /// <summary>发布包地址（release tarball/zip）。若提供，下载脚本会下载并解包，而非逐文件下载。</summary>
    public string? ArchiveUrl { get; set; }

    /// <summary>大致体积（字节），仅用于展示 / 校验。</summary>
    public long SizeBytes { get; set; }

    /// <summary>同类模型中的默认激活项。</summary>
    public bool IsDefault { get; set; }

    /// <summary>是否随应用包分发（打包阶段据此复制；未勾选的仅作候选，可后续打包或运行时下载）。</summary>
    public bool PackInBuild { get; set; }

    /// <summary>引擎相关附加选项，如 ModelFile 覆盖、Lang、NumThreads 等。</summary>
    public Dictionary<string, string>? Options { get; set; } = new();
}

/// <summary>
/// 模型清单（目录）。应用内嵌的 manifest.json 反序列化后即为完整候选目录；
/// 其中 PackInBuild 标记哪些随包分发，运行时可在设置页切换“激活模型”。
/// 更重 / 更好的模型保持并存于 ModelsLibrary，通过切换打包或运行时下载启用。
/// </summary>
public sealed class ModelManifest
{
    public List<ModelEntry> Entries { get; set; } = new();

    /// <summary>兜底目录（仅当应用内嵌 manifest.json 缺失时使用，保证至少能枚举到推荐模型）。</summary>
    public static ModelManifest Default => new()
    {
        Entries =
        {
            new ModelEntry
            {
                Id = "asr-sensevoice-int8",
                Name = "SenseVoice small (int8, 中英日韩粤)",
                Kind = ModelKind.Asr,
                Culture = null,
                Engine = "sense-voice",
                RelativePath = "asr/sensevoice-int8",
                Files = { "model.int8.onnx", "tokens.txt" },
                SizeBytes = 240_000_000,
                IsDefault = true,
                PackInBuild = true,
                Options = { ["ModelFile"] = "model.int8.onnx" },
            },
            new ModelEntry
            {
                Id = "tts-melo-zh-en",
                Name = "VITS-Melo (中英双语)",
                Kind = ModelKind.Tts,
                Culture = null,
                Engine = "vits",
                RelativePath = "tts/melo-zh-en",
                Files = { "model.onnx", "tokens.txt" },
                SizeBytes = 80_000_000,
                IsDefault = true,
                PackInBuild = true,
            },
        }
    };
}
