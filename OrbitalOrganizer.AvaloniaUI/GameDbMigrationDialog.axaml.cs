using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace OrbitalOrganizer;

public partial class GameDbMigrationDialog : Window
{
    public bool Proceed { get; private set; }

    public GameDbMigrationDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Proceed = false;
        Close();
    }

    private void ProceedButton_Click(object? sender, RoutedEventArgs e)
    {
        Proceed = true;
        Close();
    }
}
