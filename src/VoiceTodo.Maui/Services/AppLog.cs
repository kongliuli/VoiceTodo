using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Maui.Storage;

namespace VoiceTodo.Maui.Services;

/// <summary>
/// 轻量诊断日志地基（C12）：把关键路径的故障写成本地日志文件，供排障与分享。
/// 所有 IO 均包在 try/catch 内且失败绝不抛出——日志本身绝不能成为新的崩溃源。
/// 行格式：[yyyy-MM-dd HH:mm:ss.fff][INFO|WARN|ERROR] message（异常追加 \n{ex}）。
/// </summary>
public static class AppLog
{
    /// <summary>日志目录（AppData/logs）。</summary>
    public static string LogDirectory => Path.Combine(FileSystem.AppDataDirectory, "logs");

    private static string TodayFile => Path.Combine(LogDirectory, $"app-{DateTime.Now:yyyyMMdd}.log");

    private static readonly object Gate = new();

    static AppLog()
    {
        try { EnsureDir(); CleanupOld(); }
        catch { /* 静态构造失败也绝不抛 */ }
    }

    private static void EnsureDir()
    {
        var dir = LogDirectory;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
    }

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message) => Write("WARN", message, null);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            EnsureDir();
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}][{level}] {message}";
            if (ex is not null) line += $"\n{ex}";
            lock (Gate)
            {
                File.AppendAllText(TodayFile, line + Environment.NewLine);
            }
        }
        catch { /* 日志写失败不影响宿主 */ }
    }

    /// <summary>删除 logs/ 下超过 keepDays 天的 app-*.log（默认 7 天），全 try/catch。</summary>
    public static void CleanupOld(int keepDays = 7)
    {
        try
        {
            var dir = LogDirectory;
            if (!Directory.Exists(dir)) return;
            var cutoff = DateTime.Now.AddDays(-keepDays);
            foreach (var f in Directory.GetFiles(dir, "app-*.log"))
            {
                try
                {
                    if (new FileInfo(f).CreationTime < cutoff) File.Delete(f);
                }
                catch { /* 单文件失败忽略，继续清理其余 */ }
            }
        }
        catch { /* 清理失败不影响宿主 */ }
    }

    /// <summary>把全部日志合并导出到 CacheDirectory，返回可分享路径；无日志或失败返回 null。</summary>
    public static Task<string?> ExportAsync()
    {
        try
        {
            var dir = LogDirectory;
            if (!Directory.Exists(dir)) return Task.FromResult<string?>(null);
            var files = Directory.GetFiles(dir, "app-*.log").OrderBy(f => f).ToArray();
            if (files.Length == 0) return Task.FromResult<string?>(null);

            var dest = Path.Combine(FileSystem.CacheDirectory, $"voicetodo-logs-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            var separator = Encoding.UTF8.GetBytes(Environment.NewLine);
            using var outStream = File.Create(dest);
            foreach (var f in files)
            {
                try
                {
                    using var inStream = File.OpenRead(f);
                    inStream.CopyTo(outStream);
                    outStream.Write(separator, 0, separator.Length);
                }
                catch { /* 单文件失败忽略 */ }
            }
            return Task.FromResult<string?>(dest);
        }
        catch (Exception ex)
        {
            Warn($"日志导出失败: {ex.Message}");
            return Task.FromResult<string?>(null);
        }
    }
}
