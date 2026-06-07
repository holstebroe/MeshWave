using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace MeshWave.Wpf.Converters
{
    /// <summary>
    /// Converts a file path string to a <see cref="BitmapImage"/> loaded with
    /// <see cref="BitmapCacheOption.OnLoad"/> so the source file is immediately
    /// released and cannot be locked when the image is later overwritten.
    /// </summary>
    public class PathToBitmapConverter : IValueConverter
    {
        public static readonly PathToBitmapConverter Instance = new();

        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string path || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;

            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
