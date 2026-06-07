using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace MeshWave.Wpf.Converters
{
    public class UserIconConverter : IValueConverter
    {
        private static readonly BitmapImage DefaultAvatar;

        static UserIconConverter()
        {
            DefaultAvatar = new BitmapImage();
            DefaultAvatar.BeginInit();
            DefaultAvatar.UriSource = new Uri("pack://application:,,,/Assets/MeshWaveIcon128.png");
            DefaultAvatar.CacheOption = BitmapCacheOption.OnLoad;
            DefaultAvatar.EndInit();
            DefaultAvatar.Freeze();
        }

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            string? path = value as string;

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return DefaultAvatar;
            }

            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return DefaultAvatar;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
