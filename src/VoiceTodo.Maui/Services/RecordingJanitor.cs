using System.Globalization;
using System.IO;
using Microsoft.Maui.Storage;

namespace VoiceTodo.Maui.Services;

/// <summary>
/// 录音文件清理（A5）：App 启动时 fire-and-forget 触发，清理 <c>captures/</c> 下修改时间早于 7 天的 <c>.wav</c>。
/// 判定以文件 LastWriteTime 为准；文件名 <c>capture-yyyyMMdd-HHmmss.wav</c> 作为兜底（取不到时间戳时解析）。
/// 整个操作 try/catch 吞异常，绝不阻塞启动；无法判定时间的文件一律保留，避免误删。
/// 双平台输出目录一致（均使用 <c>FileSystem.AppDataDirectory/captures</c>），此处统一清理。
/// </summary>
public static class RecordingJanitor
{
    /// <summary>保留时长上限：超过即清理。</summary>
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

    /// <summary>清理过期录音（同步、自吞异常，供 Task.Run 调用）。</summary>
    public static void CleanupOld()
    {
        try
        {
            var dir = Path.Combine(FileSystem.AppDataDirectory, "captures");
            if (!Directory.Exists(dir)) return;
            var cutoff = DateTime.Now - MaxAge;
            foreach (var file in Directory.GetFiles(dir, "*.wav", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    if (IsExpired(file, cutoff))
                        File.Delete(file);
                }
                catch
                {
                    // 单个文件删除失败忽略，继续下一个
                }
            }
        }
        catch
        {
            // 整个清理失败（目录不可读等）也不影响启动
        }
    }

    /// <summary>是否过期：优先用文件修改时间；取不到则按文件名时间戳兜底；都无法判定 → 保留。</summary>
    private static bool IsExpired(string file, DateTime cutoff)
    {
        try
        {
            if (File.GetLastWriteTime(file) <= cutoff)
                return true;
        }
        catch
        {
            // 取不到时间戳 → 退回文件名解析
        }

        var name = Path.GetFileNameWithoutExtension(file);
        if (name.StartsWith("capture-", StringComparison.Ordinal) &&
            DateTime.TryParseExact(name["capture-".Length..], "yyyyMMdd-HHmmss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return parsed <= cutoff;
        }
        return false; // 无法判定的一律保留，避免误删
    }
}
