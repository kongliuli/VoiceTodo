using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using VoiceTodo.Core.Abstractions;
using VoiceTodo.Core.Models;
using VoiceTodo.Core.Services;
using VoiceTodo.Maui.Services;

namespace VoiceTodo.Maui.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly ISpeechRecognizer _recognizer;
    private readonly VoicePipeline _pipeline;
    private readonly ILocalizer _localizer;
    private readonly ITodoRepository _repo;
    private readonly ITodoChangeDispatcher _dispatcher; // 行内勾选/删除走统一变更入口（同步取消/重排提醒）

    private string _status = "";
    private string _latestCommand = "";
    private bool _isListening;
    private bool _isError;
    private int _totalPending;
    private int _runningCount;
    private int _unscheduledCount;
    private bool _refreshing;
    private readonly ObservableCollection<TodoRowVm> _todos = new();
    private readonly ObservableCollection<TodoRowVm> _doneTodos = new();
    private readonly ObservableCollection<TimerItem> _timers = new();

    public MainViewModel(ISpeechRecognizer recognizer, VoicePipeline pipeline, ILocalizer localizer,
        ITodoRepository repo, ITodoChangeDispatcher dispatcher)
    {
        _recognizer = recognizer;
        _pipeline = pipeline;
        _localizer = localizer;
        _repo = repo;
        _dispatcher = dispatcher;
        ListenCommand = new AsyncCommand(ListenAsync, () => !IsListening);
    }

    public string Status { get => _status; set => Set(ref _status, value); }
    public string LatestCommand { get => _latestCommand; set => Set(ref _latestCommand, value); }
    public bool IsListening { get => _isListening; set => Set(ref _isListening, value); }
    public bool IsError { get => _isError; set => Set(ref _isError, value); }

    /// <summary>今日待办：今日及逾期的未完成项。</summary>
    public ObservableCollection<TodoRowVm> Todos => _todos;

    /// <summary>今日已完成项。</summary>
    public ObservableCollection<TodoRowVm> DoneTodos => _doneTodos;
    public ObservableCollection<TimerItem> Timers => _timers;

    /// <summary>全部未完成数（摘要行用）。</summary>
    public int TotalPending { get => _totalPending; private set => Set(ref _totalPending, value); }

    /// <summary>运行中计时数（0/1）。</summary>
    public int RunningCount { get => _runningCount; private set => Set(ref _runningCount, value); }

    /// <summary>待安排数：无时间的未完成项。</summary>
    public int UnscheduledCount { get => _unscheduledCount; private set => Set(ref _unscheduledCount, value); }

    public int DoneCount => _doneTodos.Count;

    public ICommand ListenCommand { get; }

    private async Task ListenAsync()
    {
        if (IsListening) return;
        IsListening = true;
        IsError = false;
        Status = _localizer["Listening"];
        try
        {
            var text = await _recognizer.RecognizeAsync();
            if (!string.IsNullOrWhiteSpace(text))
            {
                LatestCommand = text;
                var cmd = await _pipeline.ProcessTextAsync(text);
                await RefreshAsync();
                // 免提路径：语音创建的计时已就绪（TimerLauncher 已投放）→ 跳到计时页，OnAppearing 消费进准备态
                if (cmd.Type == CommandType.Timer && cmd.Action == CommandAction.Add
                    && TimerLauncher.PendingStart is not null && Shell.Current is not null)
                {
                    await Shell.Current.GoToAsync("//TimerPage");
                }
            }
        }
        catch (Exception ex)
        {
            IsError = true;
            Status = ex.Message;
        }
        finally
        {
            IsListening = false;
            if (!IsError) Status = "";
        }
    }

    public async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var todos = await _repo.GetTodosAsync();
            var today = DateTime.Now.Date;

            _todos.Clear();
            foreach (var t in todos.Where(t => !t.IsDone && t.DueAt.HasValue && t.DueAt.Value.ToLocalTime().Date <= today))
                Attach(t, _todos);

            _doneTodos.Clear();
            foreach (var t in todos.Where(t => t.IsDone && t.DueAt.HasValue && t.DueAt.Value.ToLocalTime().Date <= today))
                Attach(t, _doneTodos);

            _timers.Clear();
            var timers = await _repo.GetTimersAsync();
            foreach (var t in timers)
                _timers.Add(t);

            TotalPending = todos.Count(t => !t.IsDone);
            RunningCount = RunningTimerHub.Current is null ? 0 : 1;
            UnscheduledCount = todos.Count(t => !t.IsDone && t.DueAt is null);
            OnPropertyChanged(nameof(DoneCount));
        }
        catch (StorageException ex)
        {
            // 读取失败不降级为空库：保留上一份列表，仅提示用户
            await UserAlerts.ShowStorageErrorAsync(ex.Message);
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>行内勾选切换后重建分区（pending → 已完成 及反向）。</summary>
    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TodoRowVm.IsDone))
            _ = RefreshAsync();
    }

    private void Attach(TodoItem item, ObservableCollection<TodoRowVm> owner)
    {
        var row = new TodoRowVm(item, _dispatcher, owner);
        row.PropertyChanged += OnRowPropertyChanged;
        owner.Add(row);
    }

    /// <summary>计时中枢快照变化后同步运行计数（回调已在 UI 线程）。</summary>
    public void RefreshRunningCount()
        => RunningCount = RunningTimerHub.Current is null ? 0 : 1;

    /// <summary>打字添加：交给语音管线解析入库后刷新。</summary>
    public async Task AddTextAsync(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        await _pipeline.ProcessTextAsync(text);
        await RefreshAsync();
    }

    public async Task DeleteTimerAsync(int id)
    {
        await _repo.DeleteTimerAsync(id);
        var timer = _timers.FirstOrDefault(t => t.Id == id);
        if (timer is not null)
            _timers.Remove(timer);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
