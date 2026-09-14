using System.Windows.Input;

namespace VoiceTodo.Maui.ViewModels;

/// <summary>轻量 ICommand 实现（避免引入额外的 MVVM 包）。</summary>
public class AsyncCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;

    public AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    // ICommand 契约成员：命令执行不改变 CanExecute 结果，故从不触发此事件。
#pragma warning disable CS0067
    public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public async void Execute(object? parameter) => await _execute();
}
