using System.Globalization;
using VoiceTodo.Core.Abstractions;
using VoiceTodo.Core.Models;

namespace VoiceTodo.Core.Services;

/// <summary>
/// 语音管线编排：文本 → 意图解析 + 时间解析 → 统一 VoiceCommand → 落库 + 调度 + 语音反馈。
/// 支持命令动作 Add / Complete / Delete / Query（B1 语音管理待办）。
/// 保持对具体平台实现的解耦（仅依赖抽象）。
/// </summary>
public class VoicePipeline
{
    private readonly IIntentParser _intent;
    private readonly ITodoRepository _repo;
    private readonly INotificationScheduler _scheduler;
    private readonly ITextToSpeech _tts;
    private readonly ILocalizer _localizer;
    private readonly Func<ReminderSettings>? _reminderDefaults;
    private readonly Action<TimerItem>? _onTimerReady; // 计时就绪通知（半截通路接线：交给壳层运行引擎）

    public VoicePipeline(
        IIntentParser intent,
        ITodoRepository repo,
        INotificationScheduler scheduler,
        ITextToSpeech tts,
        ILocalizer localizer,
        Func<ReminderSettings>? reminderDefaults = null,
        Action<TimerItem>? onTimerReady = null)
    {
        _intent = intent;
        _repo = repo;
        _scheduler = scheduler;
        _tts = tts;
        _localizer = localizer;
        _reminderDefaults = reminderDefaults;
        _onTimerReady = onTimerReady;
    }

    /// <summary>处理已识别的文本（STT 之后的环节）。</summary>
    public async Task<VoiceCommand> ProcessTextAsync(string text, CancellationToken ct = default)
    {
        var cmd = _intent.Parse(text);
        cmd.RawText = text;

        if (!cmd.IsValid)
        {
            // DEV-04：计划口述先于 IsValid 判定 —— 缺槽（如缺动作名）也要追问而不是「没听懂」
            if (IsPlanCommand(cmd))
            {
                await AskMissingSlotsAsync(cmd.MissingSlots, ct);
                return cmd;
            }
            await _tts.SpeakAsync(_localizer["UnknownCommand"], null, ct);
            return cmd;
        }

        // DEV-04 统一计划：训练口述分支 —— 完整 → 落库 + RequestStart 直通开始；缺槽 → 只追问缺失项，不播报猜测值
        if (IsPlanCommand(cmd))
        {
            if (cmd.MissingSlots.Count == 0 && cmd.Plan!.IsComplete)
            {
                await AddAsync(cmd, ct);
            }
            else
            {
                await AskMissingSlotsAsync(cmd.MissingSlots, ct);
            }
            return cmd;
        }

        ApplyReminderDefaults(cmd);

        switch (cmd.Action)
        {
            case CommandAction.Complete:
                await CompleteAsync(cmd, ct);
                break;
            case CommandAction.Delete:
                await DeleteAsync(cmd, ct);
                break;
            case CommandAction.Query:
                await QueryAsync(ct);
                break;
            default:
                await AddAsync(cmd, ct);
                break;
        }

        return cmd;
    }

    private async Task AddAsync(VoiceCommand cmd, CancellationToken ct)
    {
        if (cmd.Type == CommandType.Todo)
        {
            var todoId = await _repo.AddTodoAsync(MapTodo(cmd));
            // DEV-02：调度改用实体派生通知 ID（NotificationId.Main），删除自增错位逻辑
            await _scheduler.ScheduleAsync(cmd, NotificationId.Main(todoId));
        }
        else
        {
            var timer = MapTimer(cmd);
            var timerId = await _repo.AddTimerAsync(timer);
            _onTimerReady?.Invoke(timer); // 落库即就绪：由壳层决定启动运行引擎（TimerLauncher → 计时页准备态）
            // DEV-02：同样以实体 ID 派生通知 ID，保证取消/重排可对上
            await _scheduler.ScheduleAsync(cmd, NotificationId.Main(timerId));
        }

        var message = cmd.Type == CommandType.Todo
            ? string.Format(_localizer["CreatedTodo"], cmd.Title)
            // DEV-04：统一计划口述 → 简短「已开始」反馈（05）；普通计时维持原播报
            : cmd.Plan is { } p
                ? string.Format(_localizer["PlanStartedFormat"], p.ActionName, SpokenDuration(p.TotalDuration))
                : string.Format(_localizer["CreatedTimer"], cmd.Title, FormatDuration(cmd.Duration));
        await _tts.SpeakAsync(message, null, ct);
    }

    /// <summary>计划口述判定：解析器已进入训练计划形态（含缺槽追问）。</summary>
    private static bool IsPlanCommand(VoiceCommand cmd) =>
        cmd.Type == CommandType.Timer && cmd.Action == CommandAction.Add && cmd.Plan is not null;

    /// <summary>缺槽追问：只读缺失项，不播报任何猜测值（05）。计次语法先说明「N 次≠N 组」。</summary>
    private async Task AskMissingSlotsAsync(List<string> slots, CancellationToken ct)
    {
        var labels = new List<string>();
        foreach (var s in slots)
        {
            var label = s switch
            {
                RuleBasedIntentParser.SlotAction => _localizer["SlotAction"],
                RuleBasedIntentParser.SlotRounds => _localizer["SlotRounds"],
                RuleBasedIntentParser.SlotWork => _localizer["SlotWork"],
                RuleBasedIntentParser.SlotRest => _localizer["SlotRest"],
                _ => ""
            };
            if (label.Length > 0) labels.Add(label);
        }
        string message;
        if (slots.Contains(RuleBasedIntentParser.SlotCount))
        {
            // 「10 次」是按次计数：说明不支持并引导按组口述
            message = _localizer["SlotCountHint"];
            if (labels.Count > 0)
                message += " " + string.Format(_localizer["PlanMissingAskFormat"], string.Join("、", labels));
        }
        else
        {
            message = string.Format(_localizer["PlanMissingAskFormat"], string.Join("、", labels));
        }
        await _tts.SpeakAsync(message, null, ct);
    }

    /// <summary>口播时长：中文「X 分 Y 秒」，其他「Xm Ys」（避免读出 02:20 数字串）。</summary>
    private static string SpokenDuration(TimeSpan t)
    {
        int m = (int)t.TotalMinutes, s = t.Seconds;
        bool zh = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh";
        if (zh) return s == 0 ? $"{m} 分钟" : (m > 0 ? $"{m} 分 {s} 秒" : $"{s} 秒");
        return s == 0 ? $"{m} min" : (m > 0 ? $"{m}m {s:00}s" : $"{s}s");
    }

    private async Task CompleteAsync(VoiceCommand cmd, CancellationToken ct)
    {
        var todos = await _repo.GetTodosAsync();
        var match = FindByTitle(todos, cmd.Title ?? "", t => !t.IsDone);
        if (match is null)
        {
            await _tts.SpeakAsync(_localizer["NotFound"], null, ct);
            return;
        }
        await _repo.MarkDoneAsync(match.Id);
        await _scheduler.CancelAsync(match.Id);
        await _tts.SpeakAsync(string.Format(_localizer["CompletedTodo"], match.Title), null, ct);
    }

    private async Task DeleteAsync(VoiceCommand cmd, CancellationToken ct)
    {
        var todos = await _repo.GetTodosAsync();
        var timers = await _repo.GetTimersAsync();
        var matchTodo = FindByTitle<TodoItem>(todos, cmd.Title ?? "", _ => true);
        var matchTimer = FindByTitle(timers, cmd.Title ?? "", _ => true);

        if (matchTodo is null && matchTimer is null)
        {
            await _tts.SpeakAsync(_localizer["NotFound"], null, ct);
            return;
        }

        string? deleted = null;
        if (matchTodo is not null)
        {
            await _repo.DeleteTodoAsync(matchTodo.Id);
            await _scheduler.CancelAsync(matchTodo.Id);
            deleted = matchTodo.Title;
        }
        if (matchTimer is not null)
        {
            await _repo.DeleteTimerAsync(matchTimer.Id);
            await _scheduler.CancelAsync(matchTimer.Id);
            deleted = matchTimer.Title;
        }

        await _tts.SpeakAsync(string.Format(_localizer["DeletedTodo"], deleted ?? ""), null, ct);
    }

    private async Task QueryAsync(CancellationToken ct)
    {
        var todos = await _repo.GetTodosAsync();
        var timers = await _repo.GetTimersAsync();

        var pending = todos.Where(t => !t.IsDone).ToList();
        if (pending.Count == 0 && timers.Count == 0)
        {
            await _tts.SpeakAsync(_localizer["NoPending"], null, ct);
            return;
        }

        var names = pending.Skip(0).Select(t => t.Title)
            .Concat(timers.Select(t => t.Title))
            .Distinct()
            .Take(5)
            .ToArray();
        var text = string.Join("，", names.Select(n => n.Trim()).Where(n => n.Length > 0));
        await _tts.SpeakAsync(text.Length > 0
            ? string.Format(_localizer["QueryResult"], text)
            : _localizer["NoPending"], null, ct);
    }

    /// <summary>按标题子串模糊匹配，返回首个满足条件的项。</summary>
    private static T? FindByTitle<T>(List<T> list, string title, Func<T, bool> cond) where T : class
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        foreach (var item in list)
        {
            if (!cond(item)) continue;
            var name = item switch
            {
                TodoItem t => t.Title,
                TimerItem r => r.Title,
                _ => ""
            };
            if (name.Contains(title, StringComparison.OrdinalIgnoreCase))
                return item;
        }
        return null;
    }

    /// <summary>用全局默认值填充未显式配置的提醒字段（B3/B5）。</summary>
    private void ApplyReminderDefaults(VoiceCommand cmd)
    {
        var def = _reminderDefaults?.Invoke();
        if (def is null) return;
        cmd.Reminder ??= new ReminderSettings();
        if (!cmd.Reminder.NagMode && def.NagMode) cmd.Reminder.NagMode = true;
        cmd.Reminder.Snooze ??= def.Snooze;
        cmd.Reminder.PreAlert ??= def.PreAlert;
        cmd.Reminder.CustomSoundPath ??= def.CustomSoundPath;
    }

    private static TodoItem MapTodo(VoiceCommand cmd) => new()
    {
        Title = cmd.Title ?? "",
        DueAt = cmd.TriggerAt,
        IsRecurring = cmd.IsRecurring,
        RecurrenceRule = cmd.RecurrenceRule,
        Reminder = cmd.Reminder
    };

    private static TimerItem MapTimer(VoiceCommand cmd) => new()
    {
        Title = cmd.Title ?? "",
        Duration = cmd.Duration ?? TimeSpan.Zero,
        TriggerAt = cmd.TriggerAt ?? DateTimeOffset.Now,
        Phases = cmd.Phases,
        // DEV-04：统一计划快照（TimerLauncher → 运行引擎按 Plan 展开执行段）
        Plan = cmd.Plan,
        Reminder = cmd.Reminder
    };

    private static string FormatDuration(TimeSpan? d) =>
        d is null ? "" : $"{(int)d.Value.TotalMinutes} min";
}