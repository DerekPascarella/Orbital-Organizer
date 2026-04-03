using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace OrbitalOrganizer;

public partial class RegionSelectDialog : Window
{
    private static readonly (string Code, string Label)[] Regions =
    {
        ("J", "Japan"),
        ("T", "Taiwan and Philippines"),
        ("U", "USA and Canada"),
        ("B", "Brazil"),
        ("K", "Korea"),
        ("A", "Asia (PAL Area)"),
        ("E", "Europe"),
        ("L", "Latin America"),
    };

    private bool _confirmed;

    public string SelectedRegionCode { get; private set; } = "J";

    public RegionSelectDialog()
    {
        InitializeComponent();

        foreach (var (_, label) in Regions)
            RegionComboBox.Items.Add(label);

        RegionComboBox.SelectedIndex = 0;

        SourceInitialized += OnSourceInitialized;
        Closing += OnClosing;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        int index = RegionComboBox.SelectedIndex;
        if (index >= 0 && index < Regions.Length)
            SelectedRegionCode = Regions[index].Code;

        _confirmed = true;
        Close();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_confirmed)
            e.Cancel = true;
    }

    // Remove the close button from the title bar.
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int style = GetWindowLong(hwnd, GWL_STYLE);
        SetWindowLong(hwnd, GWL_STYLE, style & ~WS_SYSMENU);
    }

    private const int GWL_STYLE = -16;
    private const int WS_SYSMENU = 0x80000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
