using Avalonia.Controls;
using Avalonia.Interactivity;

namespace OrbitalOrganizer;

public partial class MetadataScanDialog : Window
{
    public bool StartScan { get; private set; }

    public MetadataScanDialog()
    {
        InitializeComponent();
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
