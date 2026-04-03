using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OrbitalOrganizer;

public class FolderTreeNode : INotifyPropertyChanged
{
    private string _name = "";
    public string Name
    {
        get => _name;
        set
        {
            var sanitized = StripNonPrintableAscii(value);
            if (_name != sanitized)
            {
                _name = sanitized;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayName));
                UpdateFullPath();
            }
        }
    }

    private string _fullPath = "";
    public string FullPath
    {
        get => _fullPath;
        set
        {
            if (_fullPath != value)
            {
                _fullPath = value;
                OnPropertyChanged();
            }
        }
    }

    private int _directGameCount;
    public int DirectGameCount
    {
        get => _directGameCount;
        set
        {
            if (_directGameCount != value)
            {
                _directGameCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    private int _totalGameCount;
    public int TotalGameCount
    {
        get => _totalGameCount;
        set
        {
            if (_totalGameCount != value)
            {
                _totalGameCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public string DisplayName => Name;

    public FolderTreeNode? Parent { get; set; }

    private bool _isExpanded = true;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                OnPropertyChanged();
            }
        }
    }

    private bool _isEditing;
    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if (_isEditing != value)
            {
                _isEditing = value;
                OnPropertyChanged();
            }
        }
    }

    private bool _isDropTarget;
    public bool IsDropTarget
    {
        get => _isDropTarget;
        set
        {
            if (_isDropTarget != value)
            {
                _isDropTarget = value;
                OnPropertyChanged();
            }
        }
    }

    public ObservableCollection<FolderTreeNode> Children { get; } = new();

    public string OriginalFullPath { get; set; } = "";

    public bool IsRootNode { get; set; }

    public void UpdateFullPath()
    {
        if (IsRootNode)
        {
            FullPath = "";
        }
        else if (Parent == null || Parent.IsRootNode)
        {
            FullPath = Name;
        }
        else
        {
            FullPath = string.IsNullOrEmpty(Parent.FullPath) ? Name : $"{Parent.FullPath}\\{Name}";
        }

        foreach (var child in Children)
            child.UpdateFullPath();
    }

    public void RecalculateCounts()
    {
        if (IsRootNode)
            return;

        TotalGameCount = DirectGameCount;
        foreach (var child in Children)
        {
            child.RecalculateCounts();
            TotalGameCount += child.TotalGameCount;
        }
    }

    public void SortChildren()
    {
        var sorted = Children.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
        Children.Clear();
        foreach (var child in sorted)
        {
            Children.Add(child);
            child.SortChildren();
        }
    }

    public static bool IsValidPrintableAscii(string text)
    {
        if (string.IsNullOrEmpty(text))
            return true;
        return text.All(c => c >= 0x20 && c <= 0x7E);
    }

    public static string StripNonPrintableAscii(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        return new string(text.Where(c => c >= 0x20 && c <= 0x7E).ToArray());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
