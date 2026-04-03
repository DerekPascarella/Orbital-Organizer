using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using OrbitalOrganizer.Core;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;

namespace OrbitalOrganizer;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        VersionRun.Text = $" v{Constants.Version}";
        PreviewKeyDown += AboutWindow_PreviewKeyDown;
    }

    private void AboutWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }

    private void Hyperlink_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Constants.AppUrl) { UseShellExecute = true });
        }
        catch { }
    }

    private async void CheckForUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        var btn = (System.Windows.Controls.Button)sender;
        btn.IsEnabled = false;
        btn.Content = "Checking...";

        try
        {
            var result = await UpdateManager.CheckForUpdateAsync();
            if (result.ManualUpdateRequired)
            {
                var manualDialog = new ManualUpdateDialog(result.LatestTag, result.LatestVersion, result.ManualReason);
                manualDialog.Owner = this;
                manualDialog.ShowDialog();
            }
            else if (result.UpdateAvailable)
            {
                btn.Content = "Check for Updates";
                btn.IsEnabled = true;

                var dialog = new UpdateAvailableDialog(result.LatestTag, result.LatestVersion);
                dialog.Owner = this;
                dialog.ShowDialog();

                if (dialog.UserWantsUpdate)
                {
                    var parentWindow = this.Owner as Window;
                    Close();
                    var wizard = new UpdateWizardWindow(result.LatestTag, result.LatestVersion);
                    if (parentWindow != null)
                    {
                        wizard.Owner = parentWindow;
                        wizard.ShowDialog();
                    }
                    else
                    {
                        wizard.Show();
                    }
                }
            }
            else
            {
                MessageBox.Show(
                    "You are running the latest version.",
                    "No Update Available",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch
        {
            MessageBox.Show(
                "Could not check for updates. Please check your internet connection.",
                "Update Check Failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            btn.IsEnabled = true;
            btn.Content = "Check for Updates";
        }
    }
}
