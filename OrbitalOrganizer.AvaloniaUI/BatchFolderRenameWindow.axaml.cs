using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MsBoxIcon = MsBox.Avalonia.Enums.Icon;

namespace OrbitalOrganizer;

public partial class BatchFolderRenameWindow : Window, INotifyPropertyChanged
{
    private TreeView FolderTreeView = null!;

    // In-process format so the node itself reaches the drop side without ever
    // going near the system clipboard.
    private static readonly DataFormat<FolderTreeNode> NodeFormat =
        DataFormat.CreateInProcessFormat<FolderTreeNode>("oo-folder-tree-node");
    // macOS drops the in-process format from the drag before it hands it to the OS
    // and then rejects a drag that carries nothing, so this small marker rides along.
    private static readonly DataFormat<byte[]> NodeMarkerFormat =
        DataFormat.CreateBytesApplicationFormat("oo-folder-tree-node-marker");

    private Point _dragStartPoint;
    // DoDragDropAsync wants the press event that started things, so it is held
    // until the pointer has moved far enough to count as a drag.
    private PointerPressedEventArgs? _dragTriggerEvent;
    private FolderTreeNode? _draggedNode;
    private FolderTreeNode? _clickedNode;
    private FolderTreeNode? _currentDropTarget;

    // Scrolls the tree a line per tick while a folder drag hovers near its top or
    // bottom edge. The tick and the drop reuse the last DragOver position because
    // neither has a reliable pointer position of its own.
    private readonly DispatcherTimer _dragScrollTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(75)
    };
    private Point _dragScrollPosition;

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

    public bool UserConfirmed { get; private set; }

    public BatchFolderRenameWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        FolderTreeView = this.FindControl<TreeView>("FolderTreeView")!;
    }

    public BatchFolderRenameWindow(Dictionary<string, int> folderCounts, int totalItemCount) : this()
    {
        BuildTree(folderCounts, totalItemCount);

        FolderTreeView.AddHandler(DragDrop.DragEnterEvent, Tree_DragOver);
        FolderTreeView.AddHandler(DragDrop.DragOverEvent, Tree_DragOver);
        FolderTreeView.AddHandler(DragDrop.DropEvent, Tree_Drop);
        FolderTreeView.AddHandler(DragDrop.DragLeaveEvent, Tree_DragLeave);
        _dragScrollTimer.Tick += DragScrollTimer_Tick;
        Closed += (_, _) => _dragScrollTimer.Stop();

        // Tunnel strategy so these fire before TreeViewItem handles the pointer for selection
        FolderTreeView.AddHandler(PointerPressedEvent, Tree_PointerPressed, RoutingStrategies.Tunnel);
        FolderTreeView.AddHandler(PointerMovedEvent, Tree_PointerMoved, RoutingStrategies.Tunnel);

        FolderTreeView.DoubleTapped += Tree_DoubleTapped;

        // KeyDown instead of KeyUp so the rename editor's handled Escape doesn't also close the window
        KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
        };
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

    private void Tree_DoubleTapped(object? sender, TappedEventArgs e)
    {
        var node = (e.Source as Control)?.DataContext as FolderTreeNode;
        if (node == null || node.IsRootNode)
            return;

        _editingOriginalName = node.Name;
        node.IsEditing = true;
        e.Handled = true;
    }

    private void EditTextBox_AttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.Focus();
            textBox.SelectAll();
        }
    }

    private async void CommitRename(FolderTreeNode node, string? rawText = null)
    {
        node.IsEditing = false;

        if (!FolderTreeNode.IsValidPrintableAscii(rawText ?? node.Name))
        {
            var msgBox = MessageBoxManager.GetMessageBoxStandard("Error",
                "Only printable ASCII characters (letters, numbers, and standard symbols) are supported.",
                ButtonEnum.Ok, MsBoxIcon.None, windowStartupLocation: WindowStartupLocation.CenterOwner);
            await msgBox.ShowWindowDialogAsync(this);
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

    private void EditTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.DataContext is not FolderTreeNode node)
            return;

        if (e.Key == Key.Enter)
        {
            CommitRename(node, textBox.Text);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            var originalName = node.OriginalFullPath.Split('\\').Last();
            node.Name = originalName;
            CommitRename(node);
            e.Handled = true;
        }
    }

    private void EditTextBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox && textBox.DataContext is FolderTreeNode node && node.IsEditing)
            CommitRename(node);
    }

    // --- Drag and drop ---

    private void Tree_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(FolderTreeView).Properties.IsLeftButtonPressed)
        {
            _clickedNode = null;
            _dragTriggerEvent = null;
            return;
        }

        _dragStartPoint = e.GetPosition(this);

        var node = (e.Source as Control)?.DataContext as FolderTreeNode;

        // Clicks inside an active rename editor should not start a drag
        if (node != null && node.IsEditing)
            node = null;

        _clickedNode = node;
        _dragTriggerEvent = node != null ? e : null;
    }

    private async void Tree_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_clickedNode == null || _draggedNode != null || _dragTriggerEvent == null)
            return;

        if (_clickedNode.IsRootNode)
            return;

        if (!e.GetCurrentPoint(FolderTreeView).Properties.IsLeftButtonPressed)
            return;

        var currentPosition = e.GetPosition(this);
        if (Math.Abs(currentPosition.X - _dragStartPoint.X) < 4 &&
            Math.Abs(currentPosition.Y - _dragStartPoint.Y) < 4)
            return;

        _draggedNode = _clickedNode;

        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(NodeFormat, _draggedNode));
        data.Add(DataTransferItem.Create(NodeMarkerFormat, new byte[] { 1 }));

        try
        {
            await DragDrop.DoDragDropAsync(_dragTriggerEvent, data, DragDropEffects.Move);
        }
        catch (Exception)
        {
            // A failed platform drag just cancels the move
        }

        _dragScrollTimer.Stop();
        _draggedNode = null;
        _clickedNode = null;
        _dragTriggerEvent = null;
        ClearDropTarget();
    }

    private void Tree_DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = _draggedNode != null ? DragDropEffects.Move : DragDropEffects.None;

        _dragScrollPosition = e.GetPosition(this);
        SetDropTarget(NodeAt(_dragScrollPosition));

        if (GetDragScrollDirection() != 0)
            _dragScrollTimer.Start();
        else
            _dragScrollTimer.Stop();

        e.Handled = true;
    }

    private void SetDropTarget(FolderTreeNode? targetNode)
    {
        if (targetNode != _currentDropTarget)
        {
            if (_currentDropTarget != null)
                _currentDropTarget.IsDropTarget = false;

            _currentDropTarget = targetNode;
            if (_currentDropTarget != null)
                _currentDropTarget.IsDropTarget = true;
        }
    }

    private void Tree_DragLeave(object? sender, RoutedEventArgs e)
    {
        _dragScrollTimer.Stop();
        ClearDropTarget();
    }

    private int GetDragScrollDirection()
    {
        if (_draggedNode == null || !IsVisible || !FolderTreeView.IsVisible)
            return 0;

        var scrollViewer = FolderTreeView.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (scrollViewer == null)
            return 0;

        var point = this.TranslatePoint(_dragScrollPosition, scrollViewer);
        if (point == null || !new Rect(scrollViewer.Bounds.Size).Contains(point.Value))
            return 0;

        double edgeHeight = Math.Min(32, scrollViewer.Bounds.Height / 2);
        if (point.Value.Y < edgeHeight)
            return -1;
        if (point.Value.Y >= scrollViewer.Bounds.Height - edgeHeight)
            return 1;
        return 0;
    }

    private void DragScrollTimer_Tick(object? sender, EventArgs e)
    {
        int direction = GetDragScrollDirection();
        var scrollViewer = FolderTreeView.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (direction == 0 || scrollViewer == null ||
            (direction < 0 && scrollViewer.Offset.Y <= 0) ||
            (direction > 0 && scrollViewer.Offset.Y >= scrollViewer.ScrollBarMaximum.Y))
        {
            _dragScrollTimer.Stop();
            return;
        }

        if (direction < 0)
            scrollViewer.LineUp();
        else
            scrollViewer.LineDown();

        FolderTreeView.UpdateLayout();
        SetDropTarget(NodeAt(_dragScrollPosition));
    }

    // A drag event's Source and InputHitTest come from the last rendered frame, which
    // can predate a timer scroll, so the node comes from layout. Nested items follow
    // their parents, so the last match is the deepest.
    private FolderTreeNode? NodeAt(Point point)
    {
        FolderTreeNode? target = null;
        foreach (var item in FolderTreeView.GetVisualDescendants().OfType<TreeViewItem>())
        {
            var origin = item.TranslatePoint(new Point(0, 0), this);
            if (item.IsEffectivelyVisible && origin != null &&
                new Rect(origin.Value, item.Bounds.Size).Contains(point))
                target = item.DataContext as FolderTreeNode;
        }
        return target;
    }

    private async void Tree_Drop(object? sender, DragEventArgs e)
    {
        _dragScrollTimer.Stop();
        try
        {
            var droppedNode = _draggedNode;
            var targetNode = NodeAt(_dragScrollPosition);

            if (droppedNode == null || targetNode == null || droppedNode == targetNode)
                return;

            if (droppedNode.IsRootNode)
                return;

            if (IsDescendant(targetNode, droppedNode))
            {
                var msgBox = MessageBoxManager.GetMessageBoxStandard("Error",
                    "Cannot move a folder into its own subfolder.",
                    ButtonEnum.Ok, MsBoxIcon.None, windowStartupLocation: WindowStartupLocation.CenterOwner);
                await msgBox.ShowWindowDialogAsync(this);
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
        finally
        {
            ClearDropTarget();
        }
    }

    private void ClearDropTarget()
    {
        if (_currentDropTarget != null)
        {
            _currentDropTarget.IsDropTarget = false;
            _currentDropTarget = null;
        }
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

    private void UndoButton_Click(object? sender, RoutedEventArgs e)
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

    private void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        FolderMappings = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var root in RootNodes)
            CollectMappings(root, FolderMappings);

        UserConfirmed = true;
        Close();
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        UserConfirmed = false;
        Close();
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
