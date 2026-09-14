using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Storage;
using VoiceTodo.Core.Abstractions;
using VoiceTodo.Core.Models;
using VoiceTodo.Core.Services;
using VoiceTodo.Maui.Resources;
using VoiceTodo.Maui.Services;
using VoiceTodo.Maui.ViewModels;

namespace VoiceTodo.Maui.Pages;

/// <summary>
/// 待办日历：以「月视图网格 / 周视图」查看待办在日期上的分布。
/// 月视图补全相邻月日期（弱化但可点击，选中即切换月份）；
/// 底部收容未安排日期的待办；视图与选中日期通过 Preferences 持久化。
/// </summary>
public partial class CalendarPage : ContentPage
{
    private const string PrefViewKey = "cal.view";
    private const string PrefSelKey = "cal.sel";

    private readonly ITodoRepository _repo;
    private readonly ITodoChangeDispatcher _dispatcher; // 日历行勾选/删除走统一变更入口
    private readonly ObservableCollection<TodoRowVm> _dayTodos = new();
    private readonly ObservableCollection<TodoRowVm> _unscheduledTodos = new();
    private Dictionary<DateOnly, List<TodoItem>> _byDay = new();
    private List<TodoItem> _unscheduled = new();
    private List<TimerSession> _sessions = new(); // 计时留痕记录（日历图层）
    private DateTime _cursor;      // 月视图=该月首日；周视图=该周任意一天
    private DateTime _selectedDay;
    private bool _weekView;
    private bool _unscheduledExpanded;

    public CalendarPage()
    {
        InitializeComponent();
        var services = Application.Current?.Handler?.MauiContext?.Services
            ?? throw new InvalidOperationException("MauiContext 未就绪");
        _repo = services.GetRequiredService<ITodoRepository>();
        _dispatcher = services.GetRequiredService<ITodoChangeDispatcher>();

        TitleLabel.Text = AppResources.CalendarTitle;
        TodayBtn.Text = AppResources.Today;
        MonthBtn.Text = AppResources.MonthView;
        WeekBtn.Text = AppResources.WeekView;
        DayTodosHeader.Text = AppResources.DayTodos;
        NoTodosLabel.Text = AppResources.NoTodosDay;
        UnschedHeaderLabel.Text = AppResources.CalUnscheduled;
        UnschedEmptyLabel.Text = AppResources.CalUnscheduledEmpty;
        SetWeekdayHeader();
        RestoreState();
        DayTodoList.ItemsSource = _dayTodos;
        UnscheduledList.ItemsSource = _unscheduledTodos;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        List<TodoItem> all;
        try
        {
            all = await _repo.GetTodosAsync();
        }
        catch (StorageException ex)
        {
            // 读取失败不降级为空日历：提示并保留上次数据
            await UserAlerts.ShowStorageErrorAsync(ex.Message);
            return;
        }
        _byDay = all
            .Where(t => t.DueAt is not null)
            .Select(t => (Day: DateOnly.FromDateTime(t.DueAt!.Value.ToLocalTime().Date), Item: t))
            .GroupBy(x => x.Day)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Item.DueAt).Select(x => x.Item).ToList());
        _unscheduled = all.Where(t => t.DueAt is null).ToList();
        Rebuild();
        await LoadSessionsAsync();
    }

    private void RestoreState()
    {
        _weekView = Preferences.Get(PrefViewKey, "month") == "week";
        _selectedDay = DateTime.TryParseExact(
            Preferences.Get(PrefSelKey, string.Empty), "yyyyMMdd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var saved)
            ? saved.Date
            : DateTime.Today;
        _cursor = _weekView ? _selectedDay : new DateTime(_selectedDay.Year, _selectedDay.Month, 1);
        SetToggleButtons();
    }

    private void PersistState()
    {
        Preferences.Set(PrefViewKey, _weekView ? "week" : "month");
        Preferences.Set(PrefSelKey, _selectedDay.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
    }

    private void Rebuild()
    {
        DaysGrid.Clear();
        DaysGrid.RowDefinitions.Clear();

        DateTime firstCell;
        int cellCount;
        if (_weekView)
        {
            firstCell = StartOfWeek(_cursor);
            cellCount = 7;
            CaptionLabel.Text = $"{firstCell:M月d日} - {firstCell.AddDays(6):M月d日}";
        }
        else
        {
            var first = new DateTime(_cursor.Year, _cursor.Month, 1);
            var offset = ((int)first.DayOfWeek + 6) % 7; // 周一为第 0 列
            var days = DateTime.DaysInMonth(_cursor.Year, _cursor.Month);
            var total = offset + days;
            total += (7 - total % 7) % 7;
            firstCell = first.AddDays(-offset);
            cellCount = total;
            CaptionLabel.Text = first.ToString("yyyy年M月", CultureInfo.CurrentCulture);
        }

        var rows = cellCount / 7;
        for (var r = 0; r < rows; r++)
            DaysGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (var i = 0; i < cellCount; i++)
        {
            var date = firstCell.AddDays(i);
            var inMonth = _weekView || (date.Year == _cursor.Year && date.Month == _cursor.Month);
            var cell = BuildDayCell(date, inMonth);
            Grid.SetRow(cell, i / 7);
            Grid.SetColumn(cell, i % 7);
            DaysGrid.Children.Add(cell);
        }

        PersistState();
        RefreshDayTodos();
        RefreshUnscheduled();
        RefreshSessions();
    }

    private Border BuildDayCell(DateTime date, bool inMonth)
    {
        var resources = App.Current!.Resources;
        var textPrimary = (Color)resources["TextPrimary"];
        var textMuted = (Color)resources["TextMuted"];
        var primary = (Color)resources["Primary"];
        var success = (Color)resources["Success"];
        var selectedBg = (Color)resources["SelectedBg"];
        var isToday = date.Date == DateTime.Today;
        var isSelected = date.Date == _selectedDay.Date;
        var count = _byDay.GetValueOrDefault(DateOnly.FromDateTime(date.Date))?.Count ?? 0;

        var stack = new VerticalStackLayout { Spacing = 1, Padding = new Thickness(0, 7, 0, 5) };
        stack.Children.Add(new Label
        {
            Text = date.Day.ToString(),
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = isToday ? primary : inMonth ? textPrimary : textMuted,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
        });
        stack.Children.Add(new Label
        {
            Text = count > 0 ? count.ToString() : " ",
            FontSize = 10,
            FontAttributes = FontAttributes.Bold,
            TextColor = inMonth ? success : textMuted,
            HorizontalTextAlignment = TextAlignment.Center,
            HeightRequest = 12,
        });

        var cell = new Border
        {
            Content = stack,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            BackgroundColor = isSelected ? selectedBg : Colors.Transparent,
            Stroke = isToday ? primary : Colors.Transparent,
            StrokeThickness = isToday ? 1.5 : 0,
            Padding = new Thickness(2),
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => SelectDay(date);
        cell.GestureRecognizers.Add(tap);
        return cell;
    }

    private void SelectDay(DateTime date)
    {
        _selectedDay = date.Date;
        if (!_weekView && (date.Year != _cursor.Year || date.Month != _cursor.Month))
            _cursor = new DateTime(date.Year, date.Month, 1); // 点击跨月补全日期 → 切到该月
        Rebuild();
    }

    /// <summary>A3：点击日历里的待办行（当天/未安排区）进入既有编辑页，复用 TaskEditorPage 的 id 路由。</summary>
    private async void OnTodoRowTapped(object? sender, EventArgs e)
    {
        if (sender is not View v || v.BindingContext is not TodoRowVm row) return;
        if (Shell.Current is null) return;
        await Shell.Current.GoToAsync($"TaskEditorPage?id={row.Id}");
    }

    private void RefreshDayTodos()
    {
        _dayTodos.Clear();
        var items = _byDay.GetValueOrDefault(DateOnly.FromDateTime(_selectedDay.Date)) ?? new List<TodoItem>();
        foreach (var t in items)
            _dayTodos.Add(new TodoRowVm(t, _dispatcher, _dayTodos));
        var mark = _selectedDay.Date == DateTime.Today ? "（今天）" : "";
        DayTodosHeader.Text = $"{AppResources.DayTodos} · {_selectedDay:M月d日}{mark}";
    }

    private void RefreshUnscheduled()
    {
        _unscheduledTodos.Clear();
        foreach (var t in _unscheduled)
            _unscheduledTodos.Add(new TodoRowVm(t, _dispatcher, _unscheduledTodos));
        UnschedCountLabel.Text = _unscheduled.Count.ToString();
        UnscheduledList.IsVisible = _unscheduledExpanded;
    }

    private void OnToggleUnscheduled(object sender, EventArgs e)
    {
        _unscheduledExpanded = !_unscheduledExpanded;
        UnscheduledList.IsVisible = _unscheduledExpanded;
    }

    private void OnPrev(object sender, EventArgs e)
    {
        _selectedDay = _weekView ? _selectedDay.AddDays(-7) : _selectedDay.AddMonths(-1);
        _cursor = _weekView ? _selectedDay : new DateTime(_selectedDay.Year, _selectedDay.Month, 1);
        Rebuild();
    }

    private void OnNext(object sender, EventArgs e)
    {
        _selectedDay = _weekView ? _selectedDay.AddDays(7) : _selectedDay.AddMonths(1);
        _cursor = _weekView ? _selectedDay : new DateTime(_selectedDay.Year, _selectedDay.Month, 1);
        Rebuild();
    }

    private void OnToday(object sender, EventArgs e)
    {
        _selectedDay = DateTime.Today;
        _cursor = _weekView ? DateTime.Today : new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        Rebuild();
    }

    private void OnMonthView(object sender, EventArgs e)
    {
        _weekView = false;
        _cursor = new DateTime(_selectedDay.Year, _selectedDay.Month, 1);
        SetToggleButtons();
        Rebuild();
    }

    private void OnWeekView(object sender, EventArgs e)
    {
        _weekView = true;
        _cursor = _selectedDay;
        SetToggleButtons();
        Rebuild();
    }

    private void SetToggleButtons()
    {
        HighlightToggle(MonthWrap, MonthBtn, !_weekView);
        HighlightToggle(WeekWrap, WeekBtn, _weekView);
    }

    private static void HighlightToggle(Border wrap, Button btn, bool active)
    {
        var resources = App.Current!.Resources;
        wrap.BackgroundColor = active ? (Color)resources["SelectedBg"] : Colors.Transparent;
        btn.TextColor = active ? (Color)resources["Primary"] : (Color)resources["TextSecondary"];
    }

    private void SetWeekdayHeader()
    {
        var names = new[] { "一", "二", "三", "四", "五", "六", "日" };
        var cols = new[] { W0, W1, W2, W3, W4, W5, W6 };
        for (var i = 0; i < names.Length; i++)
        {
            cols[i].Text = names[i];
            if (i >= 5) cols[i].TextColor = (Color)App.Current!.Resources["Danger"];
        }
    }

    private static DateTime StartOfWeek(DateTime date)
    {
        var d = date.Date;
        return d.AddDays(-((int)d.DayOfWeek + 6) % 7); // 周一
    }

    // ══════════ 计时记录图层（TimerSession 留痕，D2/L2 主链路） ══════════

    private async Task LoadSessionsAsync()
    {
        try
        {
            _sessions = await _repo.GetSessionsAsync();
        }
        catch
        {
            _sessions = new List<TimerSession>(); // 记录加载失败不阻塞日历
        }
        RefreshSessions();
    }

    /// <summary>按选中日刷新计时记录区（有记录才显示）：HH:mm–HH:mm · 标题 · Outcome 徽标，点行可改名。</summary>
    private void RefreshSessions()
    {
        var day = DateOnly.FromDateTime(_selectedDay.Date);
        var items = _sessions
            .Where(s => DateOnly.FromDateTime(s.StartedAt.LocalDateTime.Date) == day)
            .OrderBy(s => s.StartedAt)
            .ToList();
        SessionsHeader.IsVisible = SessionsHost.IsVisible = items.Count > 0;
        SessionsHost.Clear();
        if (items.Count == 0) return;
        SessionsHeader.Text = $"{AppResources.TimerSessions} · {_selectedDay:M月d日}";
        foreach (var s in items)
            SessionsHost.Children.Add(BuildSessionRow(s));
    }

    private Border BuildSessionRow(TimerSession s)
    {
        var resources = App.Current!.Resources;
        bool completed = s.Outcome == "completed";
        string start = s.StartedAt.LocalDateTime.ToString("HH:mm");
        string range = s.EndedAt is { } e ? $"{start}–{e.LocalDateTime:HH:mm}" : start;

        var badge = new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            BackgroundColor = completed ? (Color)resources["Success"] : (Color)resources["TextMuted"],
            Padding = new Thickness(8, 2),
            VerticalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = completed ? AppResources.SessionCompleted : AppResources.SessionCancelled,
                FontSize = 11,
                FontAttributes = FontAttributes.Bold,
                TextColor = Colors.White,
                VerticalOptions = LayoutOptions.Center
            }
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 10
        };
        grid.Children.Add(new Label
        {
            Text = range,
            Style = (Style)resources["RowSub"],
            VerticalOptions = LayoutOptions.Center
        });
        var title = new Label
        {
            Text = s.Title,
            Style = (Style)resources["RowTitle"],
            LineBreakMode = LineBreakMode.TailTruncation,
            VerticalOptions = LayoutOptions.Center
        };
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);
        Grid.SetColumn(badge, 2);
        grid.Children.Add(badge);

        var row = new Border
        {
            Style = (Style)resources["Card"],
            Padding = new Thickness(12, 10),
            Content = grid
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await RenameSessionAsync(s);
        row.GestureRecognizers.Add(tap);
        return row;
    }

    /// <summary>点记录行改名（D2：记录可后改）。</summary>
    private async Task RenameSessionAsync(TimerSession s)
    {
        string? name = await DisplayPromptAsync(AppResources.RenameSessionTitle, AppResources.SessionNamePrompt,
            AppResources.OK, AppResources.Cancel, initialValue: s.Title, maxLength: 60);
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == s.Title) return;
        s.Title = name.Trim();
        try
        {
            await _repo.UpdateSessionAsync(s);
        }
        catch
        {
            // 改名持久化失败不阻塞 UI
        }
        RefreshSessions();
    }
}
