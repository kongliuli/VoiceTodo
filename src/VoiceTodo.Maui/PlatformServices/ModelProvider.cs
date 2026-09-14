using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Maui.Storage;
using VoiceTodo.Core.Abstractions;
using VoiceTodo.Core.Models;

namespace VoiceTodo.Maui.PlatformServices;

/// <summary>
/// IModelProvider 的 MAUI 实现。
/// 模型文件作为 MauiAsset 随包分发，首次使用时提取到
/// FileSystem.AppDataDirectory/Models/&lt;RelativePath&gt;，再返回该真实路径给 sherpa-onnx。
///
/// 多模型并存：清单（manifest.json）列出全部候选；运行时可在设置页切换“激活模型”。
/// 切换打包：仅 PackInBuild=true 的模型物理存在于包内；其余模型若被激活但包内缺失，
/// IsModelAvailable 返回 false，提示需打包或运行时下载。
/// </summary>
public sealed class ModelProvider : IModelProvider
{
    private readonly ModelManifest _manifest;
    private static readonly string BaseDir = Path.Combine(FileSystem.AppDataDirectory, "Models");

    public ModelProvider(ModelManifest? manifest = null)
    {
        _manifest = manifest ?? LoadManifest() ?? ModelManifest.Default;
    }

    public IReadOnlyList<ModelEntry> GetModels(ModelKind kind)
        => _manifest.Entries.Where(e => e.Kind == kind).ToList();

    public ModelEntry GetActive(ModelKind kind)
    {
        var list = GetModels(kind);
        var id = Preferences.Default.Get(ActiveKey(kind), "");
        var entry = list.FirstOrDefault(e => e.Id == id)
                    ?? list.FirstOrDefault(e => e.IsDefault)
                    ?? list.FirstOrDefault();
        if (entry is null)
            throw new InvalidOperationException($"未注册任何 {kind} 模型，请检查 manifest.json。");
        return entry;
    }

    public void SetActive(ModelKind kind, string id)
    {
        var entry = GetModels(kind).FirstOrDefault(e => e.Id == id);
        if (entry is null) return;
        Preferences.Default.Set(ActiveKey(kind), id);
    }

    public async Task<string> ResolveModelDirectoryAsync(ModelEntry entry, CancellationToken ct = default)
    {
        var targetDir = Path.Combine(BaseDir, entry.RelativePath);
        await EnsureExtractedAsync(entry, targetDir, ct);
        return targetDir;
    }

    public Task<string> ResolveActiveAsync(ModelKind kind, CancellationToken ct = default)
        => ResolveModelDirectoryAsync(GetActive(kind), ct);

    public Task<bool> IsModelAvailableAsync(ModelEntry entry, CancellationToken ct = default)
    {
        var targetDir = Path.Combine(BaseDir, entry.RelativePath);
        var ok = Directory.Exists(targetDir) && entry.Files.All(f => File.Exists(Path.Combine(targetDir, f)));
        return Task.FromResult(ok);
    }

    public Task<bool> IsModelAvailableAsync(ModelKind kind, CancellationToken ct = default)
        => IsModelAvailableAsync(GetActive(kind), ct);

    // ---- 内部 ----

    private static string ActiveKey(ModelKind kind) => $"model.active.{kind}";

    private async Task EnsureExtractedAsync(ModelEntry entry, string targetDir, CancellationToken ct)
    {
        var marker = Path.Combine(targetDir, ".extracted");
        if (Directory.Exists(targetDir) && File.Exists(marker))
            return;

        Directory.CreateDirectory(targetDir);

        var relativeFiles = await LoadFileListAsync(entry) ?? entry.Files;
        foreach (var rel in relativeFiles)
        {
            var srcNames = CandidateAssetNames(entry.RelativePath, rel);
            await using var src = await OpenFirstAvailableAsync(srcNames, ct);
            var dest = Path.Combine(targetDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            await using var dst = File.Create(dest);
            await src.CopyToAsync(dst, ct);
        }

        await File.WriteAllTextAsync(marker, DateTime.UtcNow.ToString("O"), ct);
    }

    /// <summary>读取打包阶段生成的 .filelist（含子目录），以便完整提取整棵模型目录。</summary>
    private static async Task<List<string>?> LoadFileListAsync(ModelEntry entry)
    {
        var candidates = CandidateAssetNames(entry.RelativePath, ".filelist");
        foreach (var name in candidates)
        {
            try
            {
                await using var s = await FileSystem.OpenAppPackageFileAsync(name);
                using var reader = new StreamReader(s);
                var text = await reader.ReadToEndAsync();
                var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                                .Select(l => l.Trim())
                                .Where(l => l.Length > 0)
                                .ToList();
                return lines.Count > 0 ? lines : null;
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        return null;
    }

    private static async Task<Stream> OpenFirstAvailableAsync(IEnumerable<string> names, CancellationToken ct)
    {
        // 1. 优先走 MAUI 包内资产（Android assets / Windows 打包内容）。
        foreach (var name in names)
        {
            try
            {
                return await FileSystem.OpenAppPackageFileAsync(name);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            catch (Exception) when (!ct.IsCancellationRequested) { }
        }

        // 2. 回退到输出目录/部署目录的磁盘文件（Windows unpackaged 场景，
        //    MauiAsset 可能未被拷进包内容，但模型已随 Content 一并部署）。
        foreach (var name in names)
        {
            var disk = Path.Combine(AppContext.BaseDirectory, name);
            try
            {
                if (File.Exists(disk))
                    return File.OpenRead(disk);
            }
            catch { /* 继续尝试下一个候选。 */ }
        }

        throw new FileNotFoundException($"未在应用包中找到模型资产，候选路径：{string.Join(", ", names)}");
    }

    private static IEnumerable<string> CandidateAssetNames(string relativePath, string file)
    {
        var rel = relativePath.Replace('\\', '/').Trim('/');
        var f = file.Replace('\\', '/').Trim('/');
        yield return $"Models/{rel}/{f}";
        yield return $"Resources/Models/{rel}/{f}";
        yield return $"{rel}/{f}";
    }

    private static ModelManifest? LoadManifest()
    {
        var candidates = new[] { "Models/manifest.json", "Resources/Models/manifest.json", "manifest.json" };
        foreach (var c in candidates)
        {
            try
            {
                using var s = FileSystem.OpenAppPackageFileAsync(c).GetAwaiter().GetResult();
                using var reader = new StreamReader(s);
                var json = reader.ReadToEnd();
                // manifest.json 用字符串枚举（如 "Asr"），需 JsonStringEnumConverter 才能反序列化为 ModelKind。
                var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
                var m = JsonSerializer.Deserialize<ModelManifest>(json, options);
                if (m is { Entries.Count: > 0 }) return m;
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        return null;
    }
}
