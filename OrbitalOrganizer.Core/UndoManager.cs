using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using OrbitalOrganizer.Core.Models;

namespace OrbitalOrganizer.Core;

public class UndoManager : INotifyPropertyChanged
{
    private const int MaxHistorySize = 10;

    private readonly LinkedList<UndoOperation> _undoStack = new();
    private readonly LinkedList<UndoOperation> _redoStack = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public string UndoDescription => _undoStack.Count > 0 ? _undoStack.Last!.Value.Description : "";
    public string RedoDescription => _redoStack.Count > 0 ? _redoStack.Last!.Value.Description : "";

    public void RecordChange(UndoOperation operation)
    {
        if (operation == null) return;

        _undoStack.AddLast(operation);

        while (_undoStack.Count > MaxHistorySize)
            _undoStack.RemoveFirst();

        _redoStack.Clear();
        RaiseAllPropertyChanges();
    }

    public void Undo()
    {
        if (_undoStack.Count == 0) return;

        var operation = _undoStack.Last!.Value;
        _undoStack.RemoveLast();

        operation.Undo();

        _redoStack.AddLast(operation);
        while (_redoStack.Count > MaxHistorySize)
            _redoStack.RemoveFirst();

        RaiseAllPropertyChanges();
    }

    public void Redo()
    {
        if (_redoStack.Count == 0) return;

        var operation = _redoStack.Last!.Value;
        _redoStack.RemoveLast();

        operation.Redo();

        _undoStack.AddLast(operation);
        while (_undoStack.Count > MaxHistorySize)
            _undoStack.RemoveFirst();

        RaiseAllPropertyChanges();
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        RaiseAllPropertyChanges();
    }

    private void RaiseAllPropertyChanges()
    {
        RaisePropertyChanged(nameof(CanUndo));
        RaisePropertyChanged(nameof(CanRedo));
        RaisePropertyChanged(nameof(UndoDescription));
        RaisePropertyChanged(nameof(RedoDescription));
    }

    private void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public abstract class UndoOperation
{
    public abstract string Description { get; }
    public abstract void Undo();
    public abstract void Redo();
}

public class PropertyEditOperation : UndoOperation
{
    public SaturnGame Item { get; set; } = null!;
    public string PropertyName { get; set; } = "";
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }

    public override string Description => $"Edit {PropertyName}";

    public override void Undo() => SetPropertyValue(OldValue);
    public override void Redo() => SetPropertyValue(NewValue);

    private void SetPropertyValue(string? value)
    {
        Item.SidecarsDirty = true;
        switch (PropertyName)
        {
            case nameof(SaturnGame.Name): Item.Name = value ?? ""; break;
            case nameof(SaturnGame.Folder): Item.Folder = value ?? ""; break;
            case nameof(SaturnGame.ProductId):
                Item.ProductId = value ?? "";
                Item.ProductIdDirty = true;
                break;
            case nameof(SaturnGame.Disc): Item.Disc = value ?? ""; break;
        }
    }
}

public class MultiPropertyEditOperation : UndoOperation
{
    public string PropertyName { get; set; } = "";
    public List<(SaturnGame Item, string? OldValue, string? NewValue)> Edits { get; set; } = new();
    private readonly string? _customDescription;

    public MultiPropertyEditOperation(string? description = null)
    {
        _customDescription = description;
    }

    public bool HasChanges => Edits.Count > 0;

    public void AddChange(SaturnGame item, string? oldValue, string? newValue)
    {
        Edits.Add((item, oldValue, newValue));
    }

    public override string Description =>
        _customDescription ?? (Edits.Count == 1 ? $"Edit {PropertyName}" : $"Edit {Edits.Count} Items");

    public override void Undo()
    {
        foreach (var (item, oldValue, _) in Edits)
            SetPropertyValue(item, oldValue);
    }

    public override void Redo()
    {
        foreach (var (item, _, newValue) in Edits)
            SetPropertyValue(item, newValue);
    }

    private void SetPropertyValue(SaturnGame item, string? value)
    {
        item.SidecarsDirty = true;
        switch (PropertyName)
        {
            case nameof(SaturnGame.Name): item.Name = value ?? ""; break;
            case nameof(SaturnGame.Folder): item.Folder = value ?? ""; break;
            case nameof(SaturnGame.ProductId):
                item.ProductId = value ?? "";
                item.ProductIdDirty = true;
                break;
            case nameof(SaturnGame.Disc): item.Disc = value ?? ""; break;
        }
    }
}

public class ListReorderOperation : UndoOperation
{
    public ObservableCollection<SaturnGame> ItemList { get; set; } = null!;
    public List<SaturnGame> OldOrder { get; set; } = new();
    public List<SaturnGame> NewOrder { get; set; } = new();
    private readonly string _description;

    public ListReorderOperation(string description = "Reorder List")
    {
        _description = description;
    }

    public override string Description => _description;

    public override void Undo() => ApplyOrder(OldOrder);
    public override void Redo() => ApplyOrder(NewOrder);

    private void ApplyOrder(List<SaturnGame> order)
    {
        if (ItemList == null || order == null) return;
        ItemList.Clear();
        foreach (var item in order)
            ItemList.Add(item);
    }
}

public class MultiItemAddOperation : UndoOperation
{
    public ObservableCollection<SaturnGame> ItemList { get; set; } = null!;
    public List<(SaturnGame Item, int Index)> Items { get; set; } = new();

    public override string Description => Items.Count == 1 ? "Add Item" : $"Add {Items.Count} Items";

    public override void Undo()
    {
        if (ItemList == null) return;
        for (int i = Items.Count - 1; i >= 0; i--)
            ItemList.Remove(Items[i].Item);
    }

    public override void Redo()
    {
        if (ItemList == null) return;
        foreach (var (item, index) in Items)
        {
            if (index >= 0 && index <= ItemList.Count)
                ItemList.Insert(index, item);
            else
                ItemList.Add(item);
        }
    }
}

public class BatchFolderRenameOperation : UndoOperation
{
    public class ItemSnapshot
    {
        public SaturnGame Item { get; set; } = null!;
        public string OldFolder { get; set; } = "";
        public string NewFolder { get; set; } = "";
        public List<string> OldAltFolders { get; set; } = new();
        public List<string> NewAltFolders { get; set; } = new();
    }

    public List<ItemSnapshot> Snapshots { get; set; } = new();

    public override string Description => "Batch Folder Rename";

    public override void Undo()
    {
        foreach (var s in Snapshots)
        {
            s.Item.Folder = s.OldFolder;
            s.Item.AlternativeFolders = new List<string>(s.OldAltFolders);
            s.Item.SidecarsDirty = true;
        }
    }

    public override void Redo()
    {
        foreach (var s in Snapshots)
        {
            s.Item.Folder = s.NewFolder;
            s.Item.AlternativeFolders = new List<string>(s.NewAltFolders);
            s.Item.SidecarsDirty = true;
        }
    }
}

public class AltFoldersChangeOperation : UndoOperation
{
    public SaturnGame Item { get; set; } = null!;
    public List<string> OldAltFolders { get; set; } = new();
    public List<string> NewAltFolders { get; set; } = new();

    public override string Description => "Assign Additional Folder Paths";

    public override void Undo()
    {
        Item.AlternativeFolders = new List<string>(OldAltFolders);
        Item.SidecarsDirty = true;
    }

    public override void Redo()
    {
        Item.AlternativeFolders = new List<string>(NewAltFolders);
        Item.SidecarsDirty = true;
    }
}

public class MultiItemRemoveOperation : UndoOperation
{
    public ObservableCollection<SaturnGame> ItemList { get; set; } = null!;
    public List<(SaturnGame Item, int Index)> Items { get; set; } = new();

    public override string Description => Items.Count == 1 ? "Remove Item" : $"Remove {Items.Count} Items";

    public override void Undo()
    {
        if (ItemList == null) return;
        var sorted = new List<(SaturnGame Item, int Index)>(Items);
        sorted.Sort((a, b) => a.Index.CompareTo(b.Index));

        foreach (var (item, index) in sorted)
        {
            if (index >= 0 && index <= ItemList.Count)
                ItemList.Insert(index, item);
            else
                ItemList.Add(item);
        }
    }

    public override void Redo()
    {
        if (ItemList == null) return;
        var sorted = new List<(SaturnGame Item, int Index)>(Items);
        sorted.Sort((a, b) => b.Index.CompareTo(a.Index));

        foreach (var (item, _) in sorted)
            ItemList.Remove(item);
    }
}
