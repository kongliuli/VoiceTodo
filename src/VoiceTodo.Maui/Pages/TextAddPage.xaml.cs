using Microsoft.Extensions.DependencyInjection;
using VoiceTodo.Core.Abstractions;
using VoiceTodo.Core.Models;
using VoiceTodo.Core.Services;
using VoiceTodo.Maui.Resources;
using VoiceTodo.Maui.Services;

namespace VoiceTodo.Maui.Pages;

/// <summary>
/// 文字添加待办：Editor 输入 300ms 防抖 → IIntentParser 解析 → chips 展示识别结果；
/// 草稿行（事项/时间/重复）可经覆盖层修改；确认后经 TodoChangeDispatcher 落库并排提醒（DEV-02）。
/// DEV-04：识别为统一训练计划时 chips 展示 Plan.Describe()（或缺口提示）；完整计划确认 → 落库 + RequestStart 直通
/// （与口述同规则）；缺槽不猜测，提示后留在页面修改。
/// </summary>
public partial class TextAddPage : ContentPage
{
    private readonly IIntentParser _intent;
    private readonly ITodoChangeDispatcher _dispatcher;
    private readonly ITodoRepository _repo;

    private CancellationTokenSource? _debounceCts;
    private bool _pickingProgrammatic; // 覆盖层 Picker 程序赋值时屏蔽事件
    private string _overlayKind = "date";

    // 草稿（可被覆盖层修改）
    private string _draftTitle = "";
    private DateTime? _draftDate;
    private TimeSpan? _draftTime;
    private bool _draftIsRecurring;
    private string? _draftRule;

    // 手工修改保护（DEV-07 草稿保护）：用户改过的字段不再被后续解析覆盖
    private bool _titleEdited;
    private bool _dateEdited;
    private bool _timeEdited;
    private bool _repeatEdited;

    // DEV-04：计划解析草稿
    private TrainingPlan? _draftPlan;
    private List<string> _draftMissing = new();

    public TextAddPage()
    {
        InitializeComponent();
        var services = Application.Current?.Handler?.MauiContext?.Services
            ?? throw new InvalidOperationException("MauiContext 未就绪");
        _intent = services.GetRequiredService<IIntentParser>();
        _dispatcher = services.GetRequiredService<ITodoChangeDispatcher>();
        _repo = services.GetRequiredService<ITodoRepository>();

        ApplyTexts();
        RefreshRows();
    }

    private void ApplyTexts()
    {
        TitleLabel.Text = AppResources.TextAddTitle;
        InputEditor.Placeholder = AppResources.InputPlaceholder;
        RecognizedHeaderLabel.Text = AppResources.RecognizedHeader;
        ItemLabel.Text = "📝 " + AppResources.ItemLabel;
        TimeLabel.Text = "🕐 " + AppResources.FieldTime;
        RepeatLabel.Text = "🔁 " + AppResources.FieldRepeat;
        EditHintLabel.Text = AppResources.EditHintText;
        ConfirmBtn.Text = AppResources.ConfirmAdd;
        OverlayOkBtn.Text = AppResources.OK;
    }

    // ══════════ 输入防抖解析 ══════════

    private void OnInputTextChanged(object? sender, TextChangedEventArgs e)
    {
        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        _ = DebounceAsync(e.NewTextValue ?? "", _debounceCts.Token);
    }

    private async Task DebounceAsync(string text, CancellationToken ct)
    {
        try
        {
            await Task.Delay(300, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        ParseAndUpdate(text);
    }

    private void ParseAndUpdate(string text)
    {
        text = text.Trim();
        if (text.Length == 0)
        {
            _draftTitle = "";
            _draftDate = null;
            _draftTime = null;
            _draftIsRecurring = false;
            _draftRule = null;
            _draftPlan = null;
            _draftMissing = new();
            _titleEdited = _dateEdited = _timeEdited = _repeatEdited = false; // 输入清空 → 重置保护
            RecognizedSection.IsVisible = false;
            RefreshRows();
            return;
        }

        var cmd = _intent.Parse(text);
        // DEV-07：手工修改过的字段不被重新解析覆盖（其余字段随输入更新）
        if (!_titleEdited)
            _draftTitle = !string.IsNullOrWhiteSpace(cmd.Title) ? cmd.Title! : text;
        if (cmd.TriggerAt is { } t)
        {
            if (!_dateEdited) _draftDate = t.DateTime.Date;
            if (!_timeEdited) _draftTime = t.DateTime.TimeOfDay;
        }
        else
        {
            if (!_dateEdited) _draftDate = null;
            if (!_timeEdited) _draftTime = null;
        }
        if (!_repeatEdited)
        {
            _draftIsRecurring = cmd.IsRecurring;
            _draftRule = cmd.IsRecurring ? cmd.RecurrenceRule : null;
        }

        // DEV-04：统一计划草稿（完整→摘要 chip；缺槽→缺口提示 chip，确认时不猜默认值）
        _draftPlan = cmd.Plan;
        _draftMissing = cmd.MissingSlots != null ? new List<string>(cmd.MissingSlots) : new();

        bool hasAnything = true; // 有输入即展示识别区块（含“准时提醒”占位）
        RecognizedSection.IsVisible = hasAnything;
        BuildChips();
        RefreshRows();
    }

    /// <summary>重建识别 chips：计划摘要 / 日期 / 重复规则；皆无时显示“准时提醒”占位。
    /// 展示以「生效草稿」为准（叠加手工修改保护），避免 chips 与草稿行不一致。</summary>
    private void BuildChips()
    {
        ChipsHost.Children.Clear();

        // DEV-04：计划 chip 用 Plan.Describe() 展示；缺槽时展示缺口提示（不播报猜测值）
        if (_draftPlan is { } plan)
        {
            AddChip(plan is { IsComplete: true } && _draftMissing.Count == 0
                ? plan.Describe()
                : BuildMissingText());
            return; // 计划形态下日期/重复与计划无关，避免混淆
        }

        if (_draftDate is { } d)
        {
            var offset = TimeZoneInfo.Local.GetUtcOffset(d.Date + (_draftTime ?? TimeSpan.Zero));
            AddChip(FmtChipDate(new DateTimeOffset(d.Date + (_draftTime ?? TimeSpan.Zero), offset)));
        }
        if (_draftIsRecurring)
        {
            AddChip(RepeatText());
        }
        else if (_draftDate is null)
        {
            AddChip(AppResources.OnTimeRemind);
        }
    }

    /// <summary>缺口提示：「还缺：组数、每组运动时长」；计次语法先说明不支持。</summary>
    private string BuildMissingText()
    {
        var labels = _draftMissing
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
        if (_draftMissing.Contains(RuleBasedIntentParser.SlotCount))
        {
            var hint = AppResources.PlanCountHint;
            if (labels.Count > 0) hint += " " + string.Format(AppResources.PlanMissingFormat, string.Join("、", labels));
            return hint;
        }
        return string.Format(AppResources.PlanMissingFormat, string.Join("、", labels));
    }

    private void AddChip(string text)
    {
        var chip = new Border { Style = (Style)Application.Current!.Resources["FilterChip"] };
        chip.BackgroundColor = (Color)Application.Current.Resources["SelectedBg"];
        chip.Content = new Label
        {
            Text = text,
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = (Color)Application.Current.Resources["Primary"],
            VerticalOptions = LayoutOptions.Center
        };
        ChipsHost.Children.Add(chip);
    }

    private static string FmtChipDate(DateTimeOffset t)
    {
        string md = $"{t.DateTime.Month}月{t.DateTime.Day}日 {t.DateTime.Hour:00}:{t.DateTime.Minute:00}";
        var today = DateTime.Today;
        if (t.DateTime.Date == today) return $"{AppResources.TodayWord} {md}";
        if (t.DateTime.Date == today.AddDays(1)) return $"{AppResources.TomorrowWord} {md}";
        return md;
    }

    // ══════════ 草稿行 ══════════

    private void RefreshRows()
    {
        ItemValue.Text = _draftTitle;
        if (_draftDate is null)
        {
            TimeValue.Text = AppResources.NoDueLabel;
        }
        else
        {
            string time = _draftTime is { } tm ? $" {tm:hh\\:mm}" : "";
            TimeValue.Text = FmtDate(_draftDate) + time;
        }
        RepeatValue.Text = RepeatText();
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
        var date = d!.Value.Date;
        string md = $"{date.Month}月{date.Day}日";
        var today = DateTime.Today;
        if (date == today) return $"{AppResources.TodayWord}，{md}";
        if (date == today.AddDays(1)) return $"{AppResources.TomorrowWord}，{md}";
        return md;
    }

    // ══════════ 覆盖层（事项 / 日期 / 时间 / 重复） ══════════

    private void ShowOverlay(string kind)
    {
        _overlayKind = kind;
        OverlayTitle.Text = kind switch
        {
            "title" => AppResources.ItemLabel,
            "date" => AppResources.FieldDate,
            "time" => AppResources.FieldTime,
            _ => AppResources.FieldRepeat
        };
        OverlayEditor.IsVisible = kind == "title";
        OverlayDatePicker.IsVisible = kind == "date";
        OverlayTimePicker.IsVisible = kind == "time";
        OverlayOptions.IsVisible = kind == "repeat";

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
        if (kind == "repeat")
        {
            OverlayOptions.Children.Clear();
            var options = new (string Label, string? Value)[]
            {
                (AppResources.RepeatNone, null),
                (AppResources.RepeatDaily, "every day"),
                (AppResources.RepeatWeekly, "every week")
            };
            foreach (var (label, value) in options)
            {
                bool selected = value == (_draftIsRecurring ? _draftRule : null);
                var btn = new Button
                {
                    Text = label,
                    BackgroundColor = selected ? (Color)Application.Current!.Resources["SelectedBg"] : Colors.Transparent,
                    TextColor = selected ? (Color)Application.Current!.Resources["Primary"]
                                         : (Color)Application.Current!.Resources["TextPrimary"],
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
        Overlay.IsVisible = true;
    }

    private void OnOverlayOptionClicked(object? sender, EventArgs e)
    {
        if (sender is not Button b) return;
        _draftRule = b.CommandParameter as string;
        _draftIsRecurring = _draftRule is not null;
        _repeatEdited = true; // 手工改过重复 → 后续解析不再覆盖
        Overlay.IsVisible = false;
        RefreshRows();
    }

    private void OnOverlayDatePicked(object? sender, DateChangedEventArgs e)
    {
        if (_pickingProgrammatic || _overlayKind != "date") return;
        _draftDate = e.NewDate;
        _dateEdited = true; // 手工改过日期 → 后续解析不再覆盖
        RefreshRows();
        if (_draftTime is null)
        {
            ShowOverlay("time"); // 选完日期接着补时间
            return;
        }
        Overlay.IsVisible = false;
    }

    private void OnOverlayTimePicked(object? sender, TimeChangedEventArgs e)
    {
        if (_pickingProgrammatic || _overlayKind != "time") return;
        _draftTime = e.NewTime;
        _timeEdited = true; // 手工改过时间 → 后续解析不再覆盖
        Overlay.IsVisible = false;
        RefreshRows();
    }

    private void OnOverlayClose(object? sender, EventArgs e)
    {
        if (_overlayKind == "title")
        {
            var edited = OverlayEditor.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(edited))
            {
                _draftTitle = edited;
                _titleEdited = true; // 手工改过事项 → 后续解析不再覆盖
            }
            RefreshRows();
        }
        Overlay.IsVisible = false;
    }

    // ══════════ 行点击与主操作 ══════════

    private void OnEditItem(object? sender, TappedEventArgs e) => ShowOverlay("title");
    private void OnEditTime(object? sender, TappedEventArgs e) => ShowOverlay("date");
    private void OnEditRepeat(object? sender, TappedEventArgs e) => ShowOverlay("repeat");

    private async void OnConfirmAdd(object sender, EventArgs e)
    {
        // DEV-04：统一计划确认（文字添加与口述同规则）—— 完整 → 落库 + RequestStart 直通开始；
        // 缺槽 → 提示缺口不猜测，留在页面修改
        if (_draftPlan is { } plan)
        {
            if (plan.IsComplete && _draftMissing.Count == 0)
            {
                var timer = new TimerItem
                {
                    Title = !string.IsNullOrWhiteSpace(plan.ActionName) ? plan.ActionName : _draftTitle,
                    Duration = plan.TotalDuration,
                    TriggerAt = DateTimeOffset.Now,
                    Phases = plan.Expand(),
                    Plan = plan
                };
                try
                {
                    await _repo.AddTimerAsync(timer);
                }
                catch (StorageException ex)
                {
                    await UserAlerts.ShowStorageErrorAsync(ex.Message); // 留在本页，草稿不丢
                    return;
                }
                TimerLauncher.RequestStart(timer, autoStart: true); // 完整计划 → 直接开跑
                if (Shell.Current is not null) await Shell.Current.GoToAsync("//TimerPage");
                return;
            }
            await DisplayAlert(AppResources.PlanFollowUpTitle, BuildMissingText(), AppResources.OK);
            return;
        }

        if (string.IsNullOrWhiteSpace(_draftTitle)) return;
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
            Reminder = new ReminderSettings()
        };
        // A1：仅当待办带提醒时间时，创建前确保通知权限（被拒不阻断保存，也绝不谎报）
        if (due is not null) await NotificationPermission.EnsureAsync();
        // DEV-02：走统一变更入口，落库与提醒排期同一闭环（不再直接调仓库）
        TodoChangeResult result;
        try
        {
            result = await _dispatcher.CreateAsync(item);
        }
        catch (StorageException ex)
        {
            await UserAlerts.ShowStorageErrorAsync(ex.Message); // 保存失败：留在本页保留输入
            return;
        }
        if (result.Saved)
        {
            bool notifGranted = await NotificationPermission.IsGrantedAsync();
            await DisplayAlert(AppResources.ConfirmAdd, SaveFeedbackText(result.ReminderScheduled, result.ReminderMessage, notifGranted), AppResources.OK);
        }
        if (Shell.Current is not null) await Shell.Current.GoToAsync("..");
    }

    /// <summary>DEV-02 / A1：保存反馈区分「提醒已安排」与「未安排（原因）」；权限被拒时绝不谎报「已安排」。</summary>
    private static string SaveFeedbackText(bool reminderScheduled, string? reason, bool notifGranted)
    {
        if (reminderScheduled && notifGranted)
            return AppResources.SavedRemindOn;
        if (reminderScheduled && !notifGranted)
            return AppResources.Perm_RemindOffNoNotif; // 已排期但无权限：如实告知不会响
        return string.Format(AppResources.SavedRemindOffFormat, reason ?? "");
    }

    private async void OnCloseClicked(object sender, EventArgs e)
    {
        if (Shell.Current is not null) await Shell.Current.GoToAsync("..");
    }
}
