using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Native.Core;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
}

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _run;
    private readonly Func<object?, bool>? _canRun;

    public RelayCommand(Action<object?> run, Func<object?, bool>? canRun = null)
    {
        _run = run;
        _canRun = canRun;
    }

    public RelayCommand(Action run) : this(_ => run()) { }

    public RelayCommand(Action run, Func<object?, bool> canRun) : this(_ => run(), canRun) { }

    /// <summary>Pede para a interface reavaliar se o comando pode rodar.</summary>
    public static void Refresh() => CommandManager.InvalidateRequerySuggested();

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canRun?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _run(parameter);
}
