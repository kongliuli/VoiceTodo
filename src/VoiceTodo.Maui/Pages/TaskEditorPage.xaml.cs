using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using VoiceTodo.Core.Abstractions;
using VoiceTodo.Core.Models;
using VoiceTodo.Core.Services;
using VoiceTodo.Maui.Resources;
using VoiceTodo.Maui.Services;

namespace VoiceTodo.Maui.Pages;

/// <summary>
/// 编辑任务页：GoToAsync("TaskEditorPage?id=N") 进入。
/// 内容/日期/时间/提醒/重复/备注可编辑；右上 ⋮ 支持删除任务。
/// 日期、时间、提醒、重复四行均以页内覆盖层（ShowOverlay/CloseOverlay）交互。
/// </summary>
[QueryProperty(nameof(TodoId), "id")]
[QueryProperty(nameof(QDate), "date")]
public partial class TaskEditorPage : ContentPage
{
    private readonly ITodoRepository _repo;
    private readonly ITodoChangeDispatcher _dispatcher; // DEV-02：保存走统一变更入口
    private TodoItem? _item;
    private DateTime? _date;
    private TimeSpan? _time;
    private bool _createNew; // C6：带 date 参数进入的新建模式（区别于无参错误回退）

    public string? TodoId { get; set; }

    /// <summary>C6：日期查询参数（yyyy-MM-dd）；有值即进入新建模式并预填该日。</summary>
    public string? QDate { get; set; }

    public TaskEditorPage()
    {
        InitializeComponent();
        var services = Application.Current?.Handler?.MauiContext?.Services
            ?? throw new InvalidOperationException("MauiContext 未就绪");
        _repo = services.GetRequiredService<ITodoRepository>();
        _dispatcher = services.GetRequiredService<ITodoChangeDispatcher>();

        TopTitleLabel.Text = AppResources.EditorTitle;
        ContentHeader.Text = AppResources.FieldContent;
        DateNameLabel.Text = AppResources.FieldDate;
        TimeNameLabel.Text = AppResources.FieldTime;
        ReminderNameLabel.Text = AppResources.FieldReminder;
        RepeatNameLabel.Text = AppResources.FieldRepeat;
        NotesHeader.Text = AppResources.FieldNotes;
        NotesEditor.Placeholder = AppResources.NotesPlaceholder;
        SaveBtn.Text = AppResources.SaveChanges;
        CancelBtn.Text = AppResources.Cancel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_item is not null) return;

        if (!int.TryParse(TodoId, out var id))
        {
            // C6：无 id 时，若带 date 参数则进入新建模式（预填日期、时间留空）；
            // 既无 id 也无 date 维持原有「找不到该任务」错误回退，行为不变。
            if (DateTime.TryParse(QDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                _createNew = true;
                _item = new TodoItem();
                _date = date;
                _time = null; // 时间不凭空造：留空（UI 显示 --:--）
                TitleEditor.Text = "";
                NotesEditor.Text = "";
                RefreshValues();
                return;
            }
            await AlertAndBack();
            return;
        }
        List<TodoItem> todos;
        try
        {
            todos = await _repo.GetTodosAsync();
        }
        catch (StorageException ex)
        {
            // 读取失败不当作“任务不存在”：保留页面并提示重试
            await UserAlerts.ShowStorageErrorAsync(ex.Message);
            return;
        }
        _item = todos.FirstOrDefault(t => t.Id == id);
        if (_item is null)
        {
            await AlertAndBack();
            return;
        }

        TitleEditor.Text = _item.Title;
        NotesEditor.Text = _item.Notes;
        _date = _item.DueAt?.LocalDateTime.Date;
        _time = _item.DueAt?.LocalDateTime.TimeOfDay;
        RefreshValues();
    }

    private async Task AlertAndBack()
    {
        await DisplayAlert(AppResources.EditorTitle, "找不到该任务", "好");
        await Shell.Current.GoToAsync("..");
    }

    // ══════ 数值展示 ══════

    private void RefreshValues()
    {
        if (_item is null) return;
        DateValueLabel.Text = FormatDate(_date);
        TimeValueLabel.Text = _time is null ? "--:--" : _time.Value.ToString(@"hh\:mm");

        var pre = _item.Reminder.PreAlert;
        ReminderValueLabel.Text = pre switch
        {
            null => AppResources.OnTimeRemind,
            var p when p == TimeSpan.FromMinutes(10) => AppResources.Early10,
            var p when p == TimeSpan.FromMinutes(30) => AppResources.Early30,
            var p => $"提前 {(int)p.Value.TotalMinutes} 分钟",
        };

        RepeatValueLabel.Text = !_item.IsRecurring ? AppResources.RepeatNone
            : _item.RecurrenceRule == "daily" ? AppResources.RepeatDaily
            : _item.RecurrenceRule == "weekly" ? AppResources.RepeatWeekly
            : _item.RecurrenceRule;
    }

    private static string FormatDate(DateTime? date)
    {
        if (date is null) return AppResources.NoDueLabel;
        var d = date.Value;
        var md = $"{d.Month}月{d.Day}日";
        if (d == DateTime.Today) return $"{AppResources.TodayWord}，{md}";
        if (d == DateTime.Today.AddDays(1)) return $"{AppResources.TomorrowWord}，{md}";
        return md;
    }

    // ══════ 顶栏 ══════

    private async void OnBackClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("..");

    private async void OnMoreClicked(object? sender, EventArgs e)
    {
        if (_item is null || _createNew) return; // 未保存的新建项无删除入口
        var action = await DisplayActionSheet(null, AppResources.Cancel, AppResources.DeleteTask);
        if (action != AppResources.DeleteTask) return;
        var confirm = await DisplayAlert(AppResources.DeleteTask, "确定要删除该任务吗？删除后不可恢复。",
            AppResources.DeleteTask, AppResources.Cancel);
        if (!confirm) return;
        try
        {
            await _dispatcher.DeleteAsync(_item.Id); // DEV-02：删除同时取消全部关联通知
        }
        catch (StorageException ex)
        {
            await UserAlerts.ShowStorageErrorAsync(ex.Message);
            return;
        }
        await Shell.Current.GoToAsync("..");
    }

    // ══════ 覆盖层通用逻辑 ══════

    /// <summary>以居中白卡承载内容弹出页内覆盖层。</summary>
    private void ShowOverlay(View content)
    {
        OverlayCard.Content = content;
        OverlayRoot.IsVisible = true;
    }

    private void CloseOverlay()
    {
        OverlayRoot.IsVisible = false;
        OverlayCard.Content = null;
    }

    /// <summary>Radio 式选项白卡：选中项加 ✓，点击即写回并关闭。</summary>
    private void ShowChoiceOverlay(string[] options, int selected, Action<int> onPick)
    {
        var res = Application.Current!.Resources;
        var list = new VerticalStackLayout();
        for (var i = 0; i < options.Length; i++)
        {
            var idx = i;
            var btn = new Button
            {
                Style = (Style)res["GhostButton"],
                Text = (idx == selected ? "✓ " : "   ") + options[idx],
                TextColor = idx == selected ? (Color)res["Primary"] : (Color)res["TextPrimary"],
                HorizontalOptions = LayoutOptions.Fill,
            };
            btn.Clicked += (_, _) =>
            {
                onPick(idx);
                RefreshValues();
                CloseOverlay();
            };
            list.Children.Add(btn);
        }
        ShowOverlay(list);
    }

    // ══════ 字段行 ══════

    private void OnDateRowTapped(object? sender, EventArgs e)
    {
        var picker = new DatePicker
        {
            Date = _date ?? DateTime.Today,
            TextColor = (Color)Application.Current!.Resources["TextPrimary"],
        };
        ShowOverlay(BuildPickerOverlay(picker, () =>
        {
            _date = picker.Date;
            RefreshValues();
        }));
    }

    private void OnTimeRowTapped(object? sender, EventArgs e)
    {
        var picker = new TimePicker
        {
            Time = _time ?? new TimeSpan(9, 0, 0),
            TextColor = (Color)Application.Current!.Resources["TextPrimary"],
        };
        ShowOverlay(BuildPickerOverlay(picker, () =>
        {
            _time = picker.Time;
            RefreshValues();
        }));
    }

    /// <summary>日期/时间选择覆盖层：picker + 完成按钮，确认后写回并关闭。</summary>
    private View BuildPickerOverlay(View picker, Action onDone)
    {
        var done = new Button
        {
            Style = (Style)Application.Current!.Resources["GhostButton"],
            Text = "完成",
            TextColor = (Color)Application.Current.Resources["Primary"],
            HorizontalOptions = LayoutOptions.Fill,
        };
        done.Clicked += (_, _) =>
        {
            onDone();
            CloseOverlay();
        };
        var content = new VerticalStackLayout { Spacing = 12 };
        content.Children.Add(picker);
        content.Children.Add(done);
        return content;
    }

    private void OnReminderRowTapped(object? sender, EventArgs e)
    {
        if (_item is null) return;
        var current = _item.Reminder.PreAlert switch
        {
            null => 0,
            var p when p == TimeSpan.FromMinutes(10) => 1,
            var p when p == TimeSpan.FromMinutes(30) => 2,
            _ => 0,
        };
        ShowChoiceOverlay(new[] { AppResources.OnTimeRemind, AppResources.Early10, AppResources.Early30 },
            current,
            idx => _item!.Reminder.PreAlert = idx switch
            {
                1 => TimeSpan.FromMinutes(10),
                2 => TimeSpan.FromMinutes(30),
                _ => null,
            });
    }

    private void OnRepeatRowTapped(object? sender, EventArgs e)
    {
        if (_item is null) return;
        var current = !_item.IsRecurring ? 0 : _item.RecurrenceRule == "weekly" ? 2 : 1;
        ShowChoiceOverlay(new[] { AppResources.RepeatNone, AppResources.RepeatDaily, AppResources.RepeatWeekly },
            current,
            idx =>
            {
                _item!.IsRecurring = idx > 0;
                _item.RecurrenceRule = idx == 1 ? "daily" : idx == 2 ? "weekly" : null;
            });
    }

    // ══════ 底部按钮 ══════

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (_item is null) return;
        var title = TitleEditor.Text?.Trim();
        if (string.IsNullOrEmpty(title))
        {
            await DisplayAlert(AppResources.EditorTitle, "请输入任务内容", "好");
            return;
        }

        _item.Title = title;
        if (_date is null)
        {
            _item.DueAt = null;
        }
        else
        {
            var local = _date.Value + (_time ?? new TimeSpan(9, 0, 0)); // 无时间 → 默认 09:00
            _item.DueAt = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
        }
        _item.Notes = string.IsNullOrWhiteSpace(NotesEditor.Text) ? null : NotesEditor.Text;
        // 提醒/重复已在选项浮层中写回 _item（保留 NagMode/Snooze/CustomSoundPath 等其它字段）

        // A1：仅当待办带提醒时间时，编辑保存前确保通知权限（被拒不阻断保存，也绝不谎报）
        if (_item.DueAt is not null) await NotificationPermission.EnsureAsync();
        // DEV-02：统一变更入口。新建（带 date 进入）→ CreateAsync；编辑现有 → RescheduleAsync。
        TodoChangeResult result;
        try
        {
            result = _createNew
                ? await _dispatcher.CreateAsync(_item)
                : await _dispatcher.RescheduleAsync(_item);
        }
        catch (StorageException ex)
        {
            // 保存失败：留在页面保留输入，提示重试（不跳转、不丢输入）
            await UserAlerts.ShowStorageErrorAsync(ex.Message);
            return;
        }
        if (result.Saved)
        {
            bool notifGranted = await NotificationPermission.IsGrantedAsync();
            string feedback = result.ReminderScheduled && notifGranted
                ? AppResources.SavedRemindOn
                : result.ReminderScheduled && !notifGranted
                    ? AppResources.Perm_RemindOffNoNotif // 已排期但无权限：如实告知不会响
                    : string.Format(AppResources.SavedRemindOffFormat, result.ReminderMessage ?? "");
            await DisplayAlert(AppResources.SaveChanges, feedback, AppResources.OK);
        }
        await Shell.Current.GoToAsync("..");
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("..");
}
