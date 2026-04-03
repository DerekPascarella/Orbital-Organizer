using System.Globalization;
using Avalonia.Data.Converters;

namespace OrbitalOrganizer;

public static class Converters
{
    public static readonly IValueConverter ByteSizeConverter = new ByteSizeToStringConverter();
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
