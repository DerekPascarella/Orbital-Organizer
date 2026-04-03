using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Collections.Generic;
using System.Linq;

namespace OrbitalOrganizer;

public partial class LockedFilesDialog : Window
{
    public class LockedFileInfo
    {
        public string Path { get; set; } = "";
        public string Error { get; set; } = "";
    }

    public bool UserWantsRetry { get; private set; }

    public LockedFilesDialog()
    {
        InitializeComponent();
    }

    public LockedFilesDialog(Dictionary<string, string> lockedPaths) : this()
    {
        var items = lockedPaths.Select(kvp => new LockedFileInfo
        {
            Path = kvp.Key,
            Error = kvp.Value
        }).ToList();

        FileListBox.ItemsSource = items;
    }

    private void RetryButton_Click(object? sender, RoutedEventArgs e)
    {
        UserWantsRetry = true;
        Close();
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        UserWantsRetry = false;
        Close();
    }
}
