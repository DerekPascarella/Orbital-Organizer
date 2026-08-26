using System.Windows;

namespace OrbitalOrganizer;

public partial class GameDbMigrationDialog : Window
{
    public bool Proceed { get; private set; }

    public GameDbMigrationDialog()
    {
        InitializeComponent();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Proceed = false;
        DialogResult = false;
    }

    private void ProceedButton_Click(object sender, RoutedEventArgs e)
    {
        Proceed = true;
        DialogResult = true;
    }
}
