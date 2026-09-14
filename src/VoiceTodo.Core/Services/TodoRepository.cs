using System.Text.Json;
using VoiceTodo.Core.Abstractions;
using VoiceTodo.Core.Models;

namespace VoiceTodo.Core.Services;

/// <summary>
/// 基于 JSON 文件的本地存储实现（D 类功能：离线持久化）。
/// 纯 .NET，无原生依赖，便于单测；生产环境可替换为 SQLite 实现（同接口，即插即用）。
/// 读取失败不降级为空库（抛 StorageException 并保留损坏副本）；写入经临时文件原子替换。
/// </summary>
public class JsonFileTodoRepository : ITodoRepository
{
    /// <summary>主库文件名常量，供样例种子等外部逻辑对齐路径。</summary>
    public const string MainFileName = "voicetodo.json";

    private readonly string _filePath;
    private readonly SemaphoreSlim _sem = new(1, 1);

    /// <summary>默认存储目录（LocalApplicationData/VoiceTodo），供样例初始化等场景推导主库路径。</summary>
    public static string GetDefaultDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoiceTodo");

    /// <summary>directory 为存储目录；fileName 默认主库 voicetodo.json，可传独立文件名实现库隔离。</summary>
    public JsonFileTodoRepository(string? directory = null, string fileName = MainFileName)
    {
        directory ??= GetDefaultDirectory();
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, fileName);
    }

    private record Store
    {
        public int NextId { get; set; } = 1;
        public List<TodoItem> Todos { get; set; } = new();
        public List<TimerItem> Timers { get; set; } = new();
        public List<TimerSession> Sessions { get; set; } = new();
    }

    private async Task<Store> LoadAsync()
    {
        if (!File.Exists(_filePath))
            return new Store(); // 首次安装：无库文件属正常空库
        try
        {
            await using var fs = File.OpenRead(_filePath);
            var store = await JsonSerializer.DeserializeAsync<Store>(fs)
                ?? throw new JsonException("库文件内容为空或不是有效的存储 JSON。");
            return store;
        }
        catch (Exception ex)
        {
            // 读取失败绝不返回空 Store（避免损坏被误当空库、随后写回覆盖真实数据）：
            // 先把原文件复制为带时间戳的损坏副本保留证据，再抛 StorageException 向上传播。
            string? corruptPath = null;
            var candidate = _filePath + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss");
            try
            {
                File.Copy(_filePath, candidate, overwrite: true);
                corruptPath = candidate;
            }
            catch
            {
                // 副本创建失败不阻塞抛出；清理可能残留的半截副本
                try { if (File.Exists(candidate)) File.Delete(candidate); } catch { /* 忽略 */ }
            }
            throw new StorageException(_filePath, ex, corruptPath);
        }
    }

    private async Task SaveAsync(Store s)
    {
        var tmpPath = _filePath + ".tmp";
        try
        {
            // 先写同目录临时文件，再原子替换主库，避免写一半损坏原件
            await using (var fs = File.Create(tmpPath))
            {
                await JsonSerializer.SerializeAsync(fs, s, new JsonSerializerOptions { WriteIndented = true });
            }
            File.Move(tmpPath, _filePath, overwrite: true);
        }
        catch (Exception ex)
        {
            // 任何失败都不静默：清理临时文件后向上抛出，主库保持上次完好内容
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { /* 清理失败忽略 */ }
            throw new StorageException(_filePath, ex);
        }
    }

    public async Task<int> AddTodoAsync(TodoItem item)
    {
        await _sem.WaitAsync();
        try
        {
            var s = await LoadAsync();
            item.Id = s.NextId++;
            s.Todos.Add(item);
            await SaveAsync(s);
            return item.Id;
        }
        finally { _sem.Release(); }
    }

    public async Task<int> AddTimerAsync(TimerItem item)
    {
        await _sem.WaitAsync();
        try
        {
            var s = await LoadAsync();
            item.Id = s.NextId++;
            s.Timers.Add(item);
            await SaveAsync(s);
            return item.Id;
        }
        finally { _sem.Release(); }
    }

    public async Task UpdateTodoAsync(TodoItem item)
    {
        await _sem.WaitAsync();
        try
        {
            var s = await LoadAsync();
            var idx = s.Todos.FindIndex(t => t.Id == item.Id);
            if (idx >= 0) s.Todos[idx] = item;
            await SaveAsync(s);
        }
        finally { _sem.Release(); }
    }

    public async Task UpdateTimerAsync(TimerItem item)
    {
        await _sem.WaitAsync();
        try
        {
            var s = await LoadAsync();
            var idx = s.Timers.FindIndex(t => t.Id == item.Id);
            if (idx >= 0) s.Timers[idx] = item;
            await SaveAsync(s);
        }
        finally { _sem.Release(); }
    }

    public async Task<List<TodoItem>> GetTodosAsync()
    {
        var s = await LoadAsync();
        return s.Todos;
    }

    public async Task<List<TimerItem>> GetTimersAsync()
    {
        var s = await LoadAsync();
        return s.Timers;
    }

    public async Task MarkDoneAsync(int id)
        => await SetTodoDoneAsync(id, true);

    public async Task SetTodoDoneAsync(int id, bool done)
    {
        await _sem.WaitAsync();
        try
        {
            var s = await LoadAsync();
            var item = s.Todos.FirstOrDefault(t => t.Id == id);
            if (item is not null) item.IsDone = done;
            await SaveAsync(s);
        }
        finally { _sem.Release(); }
    }

    public async Task DeleteTodoAsync(int id)
    {
        await _sem.WaitAsync();
        try
        {
            var s = await LoadAsync();
            s.Todos.RemoveAll(t => t.Id == id);
            await SaveAsync(s);
        }
        finally { _sem.Release(); }
    }

    public async Task DeleteTimerAsync(int id)
    {
        await _sem.WaitAsync();
        try
        {
            var s = await LoadAsync();
            s.Timers.RemoveAll(t => t.Id == id);
            await SaveAsync(s);
        }
        finally { _sem.Release(); }
    }

    public async Task<int> AddSessionAsync(TimerSession session)
    {
        await _sem.WaitAsync();
        try
        {
            var s = await LoadAsync();
            session.Id = s.NextId++;
            s.Sessions.Add(session);
            await SaveAsync(s);
            return session.Id;
        }
        finally { _sem.Release(); }
    }

    public async Task<List<TimerSession>> GetSessionsAsync()
    {
        var s = await LoadAsync();
        return s.Sessions;
    }

    public async Task UpdateSessionAsync(TimerSession session)
    {
        await _sem.WaitAsync();
        try
        {
            var s = await LoadAsync();
            var idx = s.Sessions.FindIndex(t => t.Id == session.Id);
            if (idx >= 0) s.Sessions[idx] = session;
            await SaveAsync(s);
        }
        finally { _sem.Release(); }
    }
}
