using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OrbitalOrganizer.Core;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OrbitalOrganizer;

public partial class ManualUpdateDialog : Window
{
    public string LatestTag { get; private set; } = "";

    public ManualUpdateDialog()
    {
        InitializeComponent();
    }

    public ManualUpdateDialog(string latestTag, string latestVersion, ManualUpdateReason reason)
    {
        InitializeComponent();
        LatestTag = latestTag;

        if (reason == ManualUpdateReason.UnsupportedPlatform)
            ReasonText.Text = $"A new version of Orbital Organizer ({latestVersion}) is available. Auto-update is not supported on this platform.";
        else
            ReasonText.Text = $"A new version of Orbital Organizer ({latestVersion}) is available, but this release cannot be auto-updated.";

        KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
        };
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void SkipButton_Click(object? sender, RoutedEventArgs e)
    {
        UpdateAvailableDialog.SaveSkippedVersion(LatestTag);
        Close();
    }

    private void ReleasesLink_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var url = Constants.AppUrl + "/releases";
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                Process.Start("xdg-open", url);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                Process.Start("open", url);
        }
        catch { }
    }
}
