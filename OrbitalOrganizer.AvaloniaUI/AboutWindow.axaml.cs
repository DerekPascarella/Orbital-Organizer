using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using MsBoxIcon = MsBox.Avalonia.Enums.Icon;
using OrbitalOrganizer.Core;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OrbitalOrganizer;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        VersionRun.Text = $" v{Constants.Version}";
        KeyDown += AboutWindow_KeyDown;
    }

    private void AboutWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }

    private void LinkText_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        try
        {
            var url = Constants.AppUrl;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                Process.Start("xdg-open", url);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                Process.Start("open", url);
        }
        catch { }
    }

    private async void CheckForUpdatesButton_Click(object? sender, RoutedEventArgs e)
    {
        var btn = (Button)sender!;
        btn.IsEnabled = false;
        btn.Content = "Checking...";

        try
        {
            var result = await UpdateManager.CheckForUpdateAsync();
            if (result.ManualUpdateRequired)
            {
                var manualDialog = new ManualUpdateDialog(result.LatestTag, result.LatestVersion, result.ManualReason);
                await manualDialog.ShowDialog(this);
            }
            else if (result.UpdateAvailable)
            {
                btn.Content = "Check for Updates";
                btn.IsEnabled = true;

                var dialog = new UpdateAvailableDialog(result.LatestTag, result.LatestVersion);
                await dialog.ShowDialog(this);

                if (dialog.UserWantsUpdate)
                {
                    var parentWindow = this.Owner as Window;
                    Close();
                    var wizard = new UpdateWizardWindow(result.LatestTag, result.LatestVersion);
                    if (parentWindow != null)
                        await wizard.ShowDialog(parentWindow);
                    else
                        wizard.Show();
                }
            }
            else
            {
                var msgBox = MessageBoxManager.GetMessageBoxStandard(
                    "No Update Available",
                    "You are running the latest version.",
                    ButtonEnum.Ok, MsBoxIcon.Info);
                await msgBox.ShowWindowDialogAsync(this);
            }
        }
        catch
        {
            var msgBox = MessageBoxManager.GetMessageBoxStandard(
                "Update Check Failed",
                "Could not check for updates. Please check your internet connection.",
                ButtonEnum.Ok, MsBoxIcon.Warning);
            await msgBox.ShowWindowDialogAsync(this);
        }
        finally
        {
            btn.IsEnabled = true;
            btn.Content = "Check for Updates";
        }
    }
}
