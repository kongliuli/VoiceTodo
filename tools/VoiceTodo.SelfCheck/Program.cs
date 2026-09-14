using System.Globalization;
using VoiceTodo.Core.Abstractions;
using VoiceTodo.Core.Models;
using VoiceTodo.Core.Services;

// VoiceTodo 回归自检（可运行、离线、无外部包）。
// 覆盖本批修复涉及的非平凡逻辑：训练计划、中文口语时间解析、解析器边界、
// 通知 ID 派生与 Nag 策略、运行快照恢复、仓库损坏读/原子写、提醒闭环调度决策。
// 用法：dotnet run --project tools/VoiceTodo.SelfCheck ；全部通过退出码 0，任一失败退出码 1。

var runner = new CheckRunner();

// ───────────────────────── 1. 训练计划模型 ─────────────────────────
runner.Case("TrainingPlan: 深蹲 10×5s + 9×10s = 02:20（末组不休息）", () =>
{
    var plan = new TrainingPlan
    {
        ActionName = "深蹲",
        Rounds = 10,
        WorkDuration = TimeSpan.FromSeconds(5),
        RestDuration = TimeSpan.FromSeconds(10),
        RestAfterLastRound = false
    };
    runner.Equal(TimeSpan.FromSeconds(140), plan.TotalDuration, "总时长");
    var phases = plan.Expand();
    runner.Equal(2, phases.Count, "展开段数");
    runner.Equal(10, phases[0].Rounds, "work 轮次");
    runner.Equal(9, phases[1].Rounds, "rest 轮次（末组无休）");
    runner.True(plan.IsComplete, "完整计划");
});

runner.Case("TrainingPlan: 末组休息开启 = 02:30", () =>
{
    var plan = new TrainingPlan
    {
        ActionName = "深蹲", Rounds = 10,
        WorkDuration = TimeSpan.FromSeconds(5), RestDuration = TimeSpan.FromSeconds(10),
        RestAfterLastRound = true
    };
    runner.Equal(TimeSpan.FromSeconds(150), plan.TotalDuration, "总时长");
    runner.Equal(10, plan.Expand()[1].Rounds, "rest 轮次（含末组）");
});

// ───────────────────────── 2. 中文口语时间解析 ─────────────────────────
var cn = new ChineseTimeParser();

runner.Case("ChineseTimeParser: 五分钟后 ≈ now+5min", () =>
{
    var got = cn.ParseRelative("五分钟后提醒我");
    runner.NotNull(got, "解析结果");
    var delta = got!.Value - DateTimeOffset.Now;
    runner.True(delta > TimeSpan.FromMinutes(4) && delta < TimeSpan.FromMinutes(6), $"间隔={delta}");
});

runner.Case("ChineseTimeParser: 半小时 = 30 分钟", () =>
{
    runner.True(cn.TryParseDuration("半小时", out var d), "可解析");
    runner.Equal(TimeSpan.FromMinutes(30), d, "时长");
});

runner.Case("ChineseTimeParser: 十五分钟 = 15 分钟", () =>
{
    runner.True(cn.TryParseDuration("十五分钟", out var d), "可解析");
    runner.Equal(TimeSpan.FromMinutes(15), d, "时长");
});

runner.Case("ChineseTimeParser: 半小时后 ≈ now+30min", () =>
{
    var got = cn.ParseRelative("半小时后叫我");
    runner.NotNull(got, "解析结果");
    var delta = got!.Value - DateTimeOffset.Now;
    runner.True(delta > TimeSpan.FromMinutes(29) && delta < TimeSpan.FromMinutes(31), $"间隔={delta}");
});

runner.Case("ChineseTimeParser: 阿拉伯数字仍可用（5分钟后）", () =>
{
    var got = cn.ParseRelative("5分钟后");
    runner.NotNull(got, "解析结果");
    var delta = got!.Value - DateTimeOffset.Now;
    runner.True(delta > TimeSpan.FromMinutes(4) && delta < TimeSpan.FromMinutes(6), $"间隔={delta}");
});

// ───────────────────────── 3. 解析器边界 ─────────────────────────
ITimeParser composite = new CompositeTimeParser(new ITimeParser[] { new ChineseTimeParser(), new EnglishTimeParser() });
var intent = new RuleBasedIntentParser(composite);

runner.Case("IntentParser: 深蹲10组/每组5秒/休息10秒 → 完整计划", () =>
{
    var cmd = intent.Parse("深蹲10组，每组持续5秒钟，休息10秒");
    runner.NotNull(cmd.Plan, "产出计划");
    runner.True(cmd.Plan!.IsComplete, "计划完整");
    runner.Equal(10, cmd.Plan.Rounds, "组数");
    runner.Equal(TimeSpan.FromSeconds(5), cmd.Plan.WorkDuration, "每组运动");
    runner.Equal(TimeSpan.FromSeconds(10), cmd.Plan.RestDuration, "休息");
    runner.Equal(0, cmd.MissingSlots.Count, "无缺槽");
});

runner.Case("IntentParser: 「10 次」≠「10 组」，进 MissingSlots(count)", () =>
{
    var cmd = intent.Parse("深蹲10次");
    runner.True(cmd.MissingSlots.Contains(RuleBasedIntentParser.SlotCount), "计次提示");
    runner.True(cmd.Plan is null || !cmd.Plan.IsComplete, "不得视为完整计划");
});

runner.Case("IntentParser: 否定修正「不是10组，是8组」→ 8 组", () =>
{
    var cmd = intent.Parse("深蹲不是10组，是8组，每组5秒");
    runner.NotNull(cmd.Plan, "产出计划");
    runner.Equal(8, cmd.Plan!.Rounds, "修正后的组数");
});

runner.Case("IntentParser: 疑问句不触发创建", () =>
{
    var cmd = intent.Parse("深蹲10组每组5秒怎么安排？");
    runner.True(cmd.Plan is null, "疑问句不产出计划");
});

runner.Case("IntentParser: 「五分钟后提醒我X」→ Todo + 提醒时刻（非倒计时）", () =>
{
    var cmd = intent.Parse("五分钟后提醒我关火");
    runner.Equal(CommandType.Todo, cmd.Type, "提醒词优先于时长词");
    runner.NotNull(cmd.TriggerAt, "相对时间");
});

runner.Case("IntentParser: 「五分钟倒计时」→ Timer + 时长", () =>
{
    var cmd = intent.Parse("五分钟倒计时");
    runner.Equal(CommandType.Timer, cmd.Type, "显式定时器词");
    runner.Equal(TimeSpan.FromMinutes(5), cmd.Duration, "时长");
});

// ───────────────────────── 4. 通知 ID 派生与 Nag 策略 ─────────────────────────
runner.Case("NotificationId: 派生槽位互不冲突", () =>
{
    runner.Equal(30, NotificationId.Main(3), "Main");
    runner.Equal(31, NotificationId.PreAlert(3), "PreAlert");
    runner.Equal(32, NotificationId.Nag(3), "Nag");
    runner.True(NotificationId.Main(4) > NotificationId.Nag(3), "实体间不重叠");
});

runner.Case("NagPolicy: 截止取次数与时长先到者", () =>
{
    var due = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
    runner.Equal(due + TimeSpan.FromMinutes(50), NagPolicy.Deadline(due), "10×5min < 60min → 50min");
    runner.Equal(due + NagPolicy.Interval, NagPolicy.FirstFire(due), "首次触发=Due+间隔");
});

// ───────────────────────── 5. 运行快照恢复 ─────────────────────────
runner.Case("RunningTimerStore.Evaluate: Resume / Advance / ResumePaused / Interrupted", () =>
{
    var now = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
    var segs = new List<SnapshotSegment>
    {
        new("work", TimeSpan.FromSeconds(30).Ticks, 1, 2),
        new("rest", TimeSpan.FromSeconds(10).Ticks, 1, 2),
        new("work", TimeSpan.FromSeconds(30).Ticks, 2, 2),
        new("rest", TimeSpan.FromSeconds(10).Ticks, 2, 2)
    };
    var baseSnap = new RunningTimerSnapshot
    {
        Title = "深蹲", Segments = segs, SchedIdx = 0, StartedAt = now, EndAt = now.AddSeconds(30)
    };

    var resume = RunningTimerStore.Evaluate(baseSnap, now.AddSeconds(10));
    runner.Equal(RunningRecoveryKind.Resume, resume.Kind, "EndAt 未到 → Resume");

    var advance = RunningTimerStore.Evaluate(baseSnap, now.AddSeconds(35)); // 越过 work(0~30) 落 rest(30~40)
    runner.Equal(RunningRecoveryKind.Advance, advance.Kind, "跨段 → Advance");
    runner.Equal(1, advance.SegmentIndex, "落点段=rest");
    runner.Equal(now.AddSeconds(40), advance.EndAt, "新段 EndAt 按真实经过时间（总窗不拉长）");

    var skip = RunningTimerStore.Evaluate(baseSnap, now.AddSeconds(45)); // 再越过 rest(10) 落 work2(40~70)
    runner.Equal(RunningRecoveryKind.Advance, skip.Kind, "连跨两段 → Advance");
    runner.Equal(2, skip.SegmentIndex, "落点段=work2");
    runner.Equal(now.AddSeconds(70), skip.EndAt, "连跨两段仍按真实经过时间");

    var paused = new RunningTimerSnapshot
    {
        Title = "深蹲", Segments = segs, SchedIdx = 1,
        PausedByUser = true, PausedRemainingTicks = TimeSpan.FromSeconds(7).Ticks, StartedAt = now, EndAt = now
    };
    var resumePaused = RunningTimerStore.Evaluate(paused, now);
    runner.Equal(RunningRecoveryKind.ResumePaused, resumePaused.Kind, "暂停态");
    runner.Equal(TimeSpan.FromSeconds(7), resumePaused.PausedRemaining, "冻结剩余");

    var overdue = RunningTimerStore.Evaluate(baseSnap, now.AddHours(2));
    runner.Equal(RunningRecoveryKind.Interrupted, overdue.Kind, "训练窗流尽 → Interrupted（≠完成）");

    runner.Equal(RunningRecoveryKind.None, RunningTimerStore.Evaluate(null, now).Kind, "无快照");
});

// ───────────────────────── 6. 仓库：损坏读 + 原子写 ─────────────────────────
runner.Case("JsonFileTodoRepository: 损坏文件抛 StorageException 且保留副本", () =>
{
    var dir = NewTempDir();
    try
    {
        var file = Path.Combine(dir, JsonFileTodoRepository.MainFileName);
        File.WriteAllText(file, "{ this is not valid json ");
        var repo = new JsonFileTodoRepository(dir);
        var threw = false;
        try { _ = repo.GetTodosAsync().GetAwaiter().GetResult(); }
        catch (StorageException ex)
        {
            threw = true;
            runner.NotNull(ex.CorruptBackupPath, "损坏副本路径");
            runner.True(File.Exists(ex.CorruptBackupPath!), "损坏副本已落盘");
        }
        runner.True(threw, "损坏读必须抛错，不降级为空库");
    }
    finally { TryDeleteDir(dir); }
});

runner.Case("JsonFileTodoRepository: 写入原子替换 + 读写往返", () =>
{
    var dir = NewTempDir();
    try
    {
        var repo = new JsonFileTodoRepository(dir);
        var id = repo.AddTodoAsync(new TodoItem { Title = "买牛奶" }).GetAwaiter().GetResult();
        runner.True(id > 0, "分配 ID");
        var all = repo.GetTodosAsync().GetAwaiter().GetResult();
        runner.Equal(1, all.Count, "读回一条");
        runner.Equal("买牛奶", all[0].Title, "标题一致");
        runner.True(!File.Exists(Path.Combine(dir, JsonFileTodoRepository.MainFileName + ".tmp")), "无残留临时文件");
    }
    finally { TryDeleteDir(dir); }
});

// ───────────────────────── 7. 提醒闭环调度决策 ─────────────────────────
runner.Case("TodoChangeDispatcher: 未来 DueAt → 排期（支持平台）", () =>
{
    var (disp, repo, sched) = NewDispatcher(supportsFuture: true);
    var item = new TodoItem { Title = "开会", DueAt = DateTimeOffset.Now.AddHours(1) };
    var r = disp.CreateAsync(item).GetAwaiter().GetResult();
    runner.True(r.Saved, "已保存");
    runner.True(r.ReminderScheduled, "已排提醒");
    runner.Equal(1, sched.ScheduleTodoCalls, "调用排期一次");
});

runner.Case("TodoChangeDispatcher: 不支持未来排期平台 → 不谎报成功", () =>
{
    var (disp, _, sched) = NewDispatcher(supportsFuture: false);
    var item = new TodoItem { Title = "开会", DueAt = DateTimeOffset.Now.AddHours(1) };
    var r = disp.CreateAsync(item).GetAwaiter().GetResult();
    runner.True(r.Saved, "已保存");
    runner.True(!r.ReminderScheduled, "不得报告已安排");
    runner.NotNull(r.ReminderMessage, "给出原因");
    runner.Equal(0, sched.ScheduleTodoCalls, "不调用排期");
});

runner.Case("TodoChangeDispatcher: 完成 → 取消全部提醒", () =>
{
    var (disp, repo, sched) = NewDispatcher(supportsFuture: true);
    var id = repo.AddTodoAsync(new TodoItem { Title = "开会", DueAt = DateTimeOffset.Now.AddHours(1) }).GetAwaiter().GetResult();
    disp.CompleteAsync(id).GetAwaiter().GetResult();
    runner.True(sched.CancelCalls.Contains(id), "取消被调用");
    runner.True(repo.GetTodosAsync().GetAwaiter().GetResult().First(t => t.Id == id).IsDone, "标记完成");
});

runner.Case("TodoChangeDispatcher: 取消勾选 → 重新排期；重新勾选 → 取消", () =>
{
    var (disp, repo, sched) = NewDispatcher(supportsFuture: true);
    var id = repo.AddTodoAsync(new TodoItem { Title = "开会", DueAt = DateTimeOffset.Now.AddHours(1) }).GetAwaiter().GetResult();

    disp.SetDoneAsync(id, true).GetAwaiter().GetResult();
    runner.True(sched.CancelCalls.Contains(id), "勾选完成取消提醒");

    var before = sched.ScheduleTodoCalls;
    disp.SetDoneAsync(id, false).GetAwaiter().GetResult();
    runner.Equal(before + 1, sched.ScheduleTodoCalls, "取消勾选重排一次");
});

runner.Case("TodoChangeDispatcher: 过去时间 → 不排且说明原因", () =>
{
    var (disp, _, sched) = NewDispatcher(supportsFuture: true);
    var item = new TodoItem { Title = "过期", DueAt = DateTimeOffset.Now.AddMinutes(-5) };
    var r = disp.CreateAsync(item).GetAwaiter().GetResult();
    runner.True(!r.ReminderScheduled, "过去时间不排");
    runner.Equal(0, sched.ScheduleTodoCalls, "不调用排期");
});

// ───────────────────────── 汇总 ─────────────────────────
return runner.Report();

// ═════════════════════ 辅助 ═════════════════════

static string NewTempDir()
{
    var dir = Path.Combine(Path.GetTempPath(), "voicetodo-selfcheck-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    return dir;
}

static void TryDeleteDir(string dir)
{
    try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { /* 忽略 */ }
}

static (TodoChangeDispatcher Dispatcher, FakeRepo Repo, FakeScheduler Scheduler) NewDispatcher(bool supportsFuture)
{
    var repo = new FakeRepo();
    var sched = new FakeScheduler { SupportsFutureScheduling = supportsFuture };
    return (new TodoChangeDispatcher(repo, sched), repo, sched);
}

/// <summary>最小断言收集器：逐条执行，统计失败明细并决定退出码。</summary>
internal sealed class CheckRunner
{
    private int _passed;
    private readonly List<string> _failures = new();
    private string _current = "";

    public void Case(string name, Action body)
    {
        _current = name;
        try
        {
            body();
            _passed++;
            Console.WriteLine($"[PASS] {name}");
        }
        catch (Exception ex)
        {
            _failures.Add($"{name} :: {ex.Message}");
            Console.WriteLine($"[FAIL] {name}");
            Console.WriteLine($"        {ex.Message}");
        }
    }

    public void True(bool condition, string what)
    {
        if (!condition) throw new Exception($"断言失败：{what}");
    }

    public void Equal<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"断言失败：{what}，期望 {expected}，实际 {actual}");
    }

    public void NotNull(object? value, string what)
    {
        if (value is null) throw new Exception($"断言失败：{what} 为 null");
    }

    public int Report()
    {
        Console.WriteLine();
        Console.WriteLine($"用例通过 {_passed} 项，失败 {_failures.Count} 项。");
        if (_failures.Count == 0)
        {
            Console.WriteLine("SELF-CHECK OK");
            return 0;
        }
        Console.WriteLine("失败明细：");
        foreach (var f in _failures) Console.WriteLine("  - " + f);
        Console.WriteLine("SELF-CHECK FAILED");
        return 1;
    }
}

/// <summary>内存仓库桩（自检用）。</summary>
internal sealed class FakeRepo : ITodoRepository
{
    private int _nextId = 1;
    private readonly List<TodoItem> _todos = new();
    private readonly List<TimerItem> _timers = new();
    private readonly List<TimerSession> _sessions = new();

    public Task<int> AddTodoAsync(TodoItem item)
    {
        item.Id = _nextId++;
        _todos.Add(item);
        return Task.FromResult(item.Id);
    }

    public Task<int> AddTimerAsync(TimerItem item)
    {
        item.Id = _nextId++;
        _timers.Add(item);
        return Task.FromResult(item.Id);
    }

    public Task UpdateTodoAsync(TodoItem item)
    {
        var i = _todos.FindIndex(t => t.Id == item.Id);
        if (i >= 0) _todos[i] = item;
        return Task.CompletedTask;
    }

    public Task UpdateTimerAsync(TimerItem item)
    {
        var i = _timers.FindIndex(t => t.Id == item.Id);
        if (i >= 0) _timers[i] = item;
        return Task.CompletedTask;
    }

    public Task<List<TodoItem>> GetTodosAsync() => Task.FromResult(new List<TodoItem>(_todos));
    public Task<List<TimerItem>> GetTimersAsync() => Task.FromResult(new List<TimerItem>(_timers));

    public Task MarkDoneAsync(int id) => SetTodoDoneAsync(id, true);

    public Task SetTodoDoneAsync(int id, bool done)
    {
        var t = _todos.FirstOrDefault(x => x.Id == id);
        if (t is not null) t.IsDone = done;
        return Task.CompletedTask;
    }

    public Task DeleteTodoAsync(int id)
    {
        _todos.RemoveAll(t => t.Id == id);
        return Task.CompletedTask;
    }

    public Task DeleteTimerAsync(int id)
    {
        _timers.RemoveAll(t => t.Id == id);
        return Task.CompletedTask;
    }

    public Task<int> AddSessionAsync(TimerSession session)
    {
        session.Id = _nextId++;
        _sessions.Add(session);
        return Task.FromResult(session.Id);
    }

    public Task<List<TimerSession>> GetSessionsAsync() => Task.FromResult(new List<TimerSession>(_sessions));

    public Task UpdateSessionAsync(TimerSession session)
    {
        var i = _sessions.FindIndex(s => s.Id == session.Id);
        if (i >= 0) _sessions[i] = session;
        return Task.CompletedTask;
    }
}

/// <summary>通知调度桩（自检用）：记录调用次数与取消过的实体。</summary>
internal sealed class FakeScheduler : INotificationScheduler
{
    public bool SupportsFutureScheduling { get; set; } = true;
    public int ScheduleTodoCalls { get; private set; }
    public List<int> CancelCalls { get; } = new();

    public Task ScheduleAsync(VoiceCommand cmd, int notificationId) => Task.CompletedTask;

    public Task ScheduleTodoAsync(TodoItem item)
    {
        ScheduleTodoCalls++;
        item.NotificationIds = new List<int> { NotificationId.Main(item.Id) };
        return Task.CompletedTask;
    }

    public Task CancelAsync(int todoId)
    {
        CancelCalls.Add(todoId);
        return Task.CompletedTask;
    }
}
