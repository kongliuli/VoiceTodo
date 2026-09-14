using Microsoft.Extensions.DependencyInjection;
using VoiceTodo.Core.Abstractions;
using VoiceTodo.Core.Models;
using VoiceTodo.Core.Resources;
using VoiceTodo.Core.Services;
using VoiceTodo.Maui.Drawables;
using VoiceTodo.Maui.PlatformServices;
using VoiceTodo.Maui.Resources;
using VoiceTodo.Maui.Services;

namespace VoiceTodo.Maui.Pages;

/// <summary>「最近使用」行视图模型（供 CollectionView 编译绑定）。</summary>
public sealed record RecentRow(TimerItem Item, string Title, string Sub);

public partial class TimerPage : ContentPage
{
    /// <summary>运行队列条目（展开轮次后的扁平计划）。</summary>
    private sealed record SchedItem(string Kind, TimeSpan Duration, int Round, int Rounds, int Index, int Count);

    private readonly VoiceTodo.Core.Abstractions.ITextToSpeech _tts;
    private readonly ILocalizer _localizer;
    private readonly IAudioCoexist _audioCoexist;
    private readonly ITodoRepository _repo;
    private readonly RunningTimerStore _store;
    private readonly AnnouncerQueue _announcer;
    private readonly ITimerForegroundController _fgsc;
    private readonly RingDrawable _ringDrawable = new();

    private List<IntervalPhase> _phases = new();
    private string? _selectedPreset;
    private int _segment; // 0=倒计时 1=间歇训练

    // 运行引擎状态（页面离开后继续运行）
    private List<SchedItem> _schedule = new();
    private int _schedIdx;
    private RunningTimerInfo _info = new() { Title = "" };
    private string _runTitle = "";
    private TrainingPlan? _runPlan; // 本次运行的计划快照（倒数窗口/阶段提示/动作名/纠错编辑来源）
    private CancellationTokenSource? _runCts;
    private bool _running;
    private bool _paused;
    private bool _prepared; // 准备态：计划已装载但未开始，等待用户点「开始」
    private bool _recovering; // 快照恢复进行中（防止与 PendingStart 竞争）
    private int _lastPubSec = -1;
    private int _lastCdSec = -1; // 末秒倒数：已入队的数字秒（每秒只报一次）

    // 留痕状态（TimerSession，D2 跑完留痕）：真正开跑时快照，结束时写库且仅写一次
    private DateTimeOffset _sessionStartedAt;
    private bool _sessionActive;
    private List<IntervalPhase> _sessionPhases = new();
    private int _sessionRounds = 1;

    public TimerPage()
    {
        InitializeComponent();
        var services = Application.Current?.Handler?.MauiContext?.Services
            ?? throw new InvalidOperationException("MauiContext 未就绪");
        _tts = services.GetRequiredService<VoiceTodo.Core.Abstractions.ITextToSpeech>();
        _localizer = services.GetRequiredService<ILocalizer>();
        _audioCoexist = services.GetRequiredService<IAudioCoexist>();
        _repo = services.GetRequiredService<ITodoRepository>();
        _store = services.GetRequiredService<RunningTimerStore>();
        _announcer = services.GetRequiredService<AnnouncerQueue>();
        _fgsc = services.GetRequiredService<ITimerForegroundController>();
        _fgsc.PauseToggled += OnRemotePauseToggled; // 通知按钮 → 运行页（通知按钮=用户意图，停止不弹确认）
        _fgsc.StopRequested += OnRemoteStopRequested;
        TimerLauncher.Changed += OnTimerRequested;   // A2：运行中再来训练请求 → 征求用户意见，绝不静默忽略

        InitRing();

        Title = AppResources.TimersTitle;
        PageTitleLabel.Text = AppResources.TimersTitle;
        NewBtn.Text = AppResources.NewTimer;
        SegCountdown.Text = AppResources.Countdown;
        SegInterval.Text = AppResources.IntervalTraining;

        InProgressHeader.Text = AppResources.InProgress;
        RunCardBadge.Text = AppResources.InProgress;
        NoRunningLabel.Text = AppResources.NoRunning;
        CommonHeader.Text = AppResources.CommonTimers;
        QuickLabel5.Text = string.Format(AppResources.QuickMinFormat, 5);
        QuickLabel10.Text = string.Format(AppResources.QuickMinFormat, 10);
        QuickLabel25.Text = string.Format(AppResources.QuickMinFormat, 25);
        RecentHeader.Text = AppResources.RecentUsed;
        EmptyTimersLabel.Text = AppResources.EmptyTimers;

        TabataBtn.Text = AppResources.PresetTabata;
        HiitBtn.Text = AppResources.PresetHiit;
        CircuitBtn.Text = AppResources.PresetCircuit;
        SummaryLabel.Text = AppResources.IntervalTraining;
        StartBtn.Text = AppResources.StartTimer;

        NextUpHeader.Text = AppResources.NextUp;
        VoiceHintLabel.Text = AppResources.VoiceHintOn;
        PauseBtn.Text = AppResources.Pause;
        FinishBtn.Text = AppResources.Finish;
        FixBtn.Text = AppResources.FixMistakeBtn;

        UpdateSegments();
        UpdatePresetUI();
    }

    /// <summary>初始化环形进度：默认走 ProgressColor 纯色弧，保留 GradientCyan 渐变兜底。</summary>
    private void InitRing()
    {
        _ringDrawable.BackgroundColor = ResColor("TrackGray");
        if (Application.Current?.Resources["GradientCyan"] is Microsoft.Maui.Controls.LinearGradientBrush b
            && b.GradientStops.Count >= 2)
        {
            _ringDrawable.ProgressBrush = new Microsoft.Maui.Graphics.LinearGradientPaint
            {
                StartColor = b.GradientStops[0].Color,
                EndColor = b.GradientStops[^1].Color,
                StartPoint = new Microsoft.Maui.Graphics.Point(b.StartPoint.X, b.StartPoint.Y),
                EndPoint = new Microsoft.Maui.Graphics.Point(b.EndPoint.X, b.EndPoint.Y)
            };
        }
        _ringDrawable.StrokeThickness = 16f;
        _ringDrawable.ProgressColor = ResColor("AmberBright");
        RingDrawing.Drawable = _ringDrawable;
    }

    private static Color ResColor(string key) => (Color)Application.Current!.Resources[key];

    // ══════════ 列表视图交互 ══════════

    private void OnSegment(object sender, EventArgs e)
    {
        _segment = sender == SegInterval ? 1 : 0;
        UpdateSegments();
    }

    /// <summary>分段控件选中态：选中 bg=Surface、text=TextPrimary；未选透明、text=TextSecondary。</summary>
    private void UpdateSegments()
    {
        bool cd = _segment == 0;
        SegCountdown.BackgroundColor = cd ? ResColor("Surface") : Colors.Transparent;
        SegCountdown.TextColor = cd ? ResColor("TextPrimary") : ResColor("TextSecondary");
        SegInterval.BackgroundColor = !cd ? ResColor("Surface") : Colors.Transparent;
        SegInterval.TextColor = !cd ? ResColor("TextPrimary") : ResColor("TextSecondary");
        CountdownPanel.IsVisible = cd;
        IntervalPanel.IsVisible = !cd;
    }

    private async void OnNewTimer(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("TimerEditorPage");

    private void OnPreset(object sender, EventArgs e)
    {
        _phases = sender switch
        {
            Button b when b == TabataBtn => Preset(20, 10, 8),
            Button b when b == HiitBtn => Preset(30, 15, 8),
            Button b when b == CircuitBtn => Preset(45, 15, 6),
            _ => _phases
        };
        _selectedPreset = sender switch
        {
            Button b when b == TabataBtn => "tabata",
            Button b when b == HiitBtn => "hiit",
            Button b when b == CircuitBtn => "circuit",
            _ => _selectedPreset
        };
        UpdatePresetUI();
        UpdateSummary();
    }

    /// <summary>刷新三个预设 chip 的选中态（选中底色 SelectedBg、描边/文字 Primary）。</summary>
    private void UpdatePresetUI()
    {
        void Apply(Border chip, Button btn, string key)
        {
            bool sel = _selectedPreset == key;
            chip.BackgroundColor = sel ? ResColor("SelectedBg") : ResColor("Surface");
            chip.Stroke = sel ? ResColor("Primary") : ResColor("BorderColor");
            chip.StrokeThickness = sel ? 2 : 1;
            btn.TextColor = sel ? ResColor("Primary") : ResColor("TextSecondary");
        }
        Apply(TabataChip, TabataBtn, "tabata");
        Apply(HiitChip, HiitBtn, "hiit");
        Apply(CircuitChip, CircuitBtn, "circuit");
    }

    private void UpdateSummary()
    {
        if (_phases.Count == 0)
        {
            SummaryLabel.Text = AppResources.IntervalTraining;
            return;
        }
        var work = _phases[0];
        var rest = _phases.Count > 1 ? _phases[1] : null;
        SummaryLabel.Text =
            $"{_localizer["Work"]} {FmtDur(work.Duration)} / {_localizer["Rest"]} {FmtDur(rest?.Duration ?? TimeSpan.Zero)} × {work.Rounds}";
    }

    private static List<IntervalPhase> Preset(int workSec, int restSec, int rounds) => new()
    {
        new IntervalPhase { Kind = "work", Duration = TimeSpan.FromSeconds(workSec), Rounds = rounds },
        new IntervalPhase { Kind = "rest", Duration = TimeSpan.FromSeconds(restSec), Rounds = rounds }
    };

    private void OnQuickTap(object? sender, TappedEventArgs e)
    {
        if (_running) return;
        if (int.TryParse(e?.Parameter?.ToString(), out int min) && min > 0)
            PrepareRun(new List<SchedItem> { new("countdown", TimeSpan.FromMinutes(min), 1, 1, 0, 1) },
                string.Format(AppResources.QuickMinFormat, min));
    }

    private void OnRecentStart(object sender, EventArgs e)
    {
        if (_running) return;
        if (sender is not Button b || b.CommandParameter is not TimerItem item) return;

        // 「最近使用」保留计划/间歇结构：优先按计划快照重建执行段，其次旧 Phases，最后才退化为普通倒计时
        if (item.Plan is { IsComplete: true } plan)
        {
            PrepareRun(Flatten(plan.Expand()),
                string.IsNullOrWhiteSpace(item.Title) ? plan.ActionName : item.Title, plan);
            return;
        }
        if (item.Phases is { Count: > 0 })
        {
            PrepareRun(Flatten(item.Phases),
                string.IsNullOrWhiteSpace(item.Title) ? AppResources.IntervalTraining : item.Title, item.Plan);
            return;
        }
        if (item.Duration > TimeSpan.Zero)
            PrepareRun(new List<SchedItem> { new("countdown", item.Duration, 1, 1, 0, 1) }, item.Title);
    }

    private void OnStartInterval(object sender, EventArgs e)
    {
        if (_running || _phases.Count == 0) return;
        PrepareRun(Flatten(_phases), AppResources.IntervalTraining); // 预设无 Plan：沿用 Work/Rest 口令，无倒数窗
    }

    private async Task LoadRecentAsync()
    {
        try
        {
            var timers = await _repo.GetTimersAsync();
            if (Window is null) return;
            var rows = timers.Select(t => new RecentRow(t, t.Title, FmtDur(t.Duration))).ToList();
            RecentList.ItemsSource = rows;
            RecentList.IsVisible = rows.Count > 0;
            EmptyTimersLabel.IsVisible = rows.Count == 0;
        }
        catch
        {
            // 最近使用加载失败不阻塞页面
        }
    }

    // ══════════ 运行引擎（可暂停，250ms 轮询，跨页面继续运行） ══════════

    /// <summary>准备运行：装载计划并展示运行视图，但不立刻开始，等待用户点「开始」。</summary>
    private void PrepareRun(List<SchedItem> schedule, string title, TrainingPlan? plan = null)
    {
        if (_running || schedule.Count == 0) return;
        _schedule = schedule;
        _runTitle = title;
        _runPlan = plan;
        _schedIdx = 0;      // 新计划从头开始（避免沿用上一轮的下标越界）
        _lastCdSec = -1;
        _prepared = true;
        ShowRunView();
        RefreshPreparedUi();
    }

    /// <summary>准备态 UI：环形进度归零、显示首阶段满时长，主按钮显示「开始」。</summary>
    private void RefreshPreparedUi()
    {
        var s = _schedule[0];
        _info.Title = _runTitle;
        _info.Phase = s.Kind;
        _info.Total = s.Duration;
        _info.EndAt = DateTimeOffset.Now + s.Duration; // Remaining = 满时长
        _info.Paused = false;
        _info.RemainingWhenPaused = TimeSpan.Zero;
        _info.Round = s.Round;
        _info.Rounds = s.Rounds;
        _info.ActionName = _runPlan?.ActionName ?? "";
        _info.TotalRemaining = ComputeTotalRemaining();
        ApplyPhaseVisuals(s); // 仅视觉，不播报
        _ringDrawable.Progress = 0;
        RingDrawing.Invalidate();
        RunTimeLabel.Text = s.Duration.ToString(@"mm\:ss");
        PauseBtn.Text = AppResources.StartTimer;
    }

    /// <summary>从准备态真正开始：启动运行循环（RunLoopAsync 会 BeginPhase(0) 并播报）。</summary>
    private void StartPreparedRun()
    {
        if (_running || _schedule.Count == 0) return;
        _prepared = false;
        _running = true;
        _paused = false;
        _lastPubSec = -1;
        _sessionStartedAt = DateTimeOffset.Now; // 留痕：开始时间 + 阶段快照
        _sessionActive = true;
        _sessionPhases = SnapshotPhases();
        _sessionRounds = Math.Max(1, _schedule[0].Rounds);
        _fgsc.Start(_runTitle); // Android 前台服务保活 + 常驻通知（Windows 空实现）
        SaveSnapshot();         // 开跑即落盘，进程随时可被系统回收
        _runCts = new CancellationTokenSource();
        _ = RunLoopAsync(_runCts.Token, announceFirstPhase: true);
    }

    /// <summary>从扁平计划还原阶段快照：同 Kind+Duration 的 SchedItem 折叠为一个 IntervalPhase，轮次取出现次数（countdown 单段即 [countdown×1]）。</summary>
    private List<IntervalPhase> SnapshotPhases() => FoldPhases(_schedule);

    /// <summary>FoldPhases 的纯函数形态（恢复中断留痕时对快照执行段复用）。</summary>
    private static List<IntervalPhase> FoldPhases(IEnumerable<SchedItem> schedule)
    {
        var phases = new List<IntervalPhase>();
        foreach (var s in schedule)
        {
            var match = phases.FirstOrDefault(p => p.Kind == s.Kind && p.Duration == s.Duration);
            if (match is null)
                phases.Add(new IntervalPhase { Kind = s.Kind, Duration = s.Duration, Rounds = 1 });
            else
                match.Rounds++;
        }
        return phases;
    }

    private async Task RunLoopAsync(CancellationToken ct, bool announceFirstPhase)
    {
        try
        {
            if (announceFirstPhase)
                BeginPhase(0);
            // 恢复路径（announceFirstPhase=false）由 TryRecoverAsync 先 AdoptPhase，不重置时刻、不补播
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (!_paused && _info.EndAt - DateTimeOffset.Now <= TimeSpan.Zero)
                {
                    if (_schedIdx >= _schedule.Count - 1)
                    {
                        await FinishNaturalAsync(ct);
                        return;
                    }
                    BeginPhase(_schedIdx + 1);
                    continue;
                }
                Tick();
                await Task.Delay(250, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            EndRun("cancelled"); // 异常路径按中途结束留痕
        }
    }

    /// <summary>进入新阶段：按当前时刻重建 EndAt、播报、落盘快照。</summary>
    private void BeginPhase(int i)
    {
        var s = _schedule[i];
        AdoptPhase(i, DateTimeOffset.Now + s.Duration, paused: false, pausedRemaining: TimeSpan.Zero);
        RunningTimerHub.Publish(_info); // 开始/换阶段
        AnnouncePhase(s);
        SaveSnapshot();
    }

    /// <summary>采纳某阶段状态（新开始/快照恢复共用）：设置共享快照字段与视觉；恢复路径不重置时刻、不补播。</summary>
    private void AdoptPhase(int i, DateTimeOffset endAt, bool paused, TimeSpan pausedRemaining)
    {
        _schedIdx = i;
        var s = _schedule[i];
        _info.Title = _runTitle;
        _info.Phase = s.Kind;
        _info.Total = s.Duration;
        _info.EndAt = endAt;
        _info.Paused = paused;
        _info.RemainingWhenPaused = pausedRemaining;
        _info.Round = s.Round;
        _info.Rounds = s.Rounds;
        _info.ActionName = _runPlan?.ActionName ?? "";
        _info.TotalRemaining = ComputeTotalRemaining();
        _paused = paused;
        _lastPubSec = -1;
        _lastCdSec = -1;
        ApplyPhaseVisuals(s);
    }

    /// <summary>阶段视觉切换（不含 TTS 播报，播报由 BeginPhase 触发）。工作阶段突出动作名（09 稿）。</summary>
    private void ApplyPhaseVisuals(SchedItem s)
    {
        bool isWork = s.Kind == "work";
        bool isRest = s.Kind == "rest";
        Color accent = isWork ? ResColor("AmberBright") : ResColor("Primary");

        RunPhaseLabel.Text = isWork
            ? (string.IsNullOrEmpty(_info.ActionName) ? _localizer["Work"] : _info.ActionName)
            : isRest ? _localizer["Rest"] : _runTitle;
        RunPhaseLabel.TextColor = accent;
        RunTimeLabel.TextColor = accent;
        _ringDrawable.ProgressColor = accent; // work/countdown 琥珀弧，rest 蓝弧

        bool interval = s.Kind != "countdown";
        RoundLabel.IsVisible = interval;
        if (interval)
        {
            RoundLabel.Text = string.Format(AppResources.RoundFormat, s.Round, s.Rounds);
            RunTotalLabel.Text = LocalPhaseCaption(s.Duration); // 运行中被 UpdateRunUi 覆写为全程剩余
            NextUpCard.IsVisible = true;
            NextUpLabel.Text = s.Index < s.Count - 1 ? DescribeNext(_schedule[s.Index + 1]) : "——";
        }
        else
        {
            RunTotalLabel.Text = string.Format(AppResources.TotalLabel, FmtDur(s.Duration));
            NextUpCard.IsVisible = false;
        }

        // 「识别有误，停止并修改」入口：仅计划型训练（口述创建）且已开跑
        FixBtnWrap.IsVisible = _runPlan is not null && _running;

        if (Window is not null)
        {
            UpdateRunUi();
            _ = PhasePopAsync(accent);
        }
    }

    /// <summary>逐 250ms：每秒边界 Publish 快照 + 落盘 + 刷通知 + 末秒倒数；页面可见时刷新运行 UI / 运行卡。</summary>
    private void Tick()
    {
        if (!_paused)
        {
            int sec = (int)Math.Max(0, _info.Remaining.TotalSeconds);
            if (sec != _lastPubSec)
            {
                _lastPubSec = sec;
                _info.TotalRemaining = ComputeTotalRemaining(); // 预计算全程剩余
                RunningTimerHub.Publish(_info); // 每秒
                _fgsc.Update(_runTitle, ClockText(), _paused); // 常驻通知剩余时间
                _ = _store.SaveAsync(BuildSnapshot(_paused, _info.RemainingWhenPaused)); // 每秒落盘（尽力而为）
                EnqueueCountdownDigit(sec); // 末秒倒数（窗口 5/3）
            }
        }
        if (Window is null) return; // 跨页面运行时只推进状态，不碰 UI
        if (RunScroll.IsVisible) UpdateRunUi();
        if (RunningCard.IsVisible) UpdateRunningCard();
    }

    private void UpdateRunUi()
    {
        var rem = _info.Remaining;
        if (rem < TimeSpan.Zero) rem = TimeSpan.Zero;
        RunTimeLabel.Text = rem.ToString(@"mm\:ss");
        _ringDrawable.Progress = Math.Clamp(_info.ElapsedFraction, 0, 1);
        RingDrawing.Invalidate();
        PauseBtn.Text = _paused ? AppResources.Resume : AppResources.Pause;
        RunCardBtn.Text = _paused ? "▶" : "⏸";
        // 全程剩余（09 稿）：间歇训练显示「剩余各段之和」，普通倒计时保持「共 X」
        if (_schedIdx < _schedule.Count && _schedule[_schedIdx].Kind != "countdown")
            RunTotalLabel.Text = string.Format(AppResources.TotalLeftFmt, FmtDur(_info.TotalRemaining));
    }

    private void UpdateRunningCard()
    {
        var cur = RunningTimerHub.Current;
        if (cur is null) return;
        var rem = cur.Remaining;
        if (rem < TimeSpan.Zero) rem = TimeSpan.Zero;
        RunCardTitle.Text = cur.Title;
        RunCardTime.Text = rem.ToString(@"mm\:ss");
        double f = Math.Clamp(cur.ElapsedFraction, 0, 1);
        RunCardProgress.Progress = f;
        RunCardPct.Text = $"{(int)Math.Round(f * 100)}%";
        RunCardTotal.Text = string.Format(AppResources.TotalLabel, FmtDur(cur.Total));
        RunCardBtn.Text = cur.Paused ? "▶" : "⏸";
    }

    private void SyncCardVisibility()
    {
        bool has = RunningTimerHub.Current is not null;
        RunningCard.IsVisible = has;
        NoRunningLabel.IsVisible = !has;
    }

    private void OnPauseResume(object sender, EventArgs e)
    {
        if (_prepared && !_running)
        {
            StartPreparedRun(); // 准备态：主按钮即「开始」
            return;
        }
        if (!_running) return;
        if (_paused)
        {
            _info.EndAt = DateTimeOffset.Now + _info.RemainingWhenPaused; // 继续：按冻结剩余重算 EndAt
            _info.Paused = false;
            _paused = false;
            SaveSnapshot(pausedByUser: false); // 快照回到运行态（EndAt 已重建）
        }
        else
        {
            var rem = _info.EndAt - DateTimeOffset.Now;
            _info.RemainingWhenPaused = rem < TimeSpan.Zero ? TimeSpan.Zero : rem; // 暂停：冻结剩余
            _info.Paused = true;
            _paused = true;
            _announcer.Clear(); // 暂停清空待播（含倒数数字），恢复不补播
            SaveSnapshot(pausedByUser: true, pausedRemaining: _info.RemainingWhenPaused); // 暂停快照含剩余
        }
        _info.TotalRemaining = ComputeTotalRemaining();
        _lastPubSec = -1;
        RunningTimerHub.Publish(_info); // 暂停/继续
        _fgsc.Update(_runTitle, ClockText(), _paused); // 通知暂停/继续按钮态同步
        if (Window is null) return;
        if (RunScroll.IsVisible) UpdateRunUi();
        if (RunningCard.IsVisible) UpdateRunningCard();
    }

    private async void OnFinish(object sender, EventArgs e)
    {
        if (!_running) return;
        bool confirm = await DisplayAlert(AppResources.EndConfirmTitle, AppResources.EndConfirmText,
            AppResources.Finish, AppResources.Cancel);
        if (confirm) EndRun("cancelled");
    }

    /// <summary>自然跑完：播报 TimerDone → Publish(null) → 回列表。</summary>
    private async Task FinishNaturalAsync(CancellationToken ct)
    {
        try
        {
            if (Preferences.Default.Get("ttsCues", true))
                await SpeakWithCoexistAsync(CoreStrings.TimerDone, ct);
        }
        catch (OperationCanceledException)
        {
            return; // 用户已结束，EndRun 已处理
        }
        EndRun("completed");
    }

    /// <summary>结束运行（自然完成 / 确认结束 / 通知停止 / 纠错停止 / 异常路径）：
    /// 取消循环 → 清播报队列 → 删快照 → 停前台服务 → 留痕 → Publish(null) → 回列表。</summary>
    private void EndRun(string outcome)
    {
        if (!_running) return; // 准备态未开跑时绝不写留痕
        _running = false;
        _paused = false;
        _prepared = false;
        try { _runCts?.Cancel(); } catch { }
        _runCts = null;
        _announcer.Clear();       // 清空待播（含倒数数字）并中断在播语句
        _ = _store.DeleteAsync(); // 训练结束/取消即删快照
        _fgsc.Stop();             // 停止前台服务与常驻通知（结束不留幽灵通知）
        SaveSession(outcome); // 只写一次（_sessionActive 控制）
        RunningTimerHub.Publish(null); // 结束
        ShowListView();
        SyncCardVisibility();
    }

    /// <summary>写运行记录（TimerSession → 日历图层）：fire-and-forget，失败不阻塞 UI。</summary>
    private void SaveSession(string outcome)
    {
        if (!_sessionActive) return; // 未开跑或已写过
        _sessionActive = false;
        var session = new TimerSession
        {
            Title = _runTitle,
            StartedAt = _sessionStartedAt,
            EndedAt = DateTimeOffset.Now,
            Phases = _sessionPhases,
            Rounds = _sessionRounds,
            Outcome = outcome // "completed" 自然跑完 / "cancelled" 中途结束 / "interrupted" 快照恢复失败（DEV-05）
        };
        _ = SaveSessionAsync(session);
    }

    private async Task SaveSessionAsync(TimerSession session)
    {
        try
        {
            await _repo.AddSessionAsync(session);
        }
        catch (Exception ex)
        {
            // 留痕失败不阻塞结束流程，但必须如实提示（否则“结束后既没记录也没提示”）
            try
            {
                Dispatcher.Dispatch(() =>
                {
                    SessionSaveFailedBanner.Text = string.Format(
                        AppResources.SessionSaveFailedFormat,
                        ex is StorageException ? ex.Message : "");
                    SessionSaveFailedBanner.IsVisible = true;
                });
            }
            catch
            {
                // 提示失败不影响宿主。
            }
        }
    }

    private void ShowRunView()
    {
        ListScroll.IsVisible = false;
        RunScroll.IsVisible = true;
        RunPhaseLabel.Opacity = 1;
        RunPhaseLabel.Scale = 1;
        if (Window is not null) UpdateRunUi();
    }

    private void ShowListView()
    {
        RunScroll.IsVisible = false;
        ListScroll.IsVisible = true;
    }

    // ══════════ TTS（B6 音频共存）与动效 ══════════

    /// <summary>播报前先压低后台声音（B6 背景音乐共存），播报完成即恢复后台音量。</summary>
    private async Task SpeakWithCoexistAsync(string text, CancellationToken ct)
    {
        var coexist = AppSettings.AudioCoexist;
        if (coexist) _audioCoexist.DuckForSpeech();
        try
        {
            await _tts.SpeakAsync(text, null, ct);
        }
        finally
        {
            if (coexist) _audioCoexist.Restore();
        }
    }

    // ══════════ DEV-05：快照 / 播报队列 / 末秒倒数 / 全程剩余 ══════════

    /// <summary>构造运行快照（每秒/每阶段/暂停变更落盘，running-timer.json）。</summary>
    private RunningTimerSnapshot BuildSnapshot(bool pausedByUser, TimeSpan pausedRemaining) => new()
    {
        Title = _runTitle,
        Plan = _runPlan,
        Segments = _schedule
            .Select(s => new SnapshotSegment(s.Kind, s.Duration.Ticks, s.Round, s.Rounds))
            .ToList(),
        SchedIdx = _schedIdx,
        EndAt = _info.EndAt,
        StartedAt = _sessionStartedAt,
        PausedByUser = pausedByUser,
        PausedRemainingTicks = pausedRemaining.Ticks,
        Round = _schedule.Count > 0 ? _schedule[_schedIdx].Round : _info.Round,
        Rounds = _schedule.Count > 0 ? _schedule[_schedIdx].Rounds : _info.Rounds,
        SavedAt = DateTimeOffset.Now
    };

    /// <summary>落盘快照（尽力而为，失败由 RunningTimerStore 静默吞掉，绝不阻塞计时）。</summary>
    private void SaveSnapshot(bool? pausedByUser = null, TimeSpan? pausedRemaining = null)
    {
        if (!_running) return;
        bool p = pausedByUser ?? _paused;
        var rem = pausedRemaining ?? (p ? _info.RemainingWhenPaused : TimeSpan.Zero);
        _ = _store.SaveAsync(BuildSnapshot(p, rem));
    }

    /// <summary>全程剩余 = 当前段剩余 + 后续各段时长和（09 稿；验收：第 3 组剩 3 秒 → 全程 01:48）。</summary>
    private TimeSpan ComputeTotalRemaining()
    {
        var curRem = _info.Paused ? _info.RemainingWhenPaused : _info.EndAt - DateTimeOffset.Now;
        if (curRem < TimeSpan.Zero) curRem = TimeSpan.Zero;
        var sum = curRem;
        for (int i = _schedIdx + 1; i < _schedule.Count; i++)
            sum += _schedule[i].Duration;
        return sum;
    }

    /// <summary>通知用剩余时钟（MM:SS，超 1 小时按总分钟进位）。</summary>
    private string ClockText()
    {
        var rem = _info.Remaining;
        if (rem < TimeSpan.Zero) rem = TimeSpan.Zero;
        return $"{(int)rem.TotalMinutes:00}:{rem.Seconds:00}";
    }

    /// <summary>
    /// 阶段提示（05 §报数）：work→「第 N 组，{动作名}」；rest→「休息 N 秒」；倒计时/无计划沿用标题或 Work/Rest。
    /// 经 AnnouncerQueue 过期丢弃，播放时间不延长计时；末组结束由 FinishNaturalAsync 播「训练完成」。
    /// </summary>
    private void AnnouncePhase(SchedItem s)
    {
        if (!Preferences.Default.Get("ttsCues", true)) return; // 编辑器「语音提示」开关
        if (_runPlan is { PhaseAnnouncements: false }) return; // 计划的阶段指示开关关闭
        int window = _runPlan?.CountdownWindowSeconds ?? 0;
        if (window > 0 && s.Duration.TotalSeconds <= window)
            return; // 短阶段只报实际剩余秒（数字倒数已覆盖），不读长句盖满全段
        string text = s.Kind switch
        {
            "work" => _runPlan is null ? _localizer["Work"]
                : string.Format(AppResources.RoundAnnounceFmt, s.Round, _runPlan.ActionName),
            "rest" => _runPlan is null ? _localizer["Rest"]
                : string.Format(AppResources.RestAnnounceFmt, (int)s.Duration.TotalSeconds),
            _ => _runTitle
        };
        _announcer.Enqueue(text, _info.EndAt); // 阶段结束仍未播出即作废
    }

    /// <summary>
    /// 末秒倒数（05 §报数）：按 CountdownWindowSeconds（5/3/0=关），在运动与休息阶段剩余恰为窗值时
    /// 逐秒入队「5」「4」「3」「2」「1」（3 窗则 3..1）；短阶段（时长≤窗值）只报实际剩余秒。
    /// 数字须在本秒边界前播出（截止 = EndAt − (sec−1) 秒），过界即被队列丢弃；暂停清空、恢复不补播。
    /// </summary>
    private void EnqueueCountdownDigit(int sec)
    {
        int window = _runPlan?.CountdownWindowSeconds ?? 0;
        if (window <= 0 || sec < 1 || _schedule.Count == 0) return;
        var kind = _schedule[_schedIdx].Kind;
        if (kind != "work" && kind != "rest") return;
        if (!Preferences.Default.Get("ttsCues", true)) return;
        int cap = Math.Min(window, (int)_schedule[_schedIdx].Duration.TotalSeconds); // 短阶段封顶到实际时长
        if (sec > cap || sec == _lastCdSec) return; // 每个数字只入队一次
        _lastCdSec = sec;
        _announcer.Enqueue(sec.ToString(_localizer.CurrentCulture),
            _info.EndAt - TimeSpan.FromSeconds(sec - 1));
    }

    /// <summary>阶段切换提示：文字着色 + 一次淡入缩放（PhasePop）。禁用动效时直接呈现。</summary>
    private async Task PhasePopAsync(Color color)
    {
        if (Window is null) return;
        if (!AppSettings.UseAnimations)
        {
            RunPhaseLabel.Opacity = 1;
            RunPhaseLabel.Scale = 1;
            return;
        }
        try
        {
            RunPhaseLabel.Opacity = 0;
            RunPhaseLabel.Scale = 0.7;
            await RunPhaseLabel.FadeToAsync(1, 180, Easing.CubicOut);
            await RunPhaseLabel.ScaleToAsync(1, 220, Easing.CubicOut);
        }
        catch (OperationCanceledException) { }
    }

    // ══════════ 文案与格式化 ══════════

    private string DescribeNext(SchedItem next)
    {
        string name = next.Kind switch
        {
            "work" => _localizer["Work"],
            "rest" => _localizer["Rest"],
            _ => _runTitle
        };
        return $"{name} {FmtDur(next.Duration)}";
    }

    private string LocalPhaseCaption(TimeSpan d)
    {
        bool zh = _localizer.CurrentCulture.TwoLetterISOLanguageName == "zh";
        return zh ? $"本阶段 {FmtDur(d)}" : string.Format(AppResources.TotalLabel, FmtDur(d));
    }

    /// <summary>时长 →「X 分钟」/「X分Y秒」（非中文环境用「Xm YYs」）。</summary>
    private string FmtDur(TimeSpan t)
    {
        t = t.Duration();
        int m = (int)t.TotalMinutes;
        int s = t.Seconds;
        if (s == 0) return string.Format(AppResources.QuickMinFormat, m);
        bool zh = _localizer.CurrentCulture.TwoLetterISOLanguageName == "zh";
        return zh ? (m > 0 ? $"{m}分{s}秒" : $"{s}秒") : (m > 0 ? $"{m}m {s:00}s" : $"{s}s");
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        UpdateSegments();
        UpdatePresetUI();
        InterruptedBanner.IsVisible = false; // 本次恢复若再检出中断会重新置起
        SessionSaveFailedBanner.IsVisible = false; // 重新进入页面时清掉上次的留痕失败提示
        SyncCardVisibility();
        if (!_running && RunningTimerHub.Current is not null)
        {
            _info = RunningTimerHub.Current; // 页面重建后接管 Hub 快照用于运行卡展示
            UpdateRunningCard();
        }
        if (_running) ShowRunView();
        else if (_prepared && _schedule.Count > 0) { ShowRunView(); RefreshPreparedUi(); }
        else ShowListView();
        _ = LoadRecentAsync();
        if (_running || _prepared)
            return; // 页面内已有活动/准备态训练，不动快照
        if (TimerLauncher.PendingStart is not null)
        {
            ConsumePendingStart(); // 新请求优先于旧快照
            if (TimerLauncher.PendingStart is null && !_running && !_prepared)
                _ = _store.DeleteAsync(); // 新训练接管，遗留快照废弃
        }
        else if (!_recovering)
        {
            _ = TryRecoverAsync(); // 无新请求时尝试恢复上次中断的运行
        }
    }

    // ══════════ A2：运行中训练冲突（替换 / 取消） ══════════

    /// <summary>A2：计时请求到达时若有训练正在运行/准备中，必须征求用户意见，绝不静默 return。
    /// 仅前台/后台运行态才接管；空闲态仍交给 OnAppearing 的常规消费路径。</summary>
    private void OnTimerRequested()
    {
        if (!_running && !_prepared) return;       // 无活动训练：不拦截，留给 OnAppearing 消费
        var item = TimerLauncher.PendingStart;
        if (item is null) return;
        // 语音管线/通知回调可能在非 UI 线程触发，统一切回 UI 线程弹确认框
        Dispatcher.Dispatch(async () => await ResolveConflictAsync(item));
    }

    /// <summary>A2：运行中新训练请求的处理——「替换」走既有停止路径收口旧训练再开新；
    /// 「取消」丢弃新请求并给出可见反馈，当前训练继续。绝不静默忽略。</summary>
    private async Task ResolveConflictAsync(TimerItem item)
    {
        var autoStart = TimerLauncher.PendingAutoStart; // 保留原意图（完整指令=直接开跑）
        bool replace = await DisplayAlert(AppResources.Conflict_Title, AppResources.Conflict_Text,
            AppResources.Conflict_Replace, AppResources.Conflict_Cancel);
        if (!replace)
        {
            // 取消：丢弃新请求，当前训练/准备态不变，明确告知用户
            TimerLauncher.PendingStart = null;
            TimerLauncher.PendingAutoStart = false;
            await ShowConflictKeptAsync();
            return;
        }
        // 替换：正在运行 → 按既有停止路径收口旧训练（写留痕、清快照、停前台服务）；仅准备态 → 直接覆盖
        if (_running) EndRun("cancelled");
        TimerLauncher.PendingStart = item;
        TimerLauncher.PendingAutoStart = autoStart;
        ConsumePendingStart(); // 复用既有装载/开跑逻辑
    }

    /// <summary>A2：取消替换时给出可见反馈（保留当前训练）。</summary>
    private async Task ShowConflictKeptAsync()
    {
        try
        {
            await DisplayAlert(AppResources.Conflict_Title, AppResources.Conflict_Kept, AppResources.OK);
        }
        catch { /* 提示失败不影响训练 */ }
    }

    /// <summary>消费计时编辑器「保存并开始」/ 语音直通的请求（TimerLauncher）。
    /// AutoStart=true（完整有效指令）时装载后立即开跑，否则停在准备态等待「开始」。</summary>
    private void ConsumePendingStart()
    {
        var item = TimerLauncher.PendingStart;
        if (item is null || _running) return;
        var autoStart = TimerLauncher.PendingAutoStart;
        TimerLauncher.PendingStart = null;
        TimerLauncher.PendingAutoStart = false;

        TrainingPlan? plan = null;
        List<SchedItem> sched;
        if (item.Plan is { IsComplete: true } p)
        {
            // 统一计划模型：执行段由 Expand 派生（语音/模板/最近使用同一解释）
            plan = p;
            sched = Flatten(p.Expand());
        }
        else if (item.Phases is { Count: > 0 })
        {
            plan = item.Plan; // 旧编辑器产物可能无 Plan（倒数窗口等随 Plan 走）
            sched = Flatten(item.Phases);
        }
        else
        {
            var total = item.Duration > TimeSpan.Zero ? item.Duration : TimeSpan.FromMinutes(1);
            sched = new List<SchedItem> { new("countdown", total, 1, 1, 0, 1) };
        }
        PrepareRun(sched, string.IsNullOrWhiteSpace(item.Title) ? AppResources.IntervalTraining : item.Title, plan);
        if (autoStart) StartPreparedRun(); // 完整有效指令：直接进入运行态（无第二次确认）
    }

    /// <summary>扁平化执行段：按各阶段自身轮次交错展开（末组无休时 rest.Rounds = rounds−1，末轮不再补休息）。</summary>
    private static List<SchedItem> Flatten(List<IntervalPhase> phases)
    {
        int maxRounds = phases.Count > 0 ? Math.Max(1, phases.Max(p => p.Rounds)) : 1;
        var items = new List<(string Kind, TimeSpan Duration, int Round)>();
        for (int r = 1; r <= maxRounds; r++)
            foreach (var ph in phases)
                if (r <= Math.Max(1, ph.Rounds))
                    items.Add((ph.Kind, ph.Duration, r));
        return items
            .Select((x, i) => new SchedItem(x.Kind, x.Duration, x.Round, maxRounds, i, items.Count))
            .ToList();
    }

    // ══════════ DEV-05：运行快照恢复 / 通知动作 / 纠错停止 ══════════

    /// <summary>
    /// 启动恢复（快照存在且未过期才走恢复，恢复矩阵见 RunningTimerStore.Evaluate）：
    /// EndAt 在未来 → 原位继续；跨段挂起 → 按真实经过时间推进；用户暂停 → 直接进暂停态；
    /// 无法对应 → 「上次训练中断」留痕（interrupted，≠completed）并丢弃快照。
    /// </summary>
    private async Task TryRecoverAsync()
    {
        _recovering = true;
        try
        {
            var snap = await _store.LoadAsync();
            if (snap is null || Window is null) return;
            if (_running || _prepared || TimerLauncher.PendingStart is not null)
                return; // 恢复期间来了新请求：旧快照交由其消费路径废弃
            var rec = RunningTimerStore.Evaluate(snap, DateTimeOffset.Now);
            switch (rec.Kind)
            {
                case RunningRecoveryKind.Resume:
                case RunningRecoveryKind.Advance:
                    SetupRecoveredRun(snap);
                    AdoptPhase(rec.SegmentIndex, rec.EndAt, paused: false, TimeSpan.Zero);
                    _runCts = new CancellationTokenSource();
                    _ = RunLoopAsync(_runCts.Token, announceFirstPhase: false);
                    SaveSnapshot(); // Advance 后旧快照已过期，立即重写
                    break;

                case RunningRecoveryKind.ResumePaused:
                    SetupRecoveredRun(snap);
                    AdoptPhase(rec.SegmentIndex, rec.EndAt, paused: true, rec.PausedRemaining);
                    _runCts = new CancellationTokenSource();
                    _ = RunLoopAsync(_runCts.Token, announceFirstPhase: false);
                    SaveSnapshot(); // EndAt 重算为 now+冻结剩余，保持快照一致
                    break;

                case RunningRecoveryKind.Interrupted:
                    HandleInterrupted(snap);
                    break;
            }
        }
        finally
        {
            _recovering = false;
        }
    }

    /// <summary>由快照重建运行状态（执行段/标题/计划/留痕起点），进入运行视图。</summary>
    private void SetupRecoveredRun(RunningTimerSnapshot snap)
    {
        _schedule = snap.Segments
            .Select((s, i) => new SchedItem(s.Kind, TimeSpan.FromTicks(s.DurationTicks),
                s.Round, s.Rounds, i, snap.Segments.Count))
            .ToList();
        _runTitle = string.IsNullOrWhiteSpace(snap.Title) ? AppResources.IntervalTraining : snap.Title;
        _runPlan = snap.Plan;
        _sessionStartedAt = snap.StartedAt; // 留痕沿用原开始时刻
        _sessionActive = true;
        _sessionPhases = SnapshotPhases();
        _sessionRounds = Math.Max(1, _schedule[0].Rounds);
        _running = true;
        _prepared = false;
        _fgsc.Start(_runTitle); // 恢复运行即重建保活通知
        ShowRunView();
    }

    /// <summary>「上次训练 interrupted」路径：写一条 interrupted 留痕（≠completed）、删快照、列表页如实提示。</summary>
    private void HandleInterrupted(RunningTimerSnapshot snap)
    {
        var schedule = snap.Segments
            .Select((s, i) => new SchedItem(s.Kind, TimeSpan.FromTicks(s.DurationTicks),
                s.Round, s.Rounds, i, snap.Segments.Count))
            .ToList();
        _ = SaveSessionAsync(new TimerSession
        {
            Title = string.IsNullOrWhiteSpace(snap.Title) ? AppResources.IntervalTraining : snap.Title,
            StartedAt = snap.StartedAt,
            EndedAt = DateTimeOffset.Now,
            Phases = FoldPhases(schedule),
            Rounds = Math.Max(1, schedule.Count > 0 ? schedule[0].Rounds : 1),
            Outcome = "interrupted" // 中断≠完成（日历「已中断」徽标接线归主线集成）
        });
        _ = _store.DeleteAsync();
        InterruptedBanner.Text = AppResources.InterruptedRun;
        InterruptedBanner.IsVisible = true;
    }

    /// <summary>通知「暂停/继续」按钮：与页面按钮同一处理（回调来自 Android 主线程，仍切 UI 线程稳妥）。</summary>
    private void OnRemotePauseToggled()
        => Dispatcher.Dispatch(() => { if (_running) OnPauseResume(this, EventArgs.Empty); });

    /// <summary>通知「停止」按钮：用户明确意图，直接取消留痕（不弹确认）。</summary>
    private void OnRemoteStopRequested()
        => Dispatcher.Dispatch(() => { if (_running) EndRun("cancelled"); });

    /// <summary>「识别有误，停止并修改」：停止（cancelled 留痕）→ 带当前计划跳编辑器（DEV-04 接线）。</summary>
    private async void OnFixMistake(object sender, EventArgs e)
    {
        if (!_running) return;
        bool ok = await DisplayAlert(AppResources.FixConfirmTitle, AppResources.FixConfirmText,
            AppResources.StopTimer, AppResources.Cancel);
        if (!ok) return;
        TimerLauncher.PendingEditPlan = _runPlan; // 编辑器当前无查询参数路由，取值方式待 DEV-04 入口确认
        EndRun("cancelled");
        await Shell.Current.GoToAsync("TimerEditorPage");
    }
}
