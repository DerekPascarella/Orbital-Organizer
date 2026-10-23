using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using OrbitalOrganizer.Core.Models;

namespace OrbitalOrganizer;

public partial class DiscImageOptionsWindow : Window
{
    private readonly RadioButton _cdiRadioButton;

    public DiscConversionFormat ConversionFormat { get; private set; }
    public bool Saved { get; private set; }

    public DiscImageOptionsWindow(DiscConversionFormat conversionFormat)
    {
        AvaloniaXamlLoader.Load(this);
        _cdiRadioButton = this.FindControl<RadioButton>("CdiRadioButton")!;
        ConversionFormat = conversionFormat;
        _cdiRadioButton.IsChecked = conversionFormat == DiscConversionFormat.Cdi;
        this.FindControl<RadioButton>("CcdRadioButton")!.IsChecked = !_cdiRadioButton.IsChecked;
    }

    private void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        ConversionFormat = _cdiRadioButton.IsChecked == true ? DiscConversionFormat.Cdi : DiscConversionFormat.Ccd;
        Saved = true;
        Close();
    }
}
