using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace OrbitalOrganizer;

public partial class MetadataScanDialog : Window
{
    private TextBlock GameCountText = null!;

    public bool StartScan { get; private set; }

    public MetadataScanDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        GameCountText = this.FindControl<TextBlock>("GameCountText")!;
    }

    public MetadataScanDialog(int gameCount) : this()
    {
        GameCountText.Text = gameCount.ToString();
    }

    private void QuitButton_Click(object? sender, RoutedEventArgs e)
    {
        StartScan = false;
        Close();
    }

    private void StartScanButton_Click(object? sender, RoutedEventArgs e)
    {
        StartScan = true;
        Close();
    }
}
