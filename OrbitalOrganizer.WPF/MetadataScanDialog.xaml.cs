using System.Windows;

namespace OrbitalOrganizer;

public partial class MetadataScanDialog : Window
{
    public int GameCount { get; set; }
    public bool StartScan { get; private set; }

    public MetadataScanDialog(int gameCount)
    {
        InitializeComponent();
        GameCount = gameCount;
        DataContext = this;
    }

    private void QuitButton_Click(object sender, RoutedEventArgs e)
    {
        StartScan = false;
        DialogResult = false;
    }

    private void StartScanButton_Click(object sender, RoutedEventArgs e)
    {
        StartScan = true;
        DialogResult = true;
    }
}
