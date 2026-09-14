using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using VoiceTodo.Core.Abstractions;
using VoiceTodo.Core.Models;
using VoiceTodo.Maui.Services;

namespace VoiceTodo.Maui.ViewModels;

/// <summary>
/// 待办列表的单行视图模型：负责勾选切换与删除交互。
/// 勾选/删除统一走 <see cref="ITodoChangeDispatcher"/>（而非直接调仓库），
/// 保证完成/取消勾选/删除都会同步取消或重排系统通知（见 page-optimization-review §完成/删除未取消提醒）。
/// 存储异常经 <see cref="UserAlerts"/> 提示，勾选失败会回滚本地状态。
/// </summary>
public class TodoRowVm : INotifyPropertyChanged
{
    private readonly ITodoChangeDispatcher _dispatcher;
    private readonly ObservableCollection<TodoRowVm> _owner;
    private bool _isDone;

    public int Id { get; }
    public string Title { get; }
    public string DueLabel { get; }
    public string RecurrenceLabel { get; }
    public ICommand ToggleDoneCommand { get; }
    public ICommand DeleteCommand { get; }

    public TodoRowVm(TodoItem item, ITodoChangeDispatcher dispatcher, ObservableCollection<TodoRowVm> owner)
    {
        _dispatcher = dispatcher;
        _owner = owner;
        Id = item.Id;
        Title = item.Title;
        _isDone = item.IsDone;
        DueLabel = FormatDue(item.DueAt);
        RecurrenceLabel = FormatRecurrence(item.RecurrenceRule);
        ToggleDoneCommand = new AsyncCommand(ToggleDoneAsync);
        DeleteCommand = new AsyncCommand(DeleteAsync);
    }

    public bool IsDone
    {
        get => _isDone;
        private set
        {
            if (_isDone == value) return;
            _isDone = value;
            OnPropertyChanged();
        }
    }

    /// <summary>勾选/取消勾选：完成→取消全部提醒；取消勾选→按当前时间重排。失败回滚状态并提示。</summary>
    public async Task ToggleDoneAsync()
    {
        var target = !_isDone;
        IsDone = target;
        try
        {
            await _dispatcher.SetDoneAsync(Id, target);
        }
        catch (Exception ex)
        {
            IsDone = !target; // 落库失败：回滚，避免列表与存储不一致
            await UserAlerts.ShowStorageErrorAsync(ex);
        }
    }

    /// <summary>删除：落库 + 取消全部关联通知；失败提示且不移除行。</summary>
    public async Task DeleteAsync()
    {
        try
        {
            await _dispatcher.DeleteAsync(Id);
            _owner.Remove(this);
        }
        catch (Exception ex)
        {
            await UserAlerts.ShowStorageErrorAsync(ex);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private static string FormatDue(DateTimeOffset? due)
    {
        if (due is null) return "";
        var local = due.Value.ToLocalTime();
        var now = DateTime.Now;
        if (local.Date == now.Date) return $"今天 {local:HH:mm}";
        if (local.Date == now.Date.AddDays(1)) return $"明天 {local:HH:mm}";
        return local.ToString("M月d日 HH:mm");
    }

    private static string FormatRecurrence(string? rule)
    {
        if (string.IsNullOrWhiteSpace(rule)) return "";
        var r = rule.Trim().ToLowerInvariant();
        if (r is "every day" or "daily") return "每天";
        if (r.Contains("weekday")) return "每工作日";
        return r switch
        {
            "every monday" => "每周一",
            "every tuesday" => "每周二",
            "every wednesday" => "每周三",
            "every thursday" => "每周四",
            "every friday" => "每周五",
            "every saturday" => "每周六",
            "every sunday" => "每周日",
            _ => ""
        };
    }
}