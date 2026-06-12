using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MeshWave.Wpf.Services;

public static class ColorExtractionService
{
    private static readonly Color DefaultAccentColor = Color.FromRgb(0x1D, 0xB9, 0x54); // #1DB954
    private static readonly Color DarkThemeBackground = Color.FromRgb(0x18, 0x18, 0x18); // #181818

    public static async System.Threading.Tasks.Task<Color> GetDominantColorAsync(string imagePath, Color? fallbackColor = null)
    {
        var fallback = fallbackColor ?? DefaultAccentColor;

        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return fallback;

        return await System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                var uri = new Uri(imagePath, UriKind.Absolute);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = uri;
                bmp.DecodePixelWidth = 1;
                bmp.DecodePixelHeight = 1;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze(); // Allow access from other threads

                // Normalize format to Bgra32
                var converted = new FormatConvertedBitmap();
                converted.BeginInit();
                converted.Source = bmp;
                converted.DestinationFormat = PixelFormats.Bgra32;
                converted.EndInit();
                converted.Freeze();

                var pixels = new byte[4];
                converted.CopyPixels(new System.Windows.Int32Rect(0, 0, 1, 1), pixels, 4, 0);

                // B G R A
                var extractedColor = Color.FromRgb(pixels[2], pixels[1], pixels[0]);

                return EnhanceColorForDarkTheme(extractedColor, fallback);
            }
            catch
            {
                return fallback;
            }
        });
    }

    private static Color EnhanceColorForDarkTheme(Color color, Color fallback)
    {
        // Simple heuristic to ensure the color is visible on a dark background (#181818).
        // If it's too dark or has low saturation, adjust or fallback.

        // Convert to HSL/HSV representation simply
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));

        double luminance = (max + min) / 2.0;

        // If the color is too dark, bump up the luminance
        if (luminance < 0.3)
        {
            // Increase lightness
            r = Math.Min(1.0, r + 0.3);
            g = Math.Min(1.0, g + 0.3);
            b = Math.Min(1.0, b + 0.3);
            color = Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
        }

        // If it's very close to black/grey (low saturation), we might prefer the fallback
        double delta = max - min;
        double saturation = luminance > 0.5 ? delta / (2.0 - max - min) : (luminance > 0 ? delta / (max + min) : 0);

        if (saturation < 0.15 && luminance < 0.6)
        {
            // Too grey, default green looks better
            return fallback;
        }

        return color;
    }
}
