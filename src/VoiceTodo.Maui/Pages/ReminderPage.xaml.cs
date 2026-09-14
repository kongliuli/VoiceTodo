using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using VoiceTodo.Core.Abstractions;
using VoiceTodo.Core.Models;
using VoiceTodo.Maui.Resources;

namespace VoiceTodo.Maui.Pages;

/// <summary>
/// 到期提醒页：展示到期待办的提醒信息，支持标记完成与延后（快捷分钟 / 自定义时刻）。
/// 进入方式：GoToAsync("ReminderPage?id=N")。
/// </summary>
[QueryProperty(nameof(TodoId), "id")]
public partial class ReminderPage : ContentPage
{
    private readonly ITodoRepository _repo;
    private readonly ITodoChangeDispatcher _dispatcher; // DEV-02：延后走统一变更入口
    private TodoItem? _item;
    private int _selectedMinutes = 5; // 0 = 使用自定义时刻
    private TimeSpan _customTime;

    public string? TodoId { get; set; }

    public ReminderPage()
    {
        InitializeComponent();
        var services = Application.Current?.Handler?.MauiContext?.Services
            ?? throw new InvalidOperationException("MauiContext 未就绪");
        _repo = services.GetRequiredService<ITodoRepository>();
        _dispatcher = services.GetRequiredService<ITodoChangeDispatcher>();

        HeaderLabel.Text = AppResources.DueRemindTitle;
        ArrivedLabel.Text = AppResources.TimeArrived;
        SnoozeLabel.Text = AppResources.SnoozeHeader;
        CustomLink.Text = AppResources.CustomTimeLink;
        OverlayTitle.Text = AppResources.SnoozeHeader;
        CustomOkBtn.Text = AppResources.OK;
        Chip5Btn.Text = string.Format(AppResources.QuickMinFormat, 5);
        Chip10Btn.Text = string.Format(AppResources.QuickMinFormat, 10);
        Chip30Btn.Text = string.Format(AppResources.QuickMinFormat, 30);
        DoneBtn.Text = AppResources.MarkDoneBtn;
        SnoozeBtn.Text = AppResources.SnoozeApplyBtn;
        StopNagBtn.Text = AppResources.NagStop_Button; // A4：停止该任务的催促
        RefreshChips();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        var todos = await _repo.GetTodosAsync();
        int.TryParse(TodoId, out int id);
        _item = todos.FirstOrDefault(t => t.Id == id);
        if (_item is null)
        {
            bool zh = CultureInfo.CurrentCulture.TwoLetterISOLanguageName == "zh";
            await DisplayAlert(AppResources.DueRemindTitle,
                zh ? "任务不存在或已被删除" : "Task not found or deleted", AppResources.OK);
            await Shell.Current.GoToAsync("..");
            return;
        }
        BindItem();
    }

    private void BindItem()
    {
        TaskTitleLabel.Text = _item!.Title;
        if (_item.DueAt is DateTimeOffset due)
        {
            DueLabel.Text = FormatDue(due);
            DueLabel.IsVisible = true;
        }
        else
        {
            DueLabel.IsVisible = false;
        }
    }

    private static string FormatDue(DateTimeOffset due)
    {
        var local = due.ToLocalTime();
        var now = DateTime.Now;
        string hh = local.ToString("HH:mm");
        if (local.Date == now.Date) return $"{AppResources.TodayWord} {hh}";
        if (local.Date == now.Date.AddDays(1)) return $"{AppResources.TomorrowWord} {hh}";
        return $"{local.Month}月{local.Day}日 {hh}";
    }

    // ══════════ 交互 ══════════

    private async void OnClose(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("..");

    private void OnChip(object? sender, EventArgs e)
    {
        _selectedMinutes = sender == Chip10Btn ? 10 : sender == Chip30Btn ? 30 : 5;
        RefreshChips();
    }

    /// <summary>选中 chip 底 SelectedBg 文字 Primary，未选白底描边文字 TextPrimary。</summary>
    private void RefreshChips()
    {
        SetChip(Chip5, Chip5Btn, _selectedMinutes == 5);
        SetChip(Chip10, Chip10Btn, _selectedMinutes == 10);
        SetChip(Chip30, Chip30Btn, _selectedMinutes == 30);
    }

    private static void SetChip(Border chip, Button btn, bool active)
    {
        var res = Application.Current!.Resources;
        chip.BackgroundColor = active ? (Color)res["SelectedBg"] : (Color)res["Surface"];
        chip.Stroke = active ? (Color)res["Primary"] : (Color)res["BorderColor"];
        chip.StrokeThickness = active ? 2 : 1;
        btn.TextColor = active ? (Color)res["Primary"] : (Color)res["TextPrimary"];
    }

    private void OnCustomTime(object? sender, TappedEventArgs e)
    {
        CustomTimePicker.Time = DateTime.Now.AddMinutes(30).TimeOfDay;
        CustomTimeOverlay.IsVisible = true;
    }

    private void OnCustomConfirm(object sender, EventArgs e)
    {
        _customTime = (TimeSpan)CustomTimePicker.Time;
        _selectedMinutes = 0; // 标记使用自定义时刻
        RefreshChips();
        CustomTimeOverlay.IsVisible = false;
    }

    private async void OnMarkDone(object sender, EventArgs e)
    {
        if (_item is null) return;
        await _dispatcher.CompleteAsync(_item.Id); // DEV-02：完成同时取消全部关联通知
        await Shell.Current.GoToAsync("..");
    }

    private async void OnSnooze(object sender, EventArgs e)
    {
        if (_item is null) return;
        var newDue = _selectedMinutes > 0
            ? DateTimeOffset.Now.AddMinutes(_selectedMinutes)
            : NextOccurrence(_customTime);
        // DEV-02：延后 = 取消旧提醒 + 按新时刻重排一次（不再整体后移首触发）
        var result = await _dispatcher.PostponeAsync(_item, newDue);
        if (result.Saved && !result.ReminderScheduled)
        {
            await DisplayAlert(AppResources.SnoozeHeader,
                string.Format(AppResources.SavedRemindOffFormat, result.ReminderMessage ?? ""), AppResources.OK);
        }
        await Shell.Current.GoToAsync("..");
    }

    /// <summary>A4：停止这条任务的催促——取消其全部关联通知并关闭 NagMode，用户得到明确反馈后返回。</summary>
    private async void OnStopNag(object sender, EventArgs e)
    {
        if (_item is null) return;
        await _dispatcher.StopNagAsync(_item.Id); // 取消通知 + 持久化 NagMode=false
        // 明确提示已停止，让用户感知状态变化（Windows 端无系统通知时也如实说明已停，不谎报）
        await DisplayAlert(AppResources.DueRemindTitle, AppResources.NagStop_Done, AppResources.OK);
        await Shell.Current.GoToAsync("..");
    }

    /// <summary>自定义时刻 → 今天该时刻，已过则顺延到下次该时刻。</summary>
    private static DateTimeOffset NextOccurrence(TimeSpan time)
    {
        var now = DateTime.Now;
        var candidate = now.Date + time;
        if (candidate <= now) candidate = candidate.AddDays(1);
        return new DateTimeOffset(candidate);
    }
}
