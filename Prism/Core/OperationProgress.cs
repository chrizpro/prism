using System;
using System.Collections.ObjectModel;
using System.Threading;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Prism.Core;

/// <summary>Document-scoped running work. Each lease owns its updates and cannot overwrite another job.</summary>
public sealed partial class OperationTracker : ObservableObject
{
    public ObservableCollection<OperationProgress> Items { get; } = [];
    public bool HasTasks => Items.Count > 0;
    public OperationTracker() => Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasTasks));
    public OperationProgress Begin(string title, Action? cancel = null)
    {
        var operation = new OperationProgress(this, title, cancel);
        Dispatch(() => Items.Add(operation));
        return operation;
    }
    internal static void Dispatch(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action(); else Dispatcher.UIThread.Post(action);
    }
}

public sealed partial class OperationProgress : ObservableObject, IDisposable
{
    readonly OperationTracker _owner;
    readonly Action? _cancel;
    int _disposed;
    bool _stopRequested;
    public string Title { get; }
    [ObservableProperty] public partial string Status { get; set; } = "Starting…";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsIndeterminate)), NotifyPropertyChangedFor(nameof(Percent))]
    public partial double? Fraction { get; set; }
    public bool IsIndeterminate => Fraction == null;
    public double Percent => 100 * (Fraction ?? 0);
    public bool CanCancel => _cancel != null && !_stopRequested;
    public IRelayCommand CancelCommand { get; }

    internal OperationProgress(OperationTracker owner, string title, Action? cancel)
    {
        _owner = owner; Title = title; _cancel = cancel;
        CancelCommand = new RelayCommand(() =>
        {
            if (!CanCancel || Volatile.Read(ref _disposed) != 0) return;
            _stopRequested = true;
            Status = "Stopping after the current step…";
            OnPropertyChanged(nameof(CanCancel)); CancelCommand!.NotifyCanExecuteChanged();
            _cancel!();
        }, () => CanCancel && Volatile.Read(ref _disposed) == 0);
    }
    public void Report(string status, double? fraction = null) => OperationTracker.Dispatch(() =>
    {
        if (Volatile.Read(ref _disposed) != 0 || _stopRequested) return;
        Status = status;
        Fraction = fraction is { } value && double.IsFinite(value) ? Math.Clamp(value, 0, 1) : null;
    });
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            OperationTracker.Dispatch(() => _owner.Items.Remove(this));
    }
}

public sealed partial class CadSession
{
    public OperationTracker Work { get; } = new();
}
