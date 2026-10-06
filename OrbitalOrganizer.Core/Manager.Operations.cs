using OrbitalOrganizer.Core.Models;

namespace OrbitalOrganizer.Core;

public partial class Manager
{
    private int _operationActive;
    private int _mutationThread;
    private bool _needsRecovery;
    public bool IsOperationActive => Volatile.Read(ref _operationActive) != 0;
    public bool NeedsRecovery => _needsRecovery;
    public bool CanEdit => !IsOperationActive && !NeedsRecovery;

    public Manager()
    {
        ItemList = new GuardedGameCollection(EnsureCanMutate, NotifyObservers);
        UndoManager = new UndoManager(EnsureCanMutate, () => CanEdit, NotifyObservers);
    }

    private void EnsureCanMutate()
    {
        if (_mutationThread == Environment.CurrentManagedThreadId) return;
        if (!CanEdit) throw new InvalidOperationException("Finish the current operation or recover the card before editing.");
    }

    private void Mutate(Action action)
    {
        int previous = _mutationThread;
        _mutationThread = Environment.CurrentManagedThreadId;
        try { action(); }
        finally { _mutationThread = previous; }
    }

    private void NotifyObservers(Action action)
    {
        int previous = _mutationThread;
        _mutationThread = 0;
        try { action(); }
        finally { _mutationThread = previous; }
    }

    private async Task<T> RunOperationAsync<T>(Func<Task<T>> action)
    {
        if (Interlocked.CompareExchange(ref _operationActive, 1, 0) != 0)
            throw new InvalidOperationException("Another card operation is already active.");
        try { NotifyOperationState(); return await action(); }
        finally { Volatile.Write(ref _operationActive, 0); NotifyOperationState(); }
    }

    private Task RunOperationAsync(Func<Task> action) => RunOperationAsync(async () => { await action(); return true; });

    private void NotifyOperationState()
    {
        OnPropertyChanged(nameof(IsOperationActive));
        OnPropertyChanged(nameof(NeedsRecovery));
        OnPropertyChanged(nameof(CanEdit));
        UndoManager.NotifyEligibilityChanged();
    }

    private void MarkRemoved(SaturnGame game)
    {
        EnsureCanMutate();
        if (!_removedItems.Contains(game)) _removedItems.Add(game);
    }

    private void Reinstate(SaturnGame game)
    {
        EnsureCanMutate();
        _removedItems.Remove(game);
    }
}
