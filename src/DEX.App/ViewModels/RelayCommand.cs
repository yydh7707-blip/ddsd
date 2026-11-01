using System.Windows.Input;

namespace DEX.App.ViewModels;

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _can;
    public RelayCommand(Action<object?> execute, Func<object?, bool>? can = null) { _execute = execute; _can = can; }
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => _can?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);
    public void Raise() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Func<object?, bool>? _can;
    private bool _busy;
    public AsyncRelayCommand(Func<object?, Task> execute, Func<object?, bool>? can = null) { _execute = execute; _can = can; }
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_busy && (_can?.Invoke(parameter) ?? true);
    public async void Execute(object? parameter)
    {
        _busy = true;
        Raise();
        try { await _execute(parameter); }
        finally { _busy = false; Raise(); }
    }
    public void Raise() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
