using System.Text.Json;
using VoiceTodo.Core.Abstractions;
using VoiceTodo.Core.Models;
using Microsoft.Maui.Storage;

namespace VoiceTodo.Maui.Services;

/// <summary>
/// 数据导出 / 导入（C2）：把待办、计时与运行记录打包成带版本头的 JSON 文件，
/// 便于用户把数据带走或在设备间迁移。导入不沿用旧 Id（由仓库重新分配）。
/// 导入走 <see cref="ITodoRepository"/> 直接落库（批量搬运，刻意不触发提醒调度）；
/// 读取/解析失败一律降级返回 0 + 写日志，绝不抛异常让 App 崩。
/// </summary>
public sealed class TodoBackupService
{
    private readonly ITodoRepository _repo;

    public TodoBackupService(ITodoRepository repo) => _repo = repo;

    /// <summary>导出当前全部数据到 CacheDirectory，返回文件路径。</summary>
    public async Task<string> ExportAsync()
    {
        var payload = new BackupPayload
        {
            Schema = 1,
            ExportedAt = DateTimeOffset.Now,
            Todos = await _repo.GetTodosAsync(),
            Timers = await _repo.GetTimersAsync(),
            Sessions = await _repo.GetSessionsAsync(),
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
        var path = Path.Combine(FileSystem.CacheDirectory, $"voicetodo-backup-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, json);
        return path;
    }

    /// <summary>
    /// 从备份文件导入：逐条 Add*（Id 重分配）。返回成功条数。
    /// 文件不存在 / 读取失败 / 解析失败返回 (0,0)；单条失败跳过并记日志。
    /// </summary>
    public async Task<(int todos, int timers)> ImportAsync(string jsonPath)
    {
        if (string.IsNullOrWhiteSpace(jsonPath) || !File.Exists(jsonPath))
            return (0, 0);

        string json;
        try
        {
            json = await File.ReadAllTextAsync(jsonPath);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"备份文件读取失败: {ex.Message}");
            return (0, 0);
        }

        BackupPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<BackupPayload>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            AppLog.Warn($"备份文件解析失败: {ex.Message}");
            return (0, 0);
        }

        if (payload is null) return (0, 0);

        int addedTodos = 0, addedTimers = 0;
        foreach (var t in payload.Todos ?? Enumerable.Empty<TodoItem>())
        {
            try { await _repo.AddTodoAsync(t); addedTodos++; }
            catch (Exception ex) { AppLog.Warn($"待办导入失败(已跳过): {ex.Message}"); }
        }
        foreach (var tm in payload.Timers ?? Enumerable.Empty<TimerItem>())
        {
            try { await _repo.AddTimerAsync(tm); addedTimers++; }
            catch (Exception ex) { AppLog.Warn($"计时导入失败(已跳过): {ex.Message}"); }
        }
        foreach (var s in payload.Sessions ?? Enumerable.Empty<TimerSession>())
        {
            try { await _repo.AddSessionAsync(s); }
            catch (Exception ex) { AppLog.Warn($"记录导入失败(已跳过): {ex.Message}"); }
        }
        return (addedTodos, addedTimers);
    }

    private sealed class BackupPayload
    {
        public int Schema { get; set; }
        public DateTimeOffset ExportedAt { get; set; }
        public List<TodoItem>? Todos { get; set; }
        public List<TimerItem>? Timers { get; set; }
        public List<TimerSession>? Sessions { get; set; }
    }
}
