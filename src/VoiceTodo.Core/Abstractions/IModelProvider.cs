using System.Globalization;
using VoiceTodo.Core.Models;

namespace VoiceTodo.Core.Abstractions;

/// <summary>
/// 模型提供方契约：将“模型种类 + 语种”解析为本地文件系统中的真实目录路径。
/// Core 的语音管线只依赖此接口，不关心模型文件如何打包 / 提取。
/// MAUI 实现负责：把随包分发的 MauiAsset 首次提取到可写目录，并返回该路径
/// （Android 资产在 APK 内压缩，原生库无法直接按路径读取，必须提取到本地）。
///
/// 多模型并存与切换：
/// - GetModels(kind) 列出某类全部候选模型；
/// - GetActive(kind)/SetActive(kind,id) 读写当前激活模型（持久化于 Preferences）；
/// - Resolve 系列按具体 ModelEntry 解析目录，便于运行时切换后自动重建语音引擎。
/// </summary>
public interface IModelProvider
{
    /// <summary>列出某类全部候选模型（含未打包的，供设置页选择）。</summary>
    IReadOnlyList<ModelEntry> GetModels(ModelKind kind);

    /// <summary>返回当前激活的模型条目（按持久化选择，回退到 IsDefault）。</summary>
    ModelEntry GetActive(ModelKind kind);

    /// <summary>设置某类激活模型。</summary>
    void SetActive(ModelKind kind, string id);

    /// <summary>按具体条目解析本地目录（必要时从包内提取）；提取后返回绝对路径。</summary>
    Task<string> ResolveModelDirectoryAsync(ModelEntry entry, CancellationToken ct = default);

    /// <summary>解析当前激活模型的目录。</summary>
    Task<string> ResolveActiveAsync(ModelKind kind, CancellationToken ct = default);

    /// <summary>具体条目是否已就绪（文件齐备）。</summary>
    Task<bool> IsModelAvailableAsync(ModelEntry entry, CancellationToken ct = default);

    /// <summary>当前激活模型是否已就绪。</summary>
    Task<bool> IsModelAvailableAsync(ModelKind kind, CancellationToken ct = default);
}
