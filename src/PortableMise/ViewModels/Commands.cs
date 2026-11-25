using System.Windows.Input;

namespace PortableMise.ViewModels;

public sealed class RelayCommand(Action action, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) { if (CanExecute(parameter)) action(); }
}

public sealed class AsyncCommand(Func<Task> action, Action<Exception> onError, Func<bool>? canExecute = null) : ICommand
{
    private bool executing;
    public Task? ExecutionTask { get; private set; }
    public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
    public bool CanExecute(object? parameter) => !executing && (canExecute?.Invoke() ?? true);
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        executing = true;
        CommandManager.InvalidateRequerySuggested();
        try { ExecutionTask = action(); await ExecutionTask; }
        catch (Exception error) { onError(error); }
        finally { executing = false; CommandManager.InvalidateRequerySuggested(); }
    }
}
