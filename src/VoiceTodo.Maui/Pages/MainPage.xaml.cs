using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Dispatching;
using VoiceTodo.Core.Abstractions;
using VoiceTodo.Maui.Resources;
using VoiceTodo.Maui.Services;
using VoiceTodo.Maui.ViewModels;

namespace VoiceTodo.Maui.Pages;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _vm;
    private readonly ITodoRepository _repo;
    private IDispatcherTimer? _tickTimer;
    private bool _completedExpanded;
    private readonly HashSet<int> _reminderShown = new();

    public MainPage()
    {
        InitializeComponent();
        var services = Application.Current?.Handler?.MauiContext?.Services
            ?? throw new InvalidOperationException("MauiContext 未就绪");
        _vm = services.GetRequiredService<MainViewModel>();
        _repo = services.GetRequiredService<ITodoRepository>();
        BindingContext = _vm;

        DateLabel.Text = DateTime.Now.ToString("M月d日 · dddd");
        TitleLabel.Text = AppResources.TodayTitle;
        ScheduleHeader.Text = AppResources.DayTodos;
        ViewAllButton.Text = AppResources.ViewAll + " ›";
        CalendarToolbarItem.Text = AppResources.OpenCalendar;
        RunningNowLabel.Text = AppResources.RunningNow;
        UnscheduledTitleLabel.Text = AppResources.FilterUnscheduled;
        UnscheduledHintLabel.Text = AppResources.UnscheduledHint;
        EmptyTodayLabel.Text = AppResources.EmptyToday;
        SayButton.Text = AppResources.SayToRecord;
        TypeAddButton.Text = AppResources.TypeToAdd;

        // 删除/勾选导致列表增减时，重建一次以保证分区与计数正确（VM 内有重入保护）
        _vm.Todos.CollectionChanged += (_, _) => _ = _vm.RefreshAsync();
        _vm.DoneTodos.CollectionChanged += (_, _) => _ = _vm.RefreshAsync();

        _vm.PropertyChanged += OnVmPropertyChanged;
        RunningTimerHub.Changed += OnRunningTimerChanged;

        UpdateCounts();
        UpdateRunningCard();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
#if DEBUG
        await MockDataSeeder.SeedIfEmptyAsync(_repo); // 样例库隔离初始化：仅主库文件不存在时注入 30 条样例
#endif
        StartTicker();
        await _vm.RefreshAsync();
        UpdateRunningCard();
        await CheckDueReminderAsync();
    }

    /// <summary>应用内到期提醒：发现刚到期（24h 内）未完成待办 → 推送 ReminderPage（每项每会话一次）。</summary>
    private async Task CheckDueReminderAsync()
    {
        try
        {
            var todos = await _repo.GetTodosAsync();
            var now = DateTimeOffset.Now;
            var due = todos.FirstOrDefault(t => !t.IsDone
                && t.DueAt is { } d && d <= now && d >= now.AddDays(-1)
                && !_reminderShown.Contains(t.Id));
            if (due is null) return;
            _reminderShown.Add(due.Id);
            await Shell.Current.GoToAsync($"ReminderPage?id={due.Id}");
        }
        catch
        {
            // 提醒检查失败不阻塞主页
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _tickTimer?.Stop();
    }

    /// <summary>500ms 心跳：刷新剩余时间 / 进度 / 暂停态。</summary>
    private void StartTicker()
    {
        if (_tickTimer is null)
        {
            _tickTimer = Dispatcher.CreateTimer();
            _tickTimer.Interval = TimeSpan.FromMilliseconds(500);
            _tickTimer.Tick += (_, _) => UpdateRunningValues();
        }
        _tickTimer.Start();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.IsListening):
                SayButton.IsEnabled = !_vm.IsListening;
                SayButton.Text = _vm.IsListening ? _vm.Status : AppResources.SayToRecord;
                break;
            case nameof(MainViewModel.Status) when _vm.IsListening:
                SayButton.Text = _vm.Status;
                break;
            case nameof(MainViewModel.TotalPending):
            case nameof(MainViewModel.RunningCount):
            case nameof(MainViewModel.UnscheduledCount):
            case nameof(MainViewModel.DoneCount):
                UpdateCounts();
                break;
        }
    }

    private void UpdateCounts()
    {
        SummaryLabel.Text = string.Format(AppResources.SummaryFormat, _vm.TotalPending, _vm.RunningCount);
        CompletedSection.IsVisible = _vm.DoneCount > 0;
        CompletedTitleLabel.Text = string.Format(AppResources.CompletedCount, _vm.DoneCount);
        UnscheduledBadgeLabel.Text = _vm.UnscheduledCount.ToString();
        UnscheduledBadge.IsVisible = _vm.UnscheduledCount > 0;
    }

    private void OnRunningTimerChanged()
        => Dispatcher.Dispatch(() =>
        {
            UpdateRunningCard();
            _vm.RefreshRunningCount();
        });

    private void UpdateRunningCard()
    {
        var info = RunningTimerHub.Current;
        RunningCard.IsVisible = info is not null;
        if (info is null) return;
        RunningTitleLabel.Text = info.Title;
        UpdateRunningValues();
    }

    private void UpdateRunningValues()
    {
        if (RunningTimerHub.Current is not { } info) return;

        var remaining = info.Remaining;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        var clock = $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}";
        RunningRemainingLabel.Text = info.Paused ? $"⏸ {clock}" : clock;
        RunningProgress.Progress = info.ElapsedFraction;
        RunningPercentLabel.Text = $"{(int)Math.Round(info.ElapsedFraction * 100)}%";
        RunningTotalLabel.Text = string.Format(AppResources.TotalLabel, FormatDuration(info.Total));
    }

    private static string FormatDuration(TimeSpan d)
    {
        if (d.TotalHours >= 1)
            return d.Minutes > 0 ? $"{(int)d.TotalHours}小时{d.Minutes}分" : $"{(int)d.TotalHours}小时";
        if (d.TotalMinutes >= 1)
            return $"{(int)d.TotalMinutes}分钟";
        return $"{(int)d.TotalSeconds:0}秒";
    }

    private async void OnCalendarClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("CalendarPage");

    private async void OnSearchClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("ListsPage");

    private async void OnSettingsClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("SettingsPage");

    private async void OnViewAllClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("ListsPage?filter=today");

    private async void OnUnscheduledTapped(object? sender, TappedEventArgs e)
        => await Shell.Current.GoToAsync("ListsPage?filter=unscheduled");

    private async void OnRunningCardTapped(object? sender, TappedEventArgs e)
        => await Shell.Current.GoToAsync("//TimerPage");

    private void OnCompletedToggled(object? sender, TappedEventArgs e)
    {
        _completedExpanded = !_completedExpanded;
        DoneList.IsVisible = _completedExpanded;
        CompletedChevron.Text = _completedExpanded ? "⌄" : "›";
    }

    private async void OnTodoTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Grid { BindingContext: TodoRowVm row })
            await Shell.Current.GoToAsync($"TaskEditorPage?id={row.Id}");
    }

    private async void OnSayClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("VoiceCapturePage");

    private async void OnTypeAddClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("TextAddPage");
}
