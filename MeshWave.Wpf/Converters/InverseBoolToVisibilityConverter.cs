using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MeshWave.Wpf.Converters;

/// <summary>
/// Returns Visibility.Visible when the bound bool is false, Collapsed when true.
/// The opposite of the built-in BooleanToVisibilityConverter.
/// </summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public static readonly InverseBoolToVisibilityConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is Visibility.Collapsed;
    }
}