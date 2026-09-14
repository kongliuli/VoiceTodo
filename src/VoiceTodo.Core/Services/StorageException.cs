namespace VoiceTodo.Core.Services;

/// <summary>
/// 本地存储读写失败异常：读取失败绝不降级为空库（避免损坏数据被静默丢弃）。
/// 携带丢失上下文——主库文件路径、内部异常，以及可选的损坏副本路径（读取失败时保留的现场证据）。
/// </summary>
public class StorageException : Exception
{
    /// <summary>主库文件完整路径。</summary>
    public string FilePath { get; }

    /// <summary>损坏副本路径（仅读取失败且副本创建成功时非 null；写入失败为 null）。</summary>
    public string? CorruptBackupPath { get; }

    public StorageException(string filePath, Exception innerException, string? corruptBackupPath = null)
        : base(BuildMessage(filePath, corruptBackupPath), innerException)
    {
        FilePath = filePath;
        CorruptBackupPath = corruptBackupPath;
    }

    private static string BuildMessage(string filePath, string? corruptBackupPath)
        => corruptBackupPath is null
            ? $"本地存储读写失败，主库未被破坏：{filePath}"
            : $"本地存储文件损坏，原文件已备份至 {corruptBackupPath}，未降级为空库：{filePath}";
}
