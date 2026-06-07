using System.Globalization;
using System.Windows.Data;

namespace MeshWave.Wpf.Converters;

public class TimeSpanToSecondsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is TimeSpan ts)
            return ts.TotalSeconds;
        return 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d)
            return TimeSpan.FromSeconds(d);
        return TimeSpan.Zero;
    }
}