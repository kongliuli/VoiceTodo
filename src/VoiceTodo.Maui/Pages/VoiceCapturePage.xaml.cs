using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls.Shapes;
using VoiceTodo.Core.Abstractions;
using VoiceTodo.Core.Models;
using VoiceTodo.Core.Services;
using VoiceTodo.Maui.PlatformServices;
using VoiceTodo.Maui.Resources;
using VoiceTodo.Maui.Services;
using WaveBarShape = Microsoft.Maui.Controls.Shapes.Rectangle;

#if WINDOWS
using WindowsAccess = Windows.Security.Authorization.AppCapabilityAccess;
#endif

namespace VoiceTodo.Maui.Pages;

/// <summary>
/// 语音录入流：权限（等待授权/被拒/不可用）→ 聆听（麦克风 + 波形 + 计时）→ 识别（whisper）→ 确认记录（解析草稿可改）→ 添加待办。
/// DEV-03 状态机：等待授权 / 正在录音（采集真正开始才显示，无流式转写不做实时字幕暗示）/ 正在整理 / 待确认 /
/// 未听清（保留已获文本为草稿 + 重试/改文字）/ 麦克风不可用。
/// 「完成本句」= 停止并识别（保留音频）；「取消本句」= 丢弃本次并回到可再次录音；两个入口独立，不共用取消令牌。
/// 录音最长 60s 自动结束；权限永久拒绝不重复弹申请，引导系统设置 + 改用文字。
/// </summary>
public partial class VoiceCapturePage : ContentPage
{
    private enum CaptureState { AwaitingPermission, MicDenied, MicUnavailable, Listening, Recognizing, Confirm, FollowUp, Error }

    /// <summary>AccessPanel 两种模式：被拒（去授权）或不可用（再试）。</summary>
    private enum AccessMode { Denied, Unavailable }

    private const int WaveCount = 10;
    private static readonly TimeSpan MaxDuration = TimeSpan.FromSeconds(60);

    private readonly IMicrophoneCapture _capture;
    private readonly ISpeechRecognizer _recognizer;
    private readonly IIntentParser _intent;
    private readonly ITodoRepository _repo;
    private readonly ITodoChangeDispatcher _dispatcher; // DEV-02：确认添加走统一变更入口（落库+排提醒）

    private CaptureState _state = CaptureState.AwaitingPermission;
    private AccessMode _accessMode = AccessMode.Denied;
    private CancellationTokenSource? _cts;
    private Task<string> _wavTask = Task.FromResult("");
    private IDispatcherTimer? _uiTimer;
    private int _elapsedSeconds;
    private bool _hasStarted;   // OnAppearing 可能多次触发（TextAddPage/系统设置返回），控制自动流程
    private bool _finalizing;   // 防止完成/超时双触发
    private bool _leaving;      // 已决定离开页面，跳过 tick 触发的收尾
    private bool _pickingProgrammatic; // 覆盖层 Picker 程序赋值时屏蔽事件
    private string _overlayKind = "date";
    private readonly List<WaveBarShape> _waveBars = new();
    private readonly Random _rand = new();

    // 确认页草稿（可被覆盖层修改）
    private string _draftTitle = "";
    private DateTime? _draftDate;
    private TimeSpan? _draftTime;
    private bool _draftIsRecurring;
    private string? _draftRule;
    private TimeSpan? _draftPreAlert;
    private VoiceCommand? _draftCommand; // 解析结果草稿（Timer 走「先确认再跑」通路，D3）

    // DEV-04：训练计划草稿（追问轮字段级覆盖保留已识别字段）+ 最终文本去重（同结果不重复创建）
    private TrainingPlan? _draftPlan;
    private List<string> _draftMissing = new();
    private string _lastUtterance = "";
    private string? _lastCreatedHash;

    public VoiceCapturePage()
    {
        InitializeComponent();
        var services = Application.Current?.Handler?.MauiContext?.Services
            ?? throw new InvalidOperationException("MauiContext 未就绪");
        _capture = services.GetRequiredService<IMicrophoneCapture>();
        _recognizer = services.GetRequiredService<ISpeechRecognizer>();
        _intent = services.GetRequiredService<IIntentParser>();
        _repo = services.GetRequiredService<ITodoRepository>();
        _dispatcher = services.GetRequiredService<ITodoChangeDispatcher>();

        BuildWave();
        ApplyTexts();
        ShowState(CaptureState.AwaitingPermission);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // 常驻会话：会话仍开启且有在飞录音 → 接管继续（页面重建也不丢当前这一句）
        if (TryAdoptResidentCapture()) return;
        if (!_hasStarted)
        {
            _hasStarted = true;
            _ = StartFlowAsync();
        }
        else if (_state is CaptureState.MicDenied or CaptureState.MicUnavailable)
        {
            // 从系统设置返回：权限可能已开启（已授权时 Request 不再弹框），重新走一遍流程即可恢复
            _ = StartFlowAsync();
        }
    }

    /// <summary>
    /// 接管会话内已有的在飞录音：未完成 → 恢复「正在录音」视图与计时；已完成 → 直接进入识别。
    /// 返回 true 表示已接管，无需重新走 StartFlow。
    /// </summary>
    private bool TryAdoptResidentCapture()
    {
        if (!VoiceSessionController.IsActive) return false;
        var pending = VoiceSessionController.PendingWav;
        if (pending is null) return false;

        _wavTask = pending;
        _cts = VoiceSessionController.CaptureCts;
        _elapsedSeconds = VoiceSessionController.ElapsedSeconds;
        _hasStarted = true;
        _leaving = false;

        if (pending.IsCompleted)
        {
            _finalizing = false;
            _ = FinalizeRecognitionAsync(); // 离开期间录制已收口 → 直接识别
        }
        else
        {
            TimerLabel.Text = TimeSpan.FromSeconds(_elapsedSeconds).ToString(@"mm\:ss");
            ShowState(CaptureState.Listening);
            StartTimer();
        }
        return true;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        StopTimer();
        // 常驻会话：离开页面不取消本句（会话与在飞录音由 VoiceSessionController 维持），
        // 只有显式「结束会话」才终止（见 DEV-06）。
        if (VoiceSessionController.IsActive) return;

        if (!_wavTask.IsCompleted)
        {
            _leaving = true;
            // 离开页面 = 放弃本句：丢弃文件，不返回数据
            try { _ = _capture.CancelAsync(); } catch { }
            try { _cts?.Cancel(); } catch { }
        }
    }

    // ══════════ 文案 ══════════

    private void ApplyTexts()
    {
        TitleLabel.Text = AppResources.VoiceRecordTitle;
        BackBtn.Text = "✕";
        CapsuleLabel.Text = AppResources.ListeningNow;
        SpeakHintLabel.Text = AppResources.SpeakHint;
        FinishLabel.Text = AppResources.FinishSentence;      // 完成本句
        SwitchBtn.Text = AppResources.SwitchToType;
        CancelBtn.Text = AppResources.DiscardSentence;       // 取消本句
        EndSessionBtn.Text = AppResources.EndSession;        // 结束会话（常驻会话生命周期终点）
        AccessLabel.Text = AppResources.MicAwaitText;
        AccessSwitchBtn.Text = AppResources.SwitchToType;
        AccessPrimaryWrap.IsVisible = AccessSwitchBtn.IsVisible = false; // 申请中隐藏按钮

        YouSaidLabel.Text = AppResources.YouSaid;
        BannerLabel.Text = AppResources.OrganizedBanner;
        ItemLabel.Text = "📝 " + AppResources.ItemLabel;
        DateLabel.Text = "📅 " + AppResources.FieldDate;
        TimeLabel.Text = "🕐 " + AppResources.FieldTime;
        ReminderLabel.Text = "🔔 " + AppResources.FieldReminder;
        RepeatLabel.Text = "🔁 " + AppResources.FieldRepeat;
        TapHintLabel.Text = AppResources.TapRowHint;
        ConfirmBtn.Text = AppResources.ConfirmAdd;
        SayAgainBtn.Text = AppResources.SayAgain;
        ErrorLabel.Text = AppResources.VoiceFailText;
        ErrorDraftLabel.IsVisible = false;
        RetryBtn.Text = AppResources.SayAgain;
        ErrorSwitchBtn.Text = AppResources.SwitchToType;

        // DEV-04 追问态文案
        YouSaidFollowLabel.Text = AppResources.YouSaid;
        PlanActionRowLabel.Text = "🏃 " + AppResources.ActionNameLabel;
        PlanRoundsRowLabel.Text = "🔁 " + AppResources.PlanMissingRoundsCap;
        PlanWorkRowLabel.Text = "💪 " + AppResources.PhaseWorkShort;
        PlanRestRowLabel.Text = "🧘 " + AppResources.PhaseRestShort;
        FollowUpHintLabel.Text = AppResources.PlanFollowUpHint;
        FollowSpeakBtn.Text = AppResources.PlanSpeakAgain;
        FollowEditBtn.Text = AppResources.PlanManualEdit;

        OverlayOkBtn.Text = AppResources.OK;
    }

    // ══════════ 状态面板 ══════════

    private void ShowState(CaptureState state)
    {
        _state = state;
        ListeningPanel.IsVisible = state is CaptureState.Listening or CaptureState.Recognizing;
        ConfirmPanel.IsVisible = state == CaptureState.Confirm;
        FollowUpPanel.IsVisible = state == CaptureState.FollowUp; // DEV-04 追问态
        ErrorPanel.IsVisible = state == CaptureState.Error;
        AccessPanel.IsVisible = state is CaptureState.AwaitingPermission
            or CaptureState.MicDenied or CaptureState.MicUnavailable;

        switch (state)
        {
            case CaptureState.AwaitingPermission:
                // 等待授权：只提示，不显示动作按钮（申请中）
                AccessLabel.Text = AppResources.MicAwaitText;
                AccessPrimaryWrap.IsVisible = AccessSwitchBtn.IsVisible = false;
                break;
            case CaptureState.MicDenied:
                // 被拒（含永久拒绝）：不重复弹申请，引导系统设置 + 改用文字
                _accessMode = AccessMode.Denied;
                AccessLabel.Text = AppResources.MicDeniedText;
                AccessPrimaryBtn.Text = AppResources.MicOpenSettings;
                AccessPrimaryWrap.IsVisible = AccessSwitchBtn.IsVisible = true;
                break;
            case CaptureState.MicUnavailable:
                // 麦克风不可用（被占用 / 初始化失败）：可重试
                _accessMode = AccessMode.Unavailable;
                AccessLabel.Text = AppResources.MicUnavailableText;
                AccessPrimaryBtn.Text = AppResources.MicRetry;
                AccessPrimaryWrap.IsVisible = AccessSwitchBtn.IsVisible = true;
                break;
        }

        TitleLabel.Text = state switch
        {
            CaptureState.Confirm => AppResources.ConfirmRecordTitle,
            CaptureState.FollowUp => AppResources.PlanFollowUpTitle, // DEV-04 追问态标题
            _ => AppResources.VoiceRecordTitle
        };
        BackBtn.Text = state is CaptureState.Confirm or CaptureState.FollowUp ? "←" : "✕";
    }

    // ══════════ 权限（DEV-03 三分支） ══════════

    /// <summary>进入页面/开始录音前申请麦克风权限：Granted → 录音；Denied/Disabled → 明确状态页引导。
    /// 常驻会话内已授权则直接放行，避免每次回到页面重复弹申请。</summary>
    private async Task<bool> EnsureMicPermissionAsync()
    {
        if (VoiceSessionController.PermissionGranted) return true;
        bool granted;
#if ANDROID
        try
        {
            granted = await Permissions.RequestAsync<Permissions.Microphone>() == PermissionStatus.Granted;
        }
        catch
        {
            granted = false;
        }
#else
        granted = await CheckWindowsMicAccessAsync();
#endif
        if (granted) VoiceSessionController.PermissionGranted = true;
        return granted;
    }

#if WINDOWS
    /// <summary>Windows：系统麦克风隐私 AppCapability 检查/申请；UserPromptRequired 弹系统框，其余拒绝走引导。</summary>
    private static async Task<bool> CheckWindowsMicAccessAsync()
    {
        try
        {
            var cap = WindowsAccess.AppCapability.Create("microphone");
            var access = cap.CheckAccess();
            if (access == WindowsAccess.AppCapabilityAccessStatus.Allowed) return true;
            if (access == WindowsAccess.AppCapabilityAccessStatus.UserPromptRequired)
            {
                var result = await cap.RequestAccessAsync();
                return result == WindowsAccess.AppCapabilityAccessStatus.Allowed;
            }
            return false; // DenyedBySystem 等 → 引导系统设置
        }
        catch
        {
            return false;
        }
    }
#else
    private static Task<bool> CheckWindowsMicAccessAsync() => Task.FromResult(false);
#endif

    // ══════════ 录音 → 识别 → 确认 时序 ══════════

    /// <summary>页面入口流程：先过权限，再启动采集。</summary>
    private async Task StartFlowAsync()
    {
        if (_finalizing) return;
        _leaving = false;
        ShowState(CaptureState.AwaitingPermission);
        var granted = await EnsureMicPermissionAsync();
        if (_leaving || _state is CaptureState.Confirm or CaptureState.FollowUp or CaptureState.Error) return;
        if (!granted)
        {
            ShowState(CaptureState.MicDenied);
            return;
        }
        await StartListeningAsync();
    }

    /// <summary>开始（或重新）聆听：启动 60s 超时 CTS + 不阻塞的采集 Task + 1s UI 计时器。
    /// 只有采集真正开始（启动未立即失败）才显示「正在录音」。</summary>
    private async Task StartListeningAsync()
    {
        _finalizing = false;
        _leaving = false;
        _elapsedSeconds = 0;
        TimerLabel.Text = "00:00";
        LiveText.Text = "…"; // 无流式转写：仅占位，不做实时字幕暗示
        CapsuleLabel.Text = AppResources.ListeningNow;

        VoiceSessionController.Start(); // 常驻会话开启（跨页面维持；结束会话才关闭）

        try { _cts?.Cancel(); } catch { }
        try { await _capture.CancelAsync(); } catch { } // 上一轮若仍存活则丢弃
        _cts = new CancellationTokenSource(MaxDuration); // 60s 到点自动结束（保留文件）
        _wavTask = _capture.CaptureWaveFileAsync(TimeSpan.FromSeconds(60), _cts.Token);
        VoiceSessionController.TrackCapture(_wavTask, _cts); // 登记在飞句柄，供页面重建后接管

        // 探测启动失败（权限/占用等会很快以空结果收口）；1.5s 内正常未完成即视为采集已开始
        var done = await Task.WhenAny(_wavTask, Task.Delay(1500));
        if (_leaving) return;
        if (done == _wavTask)
        {
            VoiceSessionController.ClearCapture();
            ShowState(CaptureState.MicUnavailable);
            return;
        }
        ShowState(CaptureState.Listening);
        StartTimer();
    }

    private void StartTimer()
    {
        StopTimer();
        _uiTimer = Dispatcher.CreateTimer();
        _uiTimer.Interval = TimeSpan.FromSeconds(1);
        _uiTimer.IsRepeating = true;
        _uiTimer.Tick += OnUiTimerTick;
        _uiTimer.Start();
    }

    private void StopTimer()
    {
        if (_uiTimer is null) return;
        _uiTimer.Stop();
        _uiTimer.Tick -= OnUiTimerTick;
        _uiTimer = null;
    }

    private void OnUiTimerTick(object? sender, EventArgs e)
    {
        if (_leaving || _finalizing || _state != CaptureState.Listening) return;
        if (_cts is { IsCancellationRequested: true })
        {
            _ = FinalizeRecognitionAsync(); // 60s 超时收尾（保留已录音频）
            return;
        }
        _elapsedSeconds++;
        TimerLabel.Text = TimeSpan.FromSeconds(_elapsedSeconds).ToString(@"mm\:ss");
        VoiceSessionController.ElapsedSeconds = _elapsedSeconds; // 会话内记录，页面重建后续接
        AnimateWave();
    }

    /// <summary>波形条：随机目标 8..56，向目标插值抖动；UseAnimations=false 时保持静态。</summary>
    private void AnimateWave()
    {
        if (!AppSettings.UseAnimations) return;
        for (int i = 0; i < _waveBars.Count; i++)
        {
            double target = _rand.Next(8, 57);
            double current = _waveBars[i].HeightRequest;
            _waveBars[i].HeightRequest = Math.Abs(target - current) < 1
                ? target
                : current + (target - current) * 0.6;
        }
    }

    private void BuildWave()
    {
        var brush = new SolidColorBrush((Color)Application.Current!.Resources["Primary"]);
        for (int i = 0; i < WaveCount; i++)
        {
            var bar = new WaveBarShape
            {
                WidthRequest = 4,
                RadiusX = 2,
                RadiusY = 2,
                Fill = brush,
                VerticalOptions = LayoutOptions.Center,
                HeightRequest = 8 + (i * 48 / (WaveCount - 1)) // 静态初始梯度 8..56
            };
            _waveBars.Add(bar);
            WaveHost.Children.Add(bar);
        }
    }

    /// <summary>完成/超时后：等 WAV → 识别 → 解析 → 确认 / 未听清（保留草稿文本）。</summary>
    private async Task FinalizeRecognitionAsync()
    {
        if (_finalizing) return;
        _finalizing = true;
        StopTimer();
        VoiceSessionController.ClearCapture(); // 本句已由本页接管收口，取消“在飞句柄”登记

        ShowState(CaptureState.Recognizing); // 正在整理（识别中）

        string? wav = null;
        try
        {
            wav = await _wavTask; // 空字符串 = 已取消/失败，真实现超时取消收口为保留路径
        }
        catch { wav = ""; }

        string text = "";
        if (!string.IsNullOrEmpty(wav) && File.Exists(wav))
        {
            try
            {
                text = await _recognizer.RecognizeFileAsync(wav);
            }
            catch { text = ""; }
        }

        _finalizing = false;
        if (_leaving) return;

        if (string.IsNullOrWhiteSpace(text))
        {
            ShowError(""); // 未听清
            return;
        }

        // DEV-04：训练计划口述 —— 完整→自动落库并 RequestStart 直通开始（05：不进确认页）；
        // 缺槽→追问态（已识别字段保留 + 缺口提示，补说或手动补齐）；修正句与上一轮草稿字段级覆盖。
        var probe = _intent.Parse(text);
        if (probe.Type == CommandType.Timer && probe.Action == CommandAction.Add && probe.Plan is { } plan)
        {
            _draftPlan = MergePlanDraft(_draftPlan, plan, probe.MissingSlots, _draftMissing, out var mergedMissing);
            _draftMissing = mergedMissing;
            _lastUtterance = text;
            QuotedFollowText.Text = $"“{text}”";

            if (_draftPlan.IsComplete && _draftMissing.Count == 0)
            {
                // 去重：同一最终识别文本不重复创建（页面级记录上次已创建 hash）
                var hash = UtteranceHash(text);
                if (hash == _lastCreatedHash)
                {
                    // 重复最终文本：不启动第二次，直接进入确认态让用户显式处理
                    BuildDraft(text);
                    ShowState(CaptureState.Confirm);
                    return;
                }
                _lastCreatedHash = hash;

                _leaving = true;
                StopTimer();
                var item = PlanToTimerItem(_draftPlan);
                try
                {
                    await _repo.AddTimerAsync(item);
                }
                catch (StorageException ex)
                {
                    _leaving = false; // 保存失败：留在本页，草稿仍可重试
                    await UserAlerts.ShowStorageErrorAsync(ex.Message);
                    return;
                }
                TimerLauncher.RequestStart(item, autoStart: true); // 完整有效指令 → 直接开跑（05 反馈页=运行态）
                if (Shell.Current is not null) await Shell.Current.GoToAsync("//TimerPage");
                return;
            }

            // 缺槽：追问态（展示已识别字段 + 缺口提示）
            RefreshFollowUp();
            ShowState(CaptureState.FollowUp);
            return;
        }

        BuildDraft(text);
        ShowState(CaptureState.Confirm);
    }

    /// <summary>识别失败页：保留已获得文本为草稿（无文本时隐藏），可重试或改用文字。</summary>
    private void ShowError(string rawText)
    {
        var draft = rawText?.Trim() ?? "";
        ErrorDraftLabel.Text = string.Format(AppResources.ErrorDraftPrefix, draft);
        ErrorDraftLabel.IsVisible = draft.Length > 0;
        ShowState(CaptureState.Error);
    }

    private void BuildDraft(string text)
    {
        var cmd = _intent.Parse(text);
        _draftCommand = cmd;
        _draftTitle = !string.IsNullOrWhiteSpace(cmd.Title) ? cmd.Title! : text;
        if (cmd.TriggerAt is { } t)
        {
            _draftDate = t.DateTime.Date;
            _draftTime = t.DateTime.TimeOfDay;
        }
        else
        {
            _draftDate = null;
            _draftTime = null;
        }
        _draftIsRecurring = cmd.IsRecurring;
        _draftRule = cmd.IsRecurring ? cmd.RecurrenceRule : null;
        _draftPreAlert = null;

        // DEV-04：完整计划草稿（直通/确认共用）；非计划口述清除旧计划草稿
        _draftPlan = cmd.Plan is { IsComplete: true } ? cmd.Plan : null;
        _draftMissing = cmd.MissingSlots != null ? new List<string>(cmd.MissingSlots) : new();
        _lastUtterance = text;

        QuotedText.Text = $"“{text}”";
        RefreshConfirmRows();
    }

    // ══════════ DEV-04：统一计划追问与直通 ══════════

    /// <summary>
    /// 追问轮字段级覆盖：新口述只更新其明确给出的字段，已识别字段保留（05）；
    /// 「不是10组是8组」类修正句由解析器产出修正值，随 provided.Rounds 覆盖。
    /// 缺口收敛为「旧缺口 ∪ 新缺口 − 已补齐字段」。
    /// </summary>
    private static TrainingPlan? MergePlanDraft(TrainingPlan? draft, TrainingPlan provided,
        List<string> newMissing, List<string> oldMissing, out List<string> mergedMissing)
    {
        if (draft is null)
        {
            mergedMissing = new List<string>(newMissing);
            return provided;
        }

        var merged = new TrainingPlan
        {
            ActionName = draft.ActionName,
            Rounds = draft.Rounds,
            WorkDuration = draft.WorkDuration,
            RestDuration = draft.RestDuration,
            RestAfterLastRound = draft.RestAfterLastRound,
            CountdownWindowSeconds = draft.CountdownWindowSeconds,
            PhaseAnnouncements = draft.PhaseAnnouncements
        };
        var missing = new HashSet<string>(oldMissing);
        foreach (var m in newMissing) missing.Add(m);

        // 覆盖新口述明确给出的字段（休息仅在明确说出且非歧义时覆盖，避免把已识别休息清零）
        bool roundsGiven = !newMissing.Contains(RuleBasedIntentParser.SlotRounds)
                           && !newMissing.Contains(RuleBasedIntentParser.SlotCount)
                           && provided.Rounds >= 1;
        bool actionGiven = !newMissing.Contains(RuleBasedIntentParser.SlotAction)
                           && !string.IsNullOrWhiteSpace(provided.ActionName);
        bool workGiven = !newMissing.Contains(RuleBasedIntentParser.SlotWork)
                         && provided.WorkDuration > TimeSpan.Zero;
        bool restGiven = provided.RestDuration > TimeSpan.Zero;

        if (roundsGiven) merged.Rounds = provided.Rounds;
        if (actionGiven) merged.ActionName = provided.ActionName;
        if (workGiven) merged.WorkDuration = provided.WorkDuration;
        if (restGiven) merged.RestDuration = provided.RestDuration;

        // 收敛缺口：草稿现有字段可消除的缺口移除（「count」在补出组数后即解决）
        if (!string.IsNullOrWhiteSpace(merged.ActionName)) missing.Remove(RuleBasedIntentParser.SlotAction);
        if (merged.Rounds >= 1)
        {
            missing.Remove(RuleBasedIntentParser.SlotRounds);
            missing.Remove(RuleBasedIntentParser.SlotCount);
        }
        if (merged.WorkDuration > TimeSpan.Zero) missing.Remove(RuleBasedIntentParser.SlotWork);
        if (restGiven) missing.Remove(RuleBasedIntentParser.SlotRest);

        mergedMissing = missing.ToList();
        return merged;
    }

    /// <summary>最终识别文本 hash（SHA-256），页面级去重：同一结果不重复创建。</summary>
    private static string UtteranceHash(string text)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text.Trim()));
        return Convert.ToHexString(bytes);
    }

    /// <summary>完整计划 → TimerItem：Title=动作名、Duration=计划总时长、Phases 由计划展开。</summary>
    private TimerItem PlanToTimerItem(TrainingPlan plan) => new()
    {
        Title = !string.IsNullOrWhiteSpace(plan.ActionName) ? plan.ActionName : _draftTitle,
        Duration = plan.TotalDuration,
        TriggerAt = DateTimeOffset.Now,
        Phases = plan.Expand(),
        Plan = plan
    };

    /// <summary>追问态：展示已识别字段（缺的显示「—」）+ 缺口提示。</summary>
    private void RefreshFollowUp()
    {
        var p = _draftPlan;
        PlanActionRowValue.Text = string.IsNullOrWhiteSpace(p?.ActionName) ? "—" : p.ActionName;
        PlanRoundsRowValue.Text = p is { Rounds: >= 1 }
            ? string.Format(AppResources.RoundsCount, p.Rounds) : "—";
        PlanWorkRowValue.Text = p is { WorkDuration.TotalSeconds: > 0 } ? FmtDur(p.WorkDuration) : "—";
        PlanRestRowValue.Text = p is { RestDuration.TotalSeconds: > 0 } ? FmtDur(p.RestDuration) : "—";
        MissingLabel.Text = BuildMissingText(_draftMissing);
    }

    /// <summary>缺口提示文本：「还缺：组数、每组运动时长」；计次语法先说明不支持。</summary>
    private string BuildMissingText(List<string> slots)
    {
        var labels = slots
            .Where(s => s != RuleBasedIntentParser.SlotCount)
            .Select(s => s switch
            {
                RuleBasedIntentParser.SlotAction => AppResources.PlanMissingAction,
                RuleBasedIntentParser.SlotRounds => AppResources.PlanMissingRounds,
                RuleBasedIntentParser.SlotWork => AppResources.PlanMissingWork,
                RuleBasedIntentParser.SlotRest => AppResources.PlanMissingRest,
                _ => ""
            })
            .Where(l => l.Length > 0)
            .ToList();
        if (slots.Contains(RuleBasedIntentParser.SlotCount))
        {
            var hint = AppResources.PlanCountHint;
            if (labels.Count > 0) hint += " " + string.Format(AppResources.PlanMissingFormat, string.Join("、", labels));
            return hint;
        }
        return string.Format(AppResources.PlanMissingFormat, string.Join("、", labels));
    }

    /// <summary>追问态「手动补齐」：带查询参数跳计时编辑器预填（08 通用计划编辑器承接）。</summary>
    private async void OnFollowUpEdit(object sender, EventArgs e)
    {
        _leaving = true;
        StopTimer();
        var p = _draftPlan;
        var query = $"TimerEditorPage?name={Uri.EscapeDataString(p?.ActionName ?? "")}"
                    + $"&rounds={(p is { Rounds: >= 1 } ? p.Rounds : 0)}"
                    + $"&work={(int)(p?.WorkDuration.TotalSeconds ?? 0)}"
                    + $"&rest={(int)(p?.RestDuration.TotalSeconds ?? 0)}"
                    + $"&restlast={(p?.RestAfterLastRound == true ? 1 : 0)}";
        if (Shell.Current is not null) await Shell.Current.GoToAsync(query);
    }

    // ══════════ 确认页字段行 ══════════

    /// <summary>当前草稿是否为可直接运行的计时（Timer + 计划/Phases/Duration，D3：先确认再跑）。</summary>
    private bool IsTimerDraft(out VoiceCommand cmd)
    {
        cmd = _draftCommand ?? new VoiceCommand();
        bool hasPlan = _draftPlan is { IsComplete: true } && cmd.Plan is not null; // DEV-04：合并后的完整计划草稿
        return cmd.Type == CommandType.Timer && cmd.Action == CommandAction.Add
            && (hasPlan || cmd.Phases is { Count: > 0 } || cmd.Duration is { } d && d > TimeSpan.Zero);
    }

    private void RefreshConfirmRows()
    {
        bool isTimer = IsTimerDraft(out var timerCmd);
        ItemValue.Text = _draftTitle;
        DateValue.Text = FmtDate(_draftDate);
        TimeValue.Text = _draftTime?.ToString(@"hh\:mm") ?? "--:--";
        ReminderValue.Text = _draftPreAlert switch
        {
            null => AppResources.OnTimeRemind,
            { TotalMinutes: 10 } => AppResources.Early10,
            { TotalMinutes: 30 } => AppResources.Early30,
            _ => _draftPreAlert.Value.ToString()
        };
        RepeatValue.Text = RepeatText();

        if (isTimer)
        {
            // 计时草稿：隐藏日期/时间/提醒行，「重复」行改为间歇计划摘要（计划不可经覆盖层编辑）
            BannerLabel.Text = AppResources.OrganizedTimerBanner;
            DateRow.IsVisible = TimeRow.IsVisible = ReminderRow.IsVisible = false;
            RepeatRow.InputTransparent = true;
            RepeatLabel.Text = "⏱ " + AppResources.FieldPlan;
            RepeatValue.Text = TimerSummary(timerCmd);
        }
        else
        {
            BannerLabel.Text = AppResources.OrganizedBanner;
            DateRow.IsVisible = TimeRow.IsVisible = ReminderRow.IsVisible = true;
            RepeatRow.InputTransparent = false;
            RepeatLabel.Text = "🔁 " + AppResources.FieldRepeat;
        }
        RefreshRowSeparators();
    }

    /// <summary>计时草稿摘要：优先计划快照「动作 · N 组 · 运动 X · 休息 Y · 共 T」；倒计时则显示「共 X」。</summary>
    private string TimerSummary(VoiceCommand cmd)
    {
        // DEV-04：统一计划摘要（追问轮合并后的计划以 _draftPlan 为准）
        if (_draftPlan is { } p)
        {
            var text = $"{p.ActionName} · {string.Format(AppResources.RoundsCount, p.Rounds)}"
                + $" · {AppResources.PhaseWorkShort} {FmtDur(p.WorkDuration)}";
            if (p.RestDuration > TimeSpan.Zero)
                text += $" · {AppResources.PhaseRestShort} {FmtDur(p.RestDuration)}";
            return text + $" · {string.Format(AppResources.TotalLabel, FmtDur(p.TotalDuration))}";
        }
        if (cmd.Phases is { Count: > 0 })
        {
            var work = cmd.Phases[0];
            var rest = cmd.Phases.Count > 1 ? cmd.Phases[1] : null;
            string text = $"{string.Format(AppResources.RoundsCount, Math.Max(1, work.Rounds))}"
                + $" · {AppResources.PhaseWorkShort} {FmtDur(work.Duration)}";
            if (rest is not null)
                text += $" · {AppResources.PhaseRestShort} {FmtDur(rest.Duration)}";
            return text;
        }
        return string.Format(AppResources.TotalLabel, FmtDur(cmd.Duration ?? TimeSpan.Zero));
    }

    /// <summary>时长 →「X 分钟」/「X分Y秒」（与计时页口径一致；非中文环境「Xm YYs」）。</summary>
    private static string FmtDur(TimeSpan t)
    {
        t = t.Duration();
        int m = (int)t.TotalMinutes;
        int s = t.Seconds;
        if (s == 0) return string.Format(AppResources.QuickMinFormat, m);
        bool zh = System.Globalization.CultureInfo.CurrentCulture.TwoLetterISOLanguageName == "zh";
        return zh ? (m > 0 ? $"{m}分{s}秒" : $"{s}秒") : (m > 0 ? $"{m}m {s:00}s" : $"{s}s");
    }

    /// <summary>行分隔线随行的显隐同步：仅保留两个相邻可见行之间的分隔（事项 | s0 | 日期 | s1 | 时间 | s2 | 提醒 | s3 | 计划）。</summary>
    private void RefreshRowSeparators()
    {
        if (ItemRow.Parent is not VerticalStackLayout host) return;
        var seps = host.Children.OfType<BoxView>().ToList();
        if (seps.Count < 4) return;
        seps[0].IsVisible = ItemRow.IsVisible && DateRow.IsVisible;
        seps[1].IsVisible = DateRow.IsVisible && TimeRow.IsVisible;
        seps[2].IsVisible = TimeRow.IsVisible && ReminderRow.IsVisible;
        seps[3].IsVisible = ReminderRow.IsVisible && RepeatRow.IsVisible;
    }

    private string RepeatText()
    {
        if (!_draftIsRecurring || string.IsNullOrWhiteSpace(_draftRule)) return AppResources.RepeatNone;
        var rule = _draftRule!.ToLowerInvariant();
        if (rule.Contains("week")) return AppResources.RepeatWeekly;
        if (rule.Contains("day")) return AppResources.RepeatDaily;
        return _draftRule;
    }

    private static string FmtDate(DateTime? d)
    {
        if (d is null) return AppResources.NoDueLabel;
        var date = d.Value.Date;
        string md = $"{date.Month}月{date.Day}日";
        var today = DateTime.Today;
        if (date == today) return $"{AppResources.TodayWord}，{md}";
        if (date == today.AddDays(1)) return $"{AppResources.TomorrowWord}，{md}";
        return md;
    }

    private static Color ResColor(string key) => (Color)Application.Current!.Resources[key];

    // ══════════ 覆盖层（日期 / 时间 / 提醒 / 重复 / 事项） ══════════

    private void ShowOverlay(string kind)
    {
        _overlayKind = kind;
        OverlayTitle.Text = kind switch
        {
            "title" => AppResources.ItemLabel,
            "date" => AppResources.FieldDate,
            "time" => AppResources.FieldTime,
            "reminder" => AppResources.FieldReminder,
            _ => AppResources.FieldRepeat
        };
        OverlayEditor.IsVisible = kind == "title";
        OverlayDatePicker.IsVisible = kind == "date";
        OverlayTimePicker.IsVisible = kind == "time";
        OverlayOptions.IsVisible = kind is "reminder" or "repeat";

        if (kind == "title") OverlayEditor.Text = _draftTitle;
        if (kind == "date")
        {
            _pickingProgrammatic = true;
            OverlayDatePicker.Date = _draftDate ?? DateTime.Today;
            _pickingProgrammatic = false;
        }
        if (kind == "time")
        {
            _pickingProgrammatic = true;
            OverlayTimePicker.Time = _draftTime ?? new TimeSpan(9, 0, 0);
            _pickingProgrammatic = false;
        }
        if (kind == "reminder")
        {
            BuildOptions(new (string, TimeSpan?)[]
            {
                (AppResources.OnTimeRemind, null),
                (AppResources.Early10, TimeSpan.FromMinutes(10)),
                (AppResources.Early30, TimeSpan.FromMinutes(30))
            }, _draftPreAlert);
        }
        if (kind == "repeat")
        {
            BuildOptions(new (string, string?)[]
            {
                (AppResources.RepeatNone, null),
                (AppResources.RepeatDaily, "every day"),
                (AppResources.RepeatWeekly, "every week")
            }, _draftIsRecurring ? _draftRule : null);
        }
        Overlay.IsVisible = true;
    }

    private void BuildOptions<T>((string Label, T Value)[] options, T? current)
    {
        OverlayOptions.Children.Clear();
        foreach (var (label, value) in options)
        {
            bool selected = EqualityComparer<T>.Default.Equals(value, current);
            var btn = new Button
            {
                Text = label,
                BackgroundColor = selected ? ResColor("SelectedBg") : Colors.Transparent,
                TextColor = selected ? ResColor("Primary") : ResColor("TextPrimary"),
                BorderWidth = 0,
                CornerRadius = 8,
                Padding = new Thickness(12, 10),
                FontAttributes = FontAttributes.Bold,
                HorizontalOptions = LayoutOptions.Fill,
                CommandParameter = value
            };
            btn.Clicked += OnOverlayOptionClicked;
            OverlayOptions.Children.Add(btn);
        }
    }

    private void OnOverlayOptionClicked(object? sender, EventArgs e)
    {
        if (sender is not Button b) return;
        if (_overlayKind == "reminder")
        {
            _draftPreAlert = b.CommandParameter as TimeSpan?;
        }
        else if (_overlayKind == "repeat")
        {
            _draftRule = b.CommandParameter as string;
            _draftIsRecurring = _draftRule is not null;
        }
        Overlay.IsVisible = false;
        RefreshConfirmRows();
    }

    private void OnOverlayDatePicked(object? sender, DateChangedEventArgs e)
    {
        if (_pickingProgrammatic || _overlayKind != "date") return;
        _draftDate = e.NewDate;
        Overlay.IsVisible = false;
        RefreshConfirmRows();
    }

    private void OnOverlayTimePicked(object? sender, TimeChangedEventArgs e)
    {
        if (_pickingProgrammatic || _overlayKind != "time") return;
        _draftTime = e.NewTime;
        Overlay.IsVisible = false;
        RefreshConfirmRows();
    }

    private void OnOverlayClose(object? sender, EventArgs e)
    {
        if (_overlayKind == "title")
        {
            var edited = OverlayEditor.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(edited)) _draftTitle = edited;
            RefreshConfirmRows();
        }
        Overlay.IsVisible = false;
    }

    // ══════════ 行点击 ══════════

    private void OnEditItem(object? sender, TappedEventArgs e) => ShowOverlay("title");
    private void OnEditDate(object? sender, TappedEventArgs e) => ShowOverlay("date");
    private void OnEditTime(object? sender, TappedEventArgs e) => ShowOverlay("time");
    private void OnEditReminder(object? sender, TappedEventArgs e) => ShowOverlay("reminder");
    private void OnEditRepeat(object? sender, TappedEventArgs e) => ShowOverlay("repeat");

    // ══════════ 主操作 ══════════

    private async void OnConfirmAdd(object sender, EventArgs e)
    {
        // D3 先确认再跑：计时草稿不落待办 —— 存为常用/最近计时后直连计时页；取消则丢弃不落库
        if (IsTimerDraft(out var timerCmd))
        {
            var timer = MapTimerItem(timerCmd);
            try
            {
                await _repo.AddTimerAsync(timer);
            }
            catch (StorageException ex)
            {
                await UserAlerts.ShowStorageErrorAsync(ex.Message); // 保存失败：留在本页，不丢草稿
                return;
            }
            _leaving = true;
            StopTimer();
            TimerLauncher.RequestStart(timer, autoStart: true); // 用户已确认 → 直接开跑
            if (Shell.Current is not null) await Shell.Current.GoToAsync("//TimerPage");
            return;
        }

        DateTimeOffset? due = null;
        if (_draftDate.HasValue)
        {
            var time = _draftTime ?? TimeSpan.Zero;
            due = new DateTimeOffset(_draftDate.Value.Date + time);
        }
        var item = new TodoItem
        {
            Title = _draftTitle,
            DueAt = due,
            IsRecurring = _draftIsRecurring,
            RecurrenceRule = _draftIsRecurring ? _draftRule : null,
            Reminder = new ReminderSettings { PreAlert = _draftPreAlert }
        };
        // A1：仅当待办带提醒时间时，创建前确保通知权限（被拒不阻断保存，也绝不谎报）
        if (due is not null) await NotificationPermission.EnsureAsync();
        // DEV-02：走统一变更入口，落库与提醒排期同一闭环（不再直接调仓库、不绕过调度）
        TodoChangeResult result;
        try
        {
            result = await _dispatcher.CreateAsync(item);
        }
        catch (StorageException ex)
        {
            await UserAlerts.ShowStorageErrorAsync(ex.Message);
            return;
        }
        if (result.Saved)
        {
            bool notifGranted = await NotificationPermission.IsGrantedAsync();
            if (result.ReminderScheduled && notifGranted)
            {
                // 已安排且权限已授予：原行为不打扰，直接离开
            }
            else if (result.ReminderScheduled && !notifGranted)
            {
                await DisplayAlert(AppResources.ConfirmAdd, AppResources.Perm_RemindOffNoNotif, AppResources.OK);
            }
            else if (due is not null)
            {
                await DisplayAlert(AppResources.ConfirmAdd,
                    string.Format(AppResources.SavedRemindOffFormat, result.ReminderMessage ?? ""), AppResources.OK);
            }
        }
        await LeaveAsync();
    }

    /// <summary>计时草稿 → TimerItem：优先计划快照（Title=动作名、Duration=计划总时长、Phases 展开）；
    /// 旧路径存为常用/最近计时；Duration 为间歇总时长（供「最近使用」展示）。</summary>
    private TimerItem MapTimerItem(VoiceCommand cmd)
    {
        // DEV-04：完整计划草稿（含追问轮合并结果）→ 计划快照
        TrainingPlan? plan = cmd.Plan is { IsComplete: true } cp ? cp
            : _draftPlan is { IsComplete: true } dp ? dp : null;
        if (plan is not null)
            return PlanToTimerItem(plan);

        var duration = cmd.Duration ?? TimeSpan.Zero;
        if (cmd.Phases is { Count: > 0 })
        {
            duration = TimeSpan.Zero;
            foreach (var p in cmd.Phases)
                duration += TimeSpan.FromTicks(p.Duration.Ticks * Math.Max(1, p.Rounds));
        }
        return new TimerItem
        {
            Title = _draftTitle,
            Duration = duration,
            TriggerAt = DateTimeOffset.Now,
            Phases = cmd.Phases
        };
    }

    /// <summary>「完成本句」：停止并保留音频，走识别（与「取消本句」是两个独立入口/信号）。</summary>
    private void OnFinishRecord(object sender, EventArgs e)
    {
        if (_state != CaptureState.Listening || _finalizing) return;
        try { _ = _capture.StopAsync(); } catch { } // 结果统一经 _wavTask 收口
        _ = FinalizeRecognitionAsync();
    }

    /// <summary>「取消本句」：丢弃本次录音并回到可再次录音，会话可继续使用。</summary>
    private async void OnDiscardSentence(object sender, EventArgs e)
    {
        if (_state != CaptureState.Listening || _finalizing) return;
        _finalizing = true; // 阻止超时 tick 触发的收尾
        StopTimer();
        try { await _capture.CancelAsync(); } catch { }
        try { await _wavTask; } catch { } // 吸收收口结果/异常
        VoiceSessionController.ClearCapture(); // 本句作废，清空在飞句柄（会话保持）
        _finalizing = false;
        if (_leaving) return;
        await StartListeningAsync(); // 重新开录
    }

    /// <summary>「结束会话」：终止常驻会话（取消在飞录音、清空授权缓存）并离开录入页。</summary>
    private async void OnEndSession(object sender, EventArgs e)
    {
        _leaving = true;
        _finalizing = true;
        StopTimer();
        try { await _capture.CancelAsync(); } catch { }
        try { await _wavTask; } catch { }
        VoiceSessionController.End();
        if (Shell.Current is not null) await Shell.Current.GoToAsync("..");
    }

    private void OnSayAgain(object sender, EventArgs e)
        => _ = StartListeningAsync();

    /// <summary>AccessPanel 主按钮：被拒 → 引导系统设置（不重复弹申请）；不可用 → 直接重试。</summary>
    private async void OnAccessPrimary(object sender, EventArgs e)
    {
        if (_accessMode == AccessMode.Unavailable)
        {
            await StartListeningAsync();
            return;
        }
#if ANDROID
        try
        {
            var pkg = Android.App.Application.Context.PackageName;
            var intent = new Android.Content.Intent(
                Android.Provider.Settings.ActionApplicationDetailsSettings,
                Android.Net.Uri.Parse("package:" + pkg));
            intent.AddFlags(Android.Content.ActivityFlags.NewTask);
            Android.App.Application.Context.StartActivity(intent);
        }
        catch { }
#else
        try { await Launcher.OpenAsync("ms-settings:privacy-microphone"); } catch { }
#endif
    }

    private async void OnSwitchToType(object sender, EventArgs e)
    {
        _leaving = true;
        if (!_wavTask.IsCompleted)
        {
            // 切到文字页 = 放弃本句：丢弃录音（会话保持开启，稍后可回到语音继续）
            try { await _capture.CancelAsync(); } catch { }
            VoiceSessionController.ClearCapture();
        }
        StopTimer();
        if (Shell.Current is not null) await Shell.Current.GoToAsync("TextAddPage");
    }

    private async void OnBackClicked(object sender, EventArgs e)
        => await LeaveAsync();

    /// <summary>离开页面（顶部 ✕ / ←）：常驻会话下保留会话与在飞录音；未开启会话时才丢弃本句。</summary>
    private async Task LeaveAsync()
    {
        StopTimer();
        if (!VoiceSessionController.IsActive)
        {
            _leaving = true;
            if (!_wavTask.IsCompleted)
            {
                try { await _capture.CancelAsync(); } catch { }
            }
        }
        if (Shell.Current is not null) await Shell.Current.GoToAsync("..");
    }
}
