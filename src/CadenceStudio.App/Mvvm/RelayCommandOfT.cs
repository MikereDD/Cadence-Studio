using System.Windows.Input;

namespace CadenceStudio.App.Mvvm;

public sealed class RelayCommand<T>(Action<T?> execute, Predicate<T?>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke(Convert(parameter)) ?? true;
    public void Execute(object? parameter) => execute(Convert(parameter));

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    private static T? Convert(object? parameter)
    {
        if (parameter is null)
        {
            return default;
        }

        return parameter is T value ? value : default;
    }
}
