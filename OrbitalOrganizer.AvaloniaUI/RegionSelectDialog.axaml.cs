using Avalonia.Controls;
using Avalonia.Interactivity;

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

        Closing += OnClosing;
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        int index = RegionComboBox.SelectedIndex;
        if (index >= 0 && index < Regions.Length)
            SelectedRegionCode = Regions[index].Code;

        _confirmed = true;
        Close();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_confirmed)
            e.Cancel = true;
    }
}
