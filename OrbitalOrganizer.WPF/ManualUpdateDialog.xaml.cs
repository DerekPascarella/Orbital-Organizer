using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;
using OrbitalOrganizer.Core;

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

        PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
        };
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void SkipButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateAvailableDialog.SaveSkippedVersion(LatestTag);
        Close();
    }

    private void ReleasesLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch { }
        e.Handled = true;
    }
}
