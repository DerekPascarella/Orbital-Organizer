using System.Globalization;
using System.Windows.Data;
using ByteSizeLib;

namespace OrbitalOrganizer.Converter;

public class ByteSizeToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is long bytes)
            return bytes < 0 ? string.Empty : ByteSize.FromBytes(bytes).ToString("0.##");
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
