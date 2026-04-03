using Avalonia.Controls;
using Avalonia.Interactivity;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using MsBoxIcon = MsBox.Avalonia.Enums.Icon;
using OrbitalOrganizer.Core.Models;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace OrbitalOrganizer;

public class AltFolderEntry : INotifyPropertyChanged
{
    private string _folderPath = string.Empty;
    public string FolderPath
    {
        get => _folderPath;
        set
        {
            if (_folderPath != value)
            {
                _folderPath = value;
                OnPropertyChanged();
            }
        }
    }

    private string _label = "Folder Path 1:";
    public string Label
    {
        get => _label;
        set
        {
            if (_label != value)
            {
                _label = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public partial class AssignAltFoldersWindow : Window
{
    private readonly string _primaryFolder;
    private readonly ObservableCollection<AltFolderEntry> _altFolders = new();

    public bool UserConfirmed { get; private set; }

    public AssignAltFoldersWindow()
    {
        InitializeComponent();
        _primaryFolder = "";
    }

    public AssignAltFoldersWindow(SaturnGame item, IEnumerable<string> knownFolders) : this()
    {
        _primaryFolder = item.Folder;

        HeaderLabel.Text = "Assign additional folder paths for selected item";

        for (int i = 0; i < item.AlternativeFolders.Count; i++)
        {
            _altFolders.Add(new AltFolderEntry
            {
                FolderPath = item.AlternativeFolders[i],
                Label = $"Folder Path {i + 1}:"
            });
        }

        AltFoldersList.ItemsSource = _altFolders;
        UpdateAddButton();
    }

    public List<string> GetAltFolders()
    {
        return _altFolders
            .Select(e => e.FolderPath?.Trim() ?? string.Empty)
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();
    }

    private void AddButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_altFolders.Count >= 5) return;
        _altFolders.Add(new AltFolderEntry
        {
            Label = $"Folder Path {_altFolders.Count + 1}:"
        });
        UpdateAddButton();
    }

    private void RemoveButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is AltFolderEntry entry)
        {
            _altFolders.Remove(entry);
            ReindexEntries();
            UpdateAddButton();
        }
    }

    private async void FolderPath_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox && textBox.DataContext is AltFolderEntry entry)
        {
            var path = entry.FolderPath?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(path)) return;

            bool isDuplicate = false;

            if (!string.IsNullOrEmpty(_primaryFolder) && path == _primaryFolder)
                isDuplicate = true;

            if (!isDuplicate)
            {
                foreach (var other in _altFolders)
                {
                    if (other != entry && (other.FolderPath?.Trim() ?? string.Empty) == path)
                    {
                        isDuplicate = true;
                        break;
                    }
                }
            }

            if (isDuplicate)
            {
                var msgBox = MessageBoxManager.GetMessageBoxStandard(
                    "Duplicate Folder Path",
                    "This folder path is already assigned to this disc image.",
                    ButtonEnum.Ok, MsBoxIcon.Info);
                await msgBox.ShowWindowDialogAsync(this);
                entry.FolderPath = string.Empty;
            }
        }
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        UserConfirmed = true;
        Close();
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        UserConfirmed = false;
        Close();
    }

    private void ReindexEntries()
    {
        for (int i = 0; i < _altFolders.Count; i++)
            _altFolders[i].Label = $"Folder Path {i + 1}:";
    }

    private void UpdateAddButton()
    {
        AddButton.IsEnabled = _altFolders.Count < 5;
    }
}
