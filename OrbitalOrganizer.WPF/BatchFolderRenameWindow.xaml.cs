using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using TextBox = System.Windows.Controls.TextBox;

namespace OrbitalOrganizer;

public partial class BatchFolderRenameWindow : Window, INotifyPropertyChanged
{
    private Point _dragStartPoint;
    private FolderTreeNode? _draggedNode;
    private FolderTreeNode? _clickedNode;
    private FolderTreeNode? _currentDropTarget;
    private Stack<UndoOperation> _undoStack = new();
    private const int MaxUndoOperations = 10;

    public ObservableCollection<FolderTreeNode> RootNodes { get; } = new();

    private bool _canUndo;
    public bool CanUndo
    {
        get => _canUndo;
        set
        {
            if (_canUndo != value)
            {
                _canUndo = value;
                OnPropertyChanged();
            }
        }
    }

    private abstract class UndoOperation
    {
        public abstract void Undo();
    }

    private class MoveOperation : UndoOperation
    {
        public FolderTreeNode Node { get; set; } = null!;
        public FolderTreeNode OldParent { get; set; } = null!;
        public FolderTreeNode NewParent { get; set; } = null!;
        public int OldIndex { get; set; }

        public override void Undo()
        {
            NewParent.Children.Remove(Node);

            Node.Parent = OldParent;
            if (OldIndex >= OldParent.Children.Count)
                OldParent.Children.Add(Node);
            else
                OldParent.Children.Insert(OldIndex, Node);

            var node = OldParent;
            while (node != null)
            {
                node.RecalculateCounts();
                node = node.Parent;
            }
            node = NewParent;
            while (node != null)
            {
                node.RecalculateCounts();
                node = node.Parent;
            }

            Node.UpdateFullPath();
            OldParent?.SortChildren();
            NewParent?.SortChildren();
        }
    }

    private class RenameOperation : UndoOperation
    {
        public FolderTreeNode Node { get; set; } = null!;
        public string OldName { get; set; } = "";
        public string NewName { get; set; } = "";

        public override void Undo()
        {
            Node.Name = OldName;
            Node.Parent?.SortChildren();
        }
    }

    public Dictionary<string, string>? FolderMappings { get; private set; }

    public BatchFolderRenameWindow(Dictionary<string, int> folderCounts, int totalItemCount)
    {
        InitializeComponent();
        DataContext = this;

        BuildTree(folderCounts, totalItemCount);
    }

    private void BuildTree(Dictionary<string, int> folderCounts, int totalItemCount)
    {
        var allNodes = new Dictionary<string, FolderTreeNode>(StringComparer.Ordinal);
        var topLevelNodes = new List<FolderTreeNode>();

        var sortedPaths = folderCounts.Keys
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .OrderBy(p => p.Count(c => c == '\\'))
            .ThenBy(p => p);

        foreach (var path in sortedPaths)
        {
            var segments = path.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            FolderTreeNode? parent = null;
            string currentPath = "";

            for (int i = 0; i < segments.Length; i++)
            {
                currentPath = i == 0 ? segments[i] : $"{currentPath}\\{segments[i]}";

                if (!allNodes.ContainsKey(currentPath))
                {
                    var node = new FolderTreeNode
                    {
                        Name = segments[i],
                        FullPath = currentPath,
                        OriginalFullPath = currentPath,
                        Parent = parent
                    };

                    if (currentPath == path && folderCounts.ContainsKey(path))
                        node.DirectGameCount = folderCounts[path];

                    allNodes[currentPath] = node;

                    if (parent == null)
                        topLevelNodes.Add(node);
                    else
                        parent.Children.Add(node);
                }

                parent = allNodes[currentPath];
            }
        }

        var rootNode = new FolderTreeNode
        {
            Name = "(Root)",
            IsRootNode = true,
            IsExpanded = true,
            FullPath = "",
            OriginalFullPath = "",
            DirectGameCount = totalItemCount,
            TotalGameCount = totalItemCount
        };

        foreach (var topNode in topLevelNodes)
        {
            topNode.Parent = rootNode;
            rootNode.Children.Add(topNode);
            topNode.RecalculateCounts();
        }

        rootNode.SortChildren();
        RootNodes.Add(rootNode);
    }

    // --- Inline editing ---

    private string? _editingOriginalName;

    private void TreeViewItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement element && element.DataContext is FolderTreeNode node)
        {
            if (!node.IsRootNode)
            {
                _editingOriginalName = node.Name;
                node.IsEditing = true;
                e.Handled = true;
            }
        }
    }

    private void EditTextBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.Focus();
            textBox.SelectAll();
        }
    }

    private void EditTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is FolderTreeNode node)
        {
            node.IsEditing = false;

            if (!FolderTreeNode.IsValidPrintableAscii(node.Name))
            {
                MessageBox.Show(
                    "Only printable ASCII characters (letters, numbers, and standard symbols) are supported.",
                    "Invalid Characters", MessageBoxButton.OK, MessageBoxImage.Warning);
                node.Name = "PLEASE RENAME";
                _editingOriginalName = null;
                return;
            }

            if (_editingOriginalName != null && _editingOriginalName != node.Name)
            {
                RecordRename(node, _editingOriginalName, node.Name);
                node.Parent?.SortChildren();
            }
            _editingOriginalName = null;
        }
    }

    private void EditTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (sender is TextBox textBox && textBox.DataContext is FolderTreeNode node)
            {
                if (!FolderTreeNode.IsValidPrintableAscii(textBox.Text))
                {
                    MessageBox.Show(
                        "Only printable ASCII characters (letters, numbers, and standard symbols) are supported.",
                        "Invalid Characters", MessageBoxButton.OK, MessageBoxImage.Warning);
                    node.Name = "PLEASE RENAME";
                    _editingOriginalName = null;
                }
                node.IsEditing = false;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (sender is TextBox && ((TextBox)sender).DataContext is FolderTreeNode node)
            {
                var originalName = node.OriginalFullPath.Split('\\').Last();
                node.Name = originalName;
                node.IsEditing = false;
            }
            e.Handled = true;
        }
    }

    // --- Drag and drop ---

    private static TreeViewItem? FindTreeViewItem(DependencyObject? source)
    {
        while (source != null && source is not TreeViewItem)
            source = VisualTreeHelper.GetParent(source);
        return source as TreeViewItem;
    }

    private void TreeView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);

        var treeViewItem = FindTreeViewItem(e.OriginalSource as DependencyObject);
        _clickedNode = treeViewItem?.DataContext as FolderTreeNode;
    }

    private void TreeView_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && _draggedNode == null && _clickedNode != null)
        {
            if (_clickedNode.IsRootNode)
                return;

            Point currentPosition = e.GetPosition(null);
            Vector diff = _dragStartPoint - currentPosition;

            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                _draggedNode = _clickedNode;
                DragDrop.DoDragDrop(FolderTreeView, _clickedNode, DragDropEffects.Move);
                _draggedNode = null;
                _clickedNode = null;
            }
        }
    }

    private void TreeView_DragEnter(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(FolderTreeNode)))
            e.Effects = DragDropEffects.None;
    }

    private void TreeView_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(FolderTreeNode)))
        {
            e.Effects = DragDropEffects.Move;

            var targetElement = e.OriginalSource as FrameworkElement;
            var targetNode = targetElement?.DataContext as FolderTreeNode;

            if (targetNode != _currentDropTarget)
            {
                if (_currentDropTarget != null)
                    _currentDropTarget.IsDropTarget = false;

                _currentDropTarget = targetNode;
                if (_currentDropTarget != null)
                    _currentDropTarget.IsDropTarget = true;
            }
        }
        else
        {
            e.Effects = DragDropEffects.None;
            ClearDropTarget();
        }
        e.Handled = true;
    }

    private void ClearDropTarget()
    {
        if (_currentDropTarget != null)
        {
            _currentDropTarget.IsDropTarget = false;
            _currentDropTarget = null;
        }
    }

    private void TreeView_Drop(object sender, DragEventArgs e)
    {
        try
        {
            if (e.Data.GetDataPresent(typeof(FolderTreeNode)))
            {
                var droppedNode = e.Data.GetData(typeof(FolderTreeNode)) as FolderTreeNode;
                var targetElement = e.OriginalSource as FrameworkElement;
                var targetNode = targetElement?.DataContext as FolderTreeNode;

                if (droppedNode != null && targetNode != null && droppedNode != targetNode)
                {
                    if (droppedNode.IsRootNode)
                        return;

                    if (IsDescendant(targetNode, droppedNode))
                    {
                        MessageBox.Show("Cannot move a folder into its own subfolder.",
                            "Invalid Operation", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    RecordMove(droppedNode, droppedNode.Parent!, targetNode);

                    if (droppedNode.Parent != null)
                    {
                        droppedNode.Parent.Children.Remove(droppedNode);
                        droppedNode.Parent.RecalculateCounts();
                    }

                    droppedNode.Parent = targetNode;
                    targetNode.Children.Add(droppedNode);
                    targetNode.IsExpanded = true;

                    var node = targetNode;
                    while (node != null)
                    {
                        node.RecalculateCounts();
                        node = node.Parent;
                    }

                    droppedNode.UpdateFullPath();
                    targetNode.SortChildren();
                }
            }
        }
        finally
        {
            ClearDropTarget();
        }
    }

    private void TreeView_DragLeave(object sender, DragEventArgs e)
    {
        ClearDropTarget();
    }

    private static bool IsDescendant(FolderTreeNode potentialDescendant, FolderTreeNode ancestor)
    {
        var current = potentialDescendant;
        while (current != null)
        {
            if (current == ancestor)
                return true;
            current = current.Parent;
        }
        return false;
    }

    // --- Undo ---

    private void RecordMove(FolderTreeNode node, FolderTreeNode oldParent, FolderTreeNode newParent)
    {
        var oldIndex = oldParent.Children.IndexOf(node);

        PushUndo(new MoveOperation
        {
            Node = node,
            OldParent = oldParent,
            NewParent = newParent,
            OldIndex = oldIndex
        });
    }

    private void RecordRename(FolderTreeNode node, string oldName, string newName)
    {
        PushUndo(new RenameOperation
        {
            Node = node,
            OldName = oldName,
            NewName = newName
        });
    }

    private void PushUndo(UndoOperation operation)
    {
        if (_undoStack.Count >= MaxUndoOperations)
        {
            var temp = new Stack<UndoOperation>(_undoStack.Reverse().Skip(1).Reverse());
            _undoStack = temp;
        }

        _undoStack.Push(operation);
        CanUndo = true;
    }

    private void UndoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_undoStack.Count > 0)
        {
            var operation = _undoStack.Pop();
            operation.Undo();
            CanUndo = _undoStack.Count > 0;
        }
    }

    // --- Save / Cancel ---

    private void CollectMappings(FolderTreeNode node, Dictionary<string, string> mappings)
    {
        if (!node.IsRootNode)
        {
            if (node.OriginalFullPath != node.FullPath)
                mappings[node.OriginalFullPath] = node.FullPath;
        }

        foreach (var child in node.Children)
            CollectMappings(child, mappings);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        FolderMappings = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var root in RootNodes)
            CollectMappings(root, FolderMappings);

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
