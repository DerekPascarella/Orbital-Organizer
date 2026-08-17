using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using OrbitalOrganizer.Core.Models;

namespace OrbitalOrganizer;

public partial class ArchiveAddModeDialog : Window
{
    private TextBlock CountText = null!;

    public ArchiveAddMode Result { get; private set; } = ArchiveAddMode.Cancel;

    public ArchiveAddModeDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        CountText = this.FindControl<TextBlock>("CountText")!;
    }

    public ArchiveAddModeDialog(int archiveCount) : this()
    {
        CountText.Text = $"You are adding {archiveCount} compressed archives.";
    }

    private void ReadNowButton_Click(object? sender, RoutedEventArgs e)
    {
        Result = ArchiveAddMode.ParseNow;
        Close();
    }

    private void DeferToSaveButton_Click(object? sender, RoutedEventArgs e)
    {
        Result = ArchiveAddMode.DeferToSave;
        Close();
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Result = ArchiveAddMode.Cancel;
        Close();
    }
}
