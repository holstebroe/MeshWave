using System.Globalization;
using System.Windows.Data;

namespace MeshWave.Converters
{
    /// <summary>
    /// Returns the pixel width of the filled (played) portion of a Slider bar.
    /// Bindings: Value, Minimum, Maximum, ActualWidth.
    /// </summary>
    public class SliderFillWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 4)
                return 0.0;

            if (values[0] is not double value ||
                values[1] is not double minimum ||
                values[2] is not double maximum ||
                values[3] is not double totalWidth)
                return 0.0;

            var range = maximum - minimum;
            if (range <= 0 || totalWidth <= 0)
                return 0.0;

            var fraction = Math.Clamp((value - minimum) / range, 0.0, 1.0);
            return fraction * totalWidth;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
