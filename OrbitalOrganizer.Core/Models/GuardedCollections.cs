using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace OrbitalOrganizer.Core.Models;

internal sealed class GuardedGameCollection(Action ensureCanMutate, Action<Action> notifyObservers) : ObservableCollection<SaturnGame>
{
    protected override void InsertItem(int index, SaturnGame item)
    {
        ensureCanMutate();
        item.EnsureCanMutate = ensureCanMutate;
        item.NotifyObservers = notifyObservers;
        base.InsertItem(index, item);
    }

    protected override void SetItem(int index, SaturnGame item)
    {
        ensureCanMutate();
        item.EnsureCanMutate = ensureCanMutate;
        item.NotifyObservers = notifyObservers;
        base.SetItem(index, item);
    }

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e) => notifyObservers(() => base.OnCollectionChanged(e));
    protected override void OnPropertyChanged(PropertyChangedEventArgs e) => notifyObservers(() => base.OnPropertyChanged(e));

    protected override void RemoveItem(int index) { ensureCanMutate(); base.RemoveItem(index); }
    protected override void ClearItems() { ensureCanMutate(); base.ClearItems(); }
    protected override void MoveItem(int oldIndex, int newIndex) { ensureCanMutate(); base.MoveItem(oldIndex, newIndex); }
}

internal sealed class GuardedImageCollection : Collection<string>
{
    private readonly Action _ensureCanMutate;

    internal GuardedImageCollection(Action ensureCanMutate, IEnumerable<string>? items = null)
        : base(items?.ToList() ?? new List<string>()) => _ensureCanMutate = ensureCanMutate;

    protected override void InsertItem(int index, string item) { _ensureCanMutate(); base.InsertItem(index, item); }
    protected override void SetItem(int index, string item) { _ensureCanMutate(); base.SetItem(index, item); }
    protected override void RemoveItem(int index) { _ensureCanMutate(); base.RemoveItem(index); }
    protected override void ClearItems() { _ensureCanMutate(); base.ClearItems(); }
}
