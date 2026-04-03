using System.Globalization;
using System.Windows.Data;

namespace OrbitalOrganizer.Converter;

public class FolderNumberConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int num && num > 0)
            return num < 100 ? num.ToString("D2") : num.ToString();
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
