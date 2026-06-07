using System.Globalization;
using System.Windows.Data;

namespace MeshWave.Wpf.Converters
{
    /// <summary>
    /// Inverts a boolean value. Used for two-way binding with RadioButtons and visibility toggles.
    /// </summary>
    [ValueConversion(typeof(bool), typeof(bool))]
    public class InverseBoolConverter : IValueConverter
    {
        public static readonly InverseBoolConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && !b;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && !b;
    }
}
