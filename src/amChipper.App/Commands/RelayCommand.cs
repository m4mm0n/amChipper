using System.Windows.Input;

namespace amChipper.App.Commands;

/// <summary>Generic ICommand implementation wired to lambdas.</summary>
public sealed class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    : ICommand
{
    /// <summary>
    /// Raised when EventHandler changes.
    /// </summary>
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    /// <summary>
    /// Executes the CanExecute operation.
    /// </summary>
    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;
    /// <summary>
    /// Executes the Execute operation.
    /// </summary>
    public void Execute(object? parameter) => execute(parameter);

    /// <summary>
    /// Executes the RaiseCanExecuteChanged operation.
    /// </summary>
    public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
}

/// <summary>Non-generic convenience alias.</summary>
public sealed class RelayCommand<T>(Action<T?> execute, Func<T?, bool>? canExecute = null)
    : ICommand
{
    /// <summary>
    /// Raised when EventHandler changes.
    /// </summary>
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    /// <summary>
    /// Executes the CanExecute operation.
    /// </summary>
    public bool CanExecute(object? parameter) =>
        canExecute?.Invoke(parameter is T t ? t : default) ?? true;

    /// <summary>
    /// Executes the Execute operation.
    /// </summary>
    public void Execute(object? parameter) =>
        execute(parameter is T t ? t : default);
}
