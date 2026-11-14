using System.Windows.Input;

namespace SampleApp;

/// <summary>Minimal <see cref="ICommand"/> for button bindings.</summary>
internal sealed class RelayCommand(Action execute, Func<bool> canExecute) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => canExecute();

    public void Execute(object? parameter) => execute();
}
