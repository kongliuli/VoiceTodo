using System.Text.Json;
using VoiceTodo.Core.Models;

namespace VoiceTodo.Core.Services;

/// <summary>活动计时快照的一条执行段（扁平计划，Kind = work / rest / countdown）。</summary>
public sealed record SnapshotSegment(string Kind, long DurationTicks, int Round, int Rounds);

/// <summary>
/// 活动计时运行快照（running-timer.json）。进程被杀/重启后据此恢复：
/// 携带完整扁平执行段（含轮次）、当前段截止时刻与索引、暂停冻结剩余，以及可选的计划快照。
/// </summary>
public sealed class RunningTimerSnapshot
{
    public string Title { get; set; } = "";
    /// <summary>训练计划快照（口述/模板创建的训练才有；普通倒计时为 null）。</summary>
    public TrainingPlan? Plan { get; set; }
    /// <summary>展开后的执行段序列（与运行时 SchedItem 一一对应）。</summary>
    public List<SnapshotSegment> Segments { get; set; } = new();
    /// <summary>当前执行段下标。</summary>
    public int SchedIdx { get; set; }
    /// <summary>当前段截止时刻（运行中有效；暂停态忽略此字段，看 PausedRemaining）。</summary>
    public DateTimeOffset EndAt { get; set; }
    /// <summary>本次训练开始时刻（留痕用）。</summary>
    public DateTimeOffset StartedAt { get; set; }
    /// <summary>是否处于用户暂停态。</summary>
    public bool PausedByUser { get; set; }
    /// <summary>暂停冻结的当前段剩余（ticks）。</summary>
    public long PausedRemainingTicks { get; set; }
    public int Round { get; set; }
    public int Rounds { get; set; }
    /// <summary>最后一次落盘时刻（诊断用）。</summary>
    public DateTimeOffset SavedAt { get; set; }
}

/// <summary>恢复判定结果（纯计算，可单测）。</summary>
public enum RunningRecoveryKind
{
    /// <summary>无快照。</summary>
    None,
    /// <summary>当前段 EndAt 在未来 → 原位继续。</summary>
    Resume,
    /// <summary>挂起跨过若干段 → 按真实经过时间推进到新段（总时长不拉长）。</summary>
    Advance,
    /// <summary>用户暂停态 → 直接进暂停并冻结剩余。</summary>
    ResumePaused,
    /// <summary>无法对应（数据异常/训练窗已在挂起期间流尽）→ 标记中断、丢弃快照，不伪装完成。</summary>
    Interrupted
}

/// <param name="Kind">判定结果。</param>
/// <param name="SegmentIndex">恢复落点段下标。</param>
/// <param name="EndAt">恢复后当前段截止时刻（ResumePaused 为 now+冻结剩余）。</param>
/// <param name="PausedRemaining">暂停冻结剩余（仅 ResumePaused）。</param>
public sealed record RunningRecovery(
    RunningRecoveryKind Kind, int SegmentIndex, DateTimeOffset EndAt, TimeSpan PausedRemaining);

/// <summary>
/// 活动计时快照存储（DEV-05）：每秒/每阶段变更落盘 running-timer.json，
/// 临时文件 + Move 原子替换（与主库同目录、同写法）。快照属可再生的辅助数据：
/// 读失败/损坏一律按「无快照」处理，写失败静默返回 false，绝不阻塞计时运行。
/// </summary>
public class RunningTimerStore
{
    /// <summary>快照文件名常量。</summary>
    public const string DefaultFileName = "running-timer.json";

    private readonly string _filePath;
    private readonly SemaphoreSlim _sem = new(1, 1);

    public RunningTimerStore(string? directory = null, string fileName = DefaultFileName)
    {
        // 与主库同目录（LocalApplicationData/VoiceTodo），路径推导复用仓库实现
        directory ??= JsonFileTodoRepository.GetDefaultDirectory();
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, fileName);
    }

    /// <summary>落盘快照（临时文件 + 原子替换）。失败返回 false，不抛出。</summary>
    public async Task<bool> SaveAsync(RunningTimerSnapshot snapshot)
    {
        var tmpPath = _filePath + ".tmp";
        await _sem.WaitAsync();
        try
        {
            await using (var fs = File.Create(tmpPath))
            {
                await JsonSerializer.SerializeAsync(fs, snapshot);
            }
            File.Move(tmpPath, _filePath, overwrite: true);
            return true;
        }
        catch
        {
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { /* 清理失败忽略 */ }
            return false;
        }
        finally { _sem.Release(); }
    }

    /// <summary>读取快照：文件缺失/损坏/反序列化失败一律返回 null（视为无快照）。</summary>
    public async Task<RunningTimerSnapshot?> LoadAsync()
    {
        if (!File.Exists(_filePath))
            return null;
        await _sem.WaitAsync();
        try
        {
            await using var fs = File.OpenRead(_filePath);
            return await JsonSerializer.DeserializeAsync<RunningTimerSnapshot>(fs);
        }
        catch
        {
            return null;
        }
        finally { _sem.Release(); }
    }

    /// <summary>训练结束/取消/中断处理完即删除快照。失败返回 false（残留快照下次启动由恢复判定兜底）。</summary>
    public async Task<bool> DeleteAsync()
    {
        await _sem.WaitAsync();
        try
        {
            if (File.Exists(_filePath))
                File.Delete(_filePath);
            return true;
        }
        catch
        {
            return false;
        }
        finally { _sem.Release(); }
    }

    /// <summary>
    /// 恢复判定（纯函数）：now 为当前时刻。
    /// - 暂停态：直接按冻结剩余进暂停（EndAt 无意义）；
    /// - 运行态：EndAt 未到 → 原位继续；已越过 → 用真实经过时间沿执行段向前推进，
    ///   落在新段内则该段 EndAt = now +（段时长 − 已消耗），总时长不被拉长；
    ///   越过全部执行段或数据异常 → Interrupted（中断≠完成，不伪造 completed）。
    /// </summary>
    public static RunningRecovery Evaluate(RunningTimerSnapshot? snapshot, DateTimeOffset now)
    {
        if (snapshot is null)
            return new RunningRecovery(RunningRecoveryKind.None, 0, default, TimeSpan.Zero);

        var segs = snapshot.Segments;
        if (segs.Count == 0 || snapshot.SchedIdx < 0 || snapshot.SchedIdx >= segs.Count)
            return new RunningRecovery(RunningRecoveryKind.Interrupted, 0, default, TimeSpan.Zero);

        if (snapshot.PausedByUser)
        {
            var rem = TimeSpan.FromTicks(snapshot.PausedRemainingTicks);
            return rem > TimeSpan.Zero
                ? new RunningRecovery(RunningRecoveryKind.ResumePaused, snapshot.SchedIdx, now + rem, rem)
                : new RunningRecovery(RunningRecoveryKind.Interrupted, 0, default, TimeSpan.Zero);
        }

        var endAt = snapshot.EndAt;
        if (endAt - now > TimeSpan.Zero)
            return new RunningRecovery(RunningRecoveryKind.Resume, snapshot.SchedIdx, endAt, TimeSpan.Zero);

        var carry = now - endAt; // 挂起期间已流逝并越过当前段的时间
        int i = snapshot.SchedIdx;
        while (i < segs.Count - 1)
        {
            var nextDur = TimeSpan.FromTicks(segs[i + 1].DurationTicks);
            if (nextDur <= TimeSpan.Zero) // 执行段数据异常，无法对应
                return new RunningRecovery(RunningRecoveryKind.Interrupted, 0, default, TimeSpan.Zero);
            if (carry < nextDur)
                return new RunningRecovery(RunningRecoveryKind.Advance, i + 1, now + (nextDur - carry), TimeSpan.Zero);
            carry -= nextDur;
            i++;
        }
        // 训练总窗已在挂起期间流尽：如实记中断，丢弃快照
        return new RunningRecovery(RunningRecoveryKind.Interrupted, 0, default, TimeSpan.Zero);
    }
}
