using System.Windows.Input;

namespace EdifierMrVolume;

/// <summary>Partial, so C#/WinRT can generate its interop code for Native AOT.</summary>
internal sealed partial class RelayCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => execute();
}
