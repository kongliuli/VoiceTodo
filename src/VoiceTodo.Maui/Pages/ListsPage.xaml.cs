using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using VoiceTodo.Core.Abstractions;
using VoiceTodo.Core.Models;
using VoiceTodo.Core.Services;
using VoiceTodo.Maui.Resources;
using VoiceTodo.Maui.Services;
using VoiceTodo.Maui.ViewModels;

namespace VoiceTodo.Maui.Pages;

/// <summary>
/// 清单页：待办按 待安排 / 今天 / 未来 / 已完成 分组查看，支持标题即时搜索。
/// 首页可带查询串进入：GoToAsync("ListsPage?filter=today|unscheduled|upcoming|completed")。
/// </summary>
[QueryProperty(nameof(InitialFilter), "filter")]
public partial class ListsPage : ContentPage
{
    private enum ListFilter { Unscheduled, Today, Upcoming, Completed }

    private readonly ITodoRepository _repo;
    private readonly ITodoChangeDispatcher _dispatcher; // 行内勾选/删除走统一变更入口（同步取消/重排提醒）
    private readonly ObservableCollection<TodoRowVm> _rows = new();
    private readonly DueLabelConverter _dueConverter;
    private List<TodoItem> _allTodos = new();
    private ListFilter _filter = ListFilter.Unscheduled;
    private string _search = string.Empty;

    // C1：删除撤销条状态（仅保留最近一次可撤销）
    private TodoItem? _undoSnapshot;
    private CancellationTokenSource? _undoCts;
    private int _undoGeneration;

    public string? InitialFilter { get; set; }

    public ListsPage()
    {
        InitializeComponent();
        var services = Application.Current?.Handler?.MauiContext?.Services
            ?? throw new InvalidOperationException("MauiContext 未就绪");
        _repo = services.GetRequiredService<ITodoRepository>();
        _dispatcher = services.GetRequiredService<ITodoChangeDispatcher>();
        _dueConverter = (DueLabelConverter)Resources["DueLabel"];

        Title = AppResources.ListsTitle;
        TitleLabel.Text = AppResources.ListsTitle;
        SearchEntry.Placeholder = AppResources.SearchTask;
        ChipUnscheduledBtn.Text = AppResources.FilterUnscheduled;
        ChipTodayBtn.Text = AppResources.FilterToday;
        ChipUpcomingBtn.Text = AppResources.FilterUpcoming;
        ChipCompletedBtn.Text = AppResources.FilterCompleted;
        HintLabel.Text = AppResources.UnscheduledHint;
        EmptyLabel.Text = AppResources.EmptyUnscheduled;

        // C5：清单页新建入口
        AddBtn.SetValue(SemanticProperties.DescriptionProperty, AppResources.AddTodo);
        // C1：撤销条按钮文案
        UndoBtn.Text = AppResources.Undo_Action;

        TodoList.ItemsSource = _rows;
        RefreshChips();
        HintLabel.IsVisible = true;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (InitialFilter is not null)
        {
            _filter = ParseFilter(InitialFilter);
            InitialFilter = null;
        }
        if (!await LoadTodosAsync()) return; // 读取失败：保留当前列表并给重试入口，绝不当空库
        ApplyFilter(); // 保持选中筛选与搜索词
    }

    /// <summary>加载待办：损坏/读失败不降级为空库；提示重试，用户取消则保留上一份列表。</summary>
    private async Task<bool> LoadTodosAsync()
    {
        try
        {
            _allTodos = await _repo.GetTodosAsync();
            return true;
        }
        catch (StorageException ex)
        {
            var retry = await UserAlerts.ConfirmRetryAsync(
                AppResources.StorageErrorTitle,
                string.Format(AppResources.StorageErrorMessage, ex.Message));
            if (retry) return await LoadTodosAsync();
            return false;
        }
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        _search = e.NewTextValue ?? string.Empty;
        ApplyFilter();
    }

    private void OnChipClicked(object? sender, EventArgs e)
    {
        var filter = sender == ChipTodayBtn ? ListFilter.Today
            : sender == ChipUpcomingBtn ? ListFilter.Upcoming
            : sender == ChipCompletedBtn ? ListFilter.Completed
            : ListFilter.Unscheduled;
        if (_filter == filter) return;
        _filter = filter;
        ApplyFilter();
    }

    /// <summary>按当前筛选 + 搜索词重建列表（TodoRowVm 构造需传 owner 集合）。</summary>
    private void ApplyFilter()
    {
        var todayEnd = DateTime.Today.AddDays(1);
        IEnumerable<TodoItem> rows = _filter switch
        {
            ListFilter.Unscheduled => _allTodos.Where(t => !t.IsDone && t.DueAt is null),
            ListFilter.Today => _allTodos.Where(t => !t.IsDone && t.DueAt is not null && t.DueAt.Value.LocalDateTime < todayEnd), // 含逾期
            ListFilter.Upcoming => _allTodos.Where(t => !t.IsDone && t.DueAt is not null && t.DueAt.Value.LocalDateTime >= todayEnd),
            _ => _allTodos.Where(t => t.IsDone),
        };
        if (_search.Length > 0)
            rows = rows.Where(t => t.Title.Contains(_search, StringComparison.OrdinalIgnoreCase));
        rows = rows.OrderBy(t => t.DueAt ?? DateTimeOffset.MaxValue).ThenBy(t => t.Id);

        _rows.Clear();
        foreach (var t in rows)
            _rows.Add(new TodoRowVm(t, _dispatcher, _rows));

        EmptyLabel.Text = _search.Length > 0 ? AppResources.SearchEmpty : _filter switch
        {
            ListFilter.Today => AppResources.EmptyToday,
            ListFilter.Upcoming => AppResources.EmptyUpcoming,
            ListFilter.Completed => AppResources.EmptyCompleted,
            _ => AppResources.EmptyUnscheduled,
        };
        HintLabel.IsVisible = _filter == ListFilter.Unscheduled;
        _dueConverter.UnscheduledMode = _filter == ListFilter.Unscheduled;
        RefreshChips();
    }

    /// <summary>选中 chip bg=SelectedBg text=Primary，未选 bg=Transparent text=TextSecondary。</summary>
    private void RefreshChips()
    {
        SetChip(ChipUnscheduled, ChipUnscheduledBtn, _filter == ListFilter.Unscheduled);
        SetChip(ChipToday, ChipTodayBtn, _filter == ListFilter.Today);
        SetChip(ChipUpcoming, ChipUpcomingBtn, _filter == ListFilter.Upcoming);
        SetChip(ChipCompleted, ChipCompletedBtn, _filter == ListFilter.Completed);
    }

    private static void SetChip(Border chip, Button btn, bool active)
    {
        var res = Application.Current!.Resources;
        chip.BackgroundColor = active ? (Color)res["SelectedBg"] : Colors.Transparent;
        btn.TextColor = active ? (Color)res["Primary"] : (Color)res["TextSecondary"];
    }

    private async void OnTodoTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Grid { BindingContext: TodoRowVm row })
            await Shell.Current.GoToAsync($"TaskEditorPage?id={row.Id}");
    }

    // ══════════ C5 新建入口 ══════════

    /// <summary>标题右侧「＋」：进入新建待办（TaskEditorPage 无参并非新建，故跳转 TextAddPage）。</summary>
    private async void OnAddTodo(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("TextAddPage");

    // ══════════ C1 删除 + 撤销 ══════════

    /// <summary>左滑删除：取行 Id → 暂存快照 → 走统一变更入口删除 → 本地移除 → 弹撤销条。</summary>
    private async void OnDeleteSwiped(object? sender, EventArgs e)
    {
        if (sender is not SwipeItem si || si.CommandParameter is not int id) return;
        var row = _rows.FirstOrDefault(r => r.Id == id);
        if (row is null) return;
        await DeleteWithUndoAsync(row);
    }

    private async Task DeleteWithUndoAsync(TodoRowVm row)
    {
        // 删除前从本地全量列表取快照（TodoRowVm 不持有原始 TodoItem）
        var snapshot = _allTodos.FirstOrDefault(t => t.Id == row.Id);
        if (snapshot is null) return;

        try
        {
            await _dispatcher.DeleteAsync(row.Id); // DEV-02：删除同时取消关联通知
        }
        catch (Exception ex)
        {
            await UserAlerts.ShowStorageErrorAsync(ex); // 失败不移除行
            return;
        }

        // 本地移除（快照已暂存，可撤销）
        _rows.Remove(row);
        _allTodos.RemoveAll(t => t.Id == row.Id);
        _undoSnapshot = snapshot;
        ShowUndoBar(snapshot.Title);
    }

    private void ShowUndoBar(string title)
    {
        UndoLabel.Text = string.Format(AppResources.Undo_DeletedFormat, title);
        UndoBar.IsVisible = true;
        // 取消上一次自动隐藏定时器（新删除覆盖旧快照）
        _undoCts?.Cancel();
        _undoCts = new CancellationTokenSource();
        var gen = ++_undoGeneration;
        _ = AutoHideUndoAsync(_undoCts, gen);
    }

    private async Task AutoHideUndoAsync(CancellationTokenSource cts, int gen)
    {
        try { await Task.Delay(2500, cts.Token); }
        catch (TaskCanceledException) { return; } // 被新删除取消：不隐藏
        // 代次守卫：仅当本定时器仍是最新时隐藏
        if (gen != _undoGeneration) return;
        Dispatcher.Dispatch(() =>
        {
            if (gen == _undoGeneration)
            {
                UndoBar.IsVisible = false;
                _undoSnapshot = null;
            }
        });
    }

    /// <summary>撤销删除：用暂存快照走统一入口重建（分配新 Id），随后重新加载列表。</summary>
    private async void OnUndoClicked(object? sender, EventArgs e)
    {
        if (_undoSnapshot is null) return;
        _undoCts?.Cancel();
        var snapshot = _undoSnapshot;
        _undoSnapshot = null;
        UndoBar.IsVisible = false;
        try
        {
            var result = await _dispatcher.CreateAsync(snapshot);
            if (!result.Saved)
            {
                await UserAlerts.ShowAsync(AppResources.Undo_Action, AppResources.Undo_Failed);
                return;
            }
        }
        catch (Exception ex)
        {
            await UserAlerts.ShowStorageErrorAsync(ex);
            return;
        }
        if (!await LoadTodosAsync()) return;
        ApplyFilter();
    }

    private static ListFilter ParseFilter(string value) => value.Trim().ToLowerInvariant() switch
    {
        "today" => ListFilter.Today,
        "upcoming" => ListFilter.Upcoming,
        "completed" => ListFilter.Completed,
        _ => ListFilter.Unscheduled,
    };
}

/// <summary>副行时间文案：空 DueLabel 在待安排筛选下显示 NoTimeLabel，其余显示为空。</summary>
public sealed class DueLabelConverter : IValueConverter
{
    public bool UnscheduledMode { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s && s.Length > 0) return s;
        return UnscheduledMode ? AppResources.NoTimeLabel : string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
