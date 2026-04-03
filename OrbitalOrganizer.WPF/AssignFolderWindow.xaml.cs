using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace OrbitalOrganizer;

public partial class AssignFolderWindow : Window, INotifyPropertyChanged
{
    private string _folderPath = string.Empty;
    private string _selectionInfo = string.Empty;

    public string FolderPath
    {
        get => _folderPath;
        set { _folderPath = value; OnPropertyChanged(); }
    }

    public string SelectionInfo
    {
        get => _selectionInfo;
        set { _selectionInfo = value; OnPropertyChanged(); }
    }

    public IEnumerable<string> KnownFolders { get; set; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    public AssignFolderWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    public AssignFolderWindow(int selectedCount, IEnumerable<string> knownFolders) : this()
    {
        SelectionInfo = $"Assign folder path to {selectedCount} selected {(selectedCount == 1 ? "item" : "items")}";
        KnownFolders = knownFolders;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
