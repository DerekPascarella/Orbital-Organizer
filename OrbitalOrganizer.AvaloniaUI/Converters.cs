using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace OrbitalOrganizer;

public static class Converters
{
    public static readonly IValueConverter ByteSizeConverter = new ByteSizeToStringConverter();
    public static readonly IValueConverter DropTargetBackground = new DropTargetBrushConverter("#ADD8E6");
    public static readonly IValueConverter DropTargetBorderBrush = new DropTargetBrushConverter("#4682B4");
}

public class ByteSizeToStringConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is long bytes && bytes >= 0)
        {
            var size = ByteSizeLib.ByteSize.FromBytes(bytes);
            if (size.GigaBytes >= 1)
                return $"{size.GigaBytes:F1} GB";
            if (size.MegaBytes >= 1)
                return $"{size.MegaBytes:F0} MB";
            return $"{size.KiloBytes:F0} KB";
        }
        return string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public class DropTargetBrushConverter : IValueConverter
{
    private readonly IBrush _highlight;

    public DropTargetBrushConverter(string highlightColor)
    {
        _highlight = new SolidColorBrush(Color.Parse(highlightColor));
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? _highlight : Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
