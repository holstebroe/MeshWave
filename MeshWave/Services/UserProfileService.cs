using System;
using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MeshWave.Models;

namespace MeshWave.Services
{
    public class UserProfileService
    {
        private static readonly string AppDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MeshWave");

        private static readonly string ProfileFilePath = Path.Combine(AppDataFolder, "profile.json");

        public UserProfile LoadProfile()
        {
            try
            {
                if (File.Exists(ProfileFilePath))
                {
                    var json = File.ReadAllText(ProfileFilePath);
                    var profile = JsonSerializer.Deserialize<UserProfile>(json);
                    if (profile != null)
                    {
                        return profile;
                    }
                }
            }
            catch
            {
                // fall through to defaults
            }

            return new UserProfile();
        }

        public void SaveProfile(UserProfile profile)
        {
            if (!Directory.Exists(AppDataFolder))
            {
                Directory.CreateDirectory(AppDataFolder);
            }

            if (!string.IsNullOrWhiteSpace(profile.AvatarImagePath) && File.Exists(profile.AvatarImagePath))
            {
                profile.AvatarIconPath = GenerateRoundedIcon(profile.AvatarImagePath);
            }
            else
            {
                profile.AvatarIconPath = string.Empty;
            }

            var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ProfileFilePath, json);
        }

        private string GenerateRoundedIcon(string sourceImagePath)
        {
            var iconPath = Path.Combine(AppDataFolder, "user_icon.png");

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(sourceImagePath, UriKind.Absolute);
            bitmap.DecodePixelWidth = 64;
            bitmap.DecodePixelHeight = 64;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            var visual = new DrawingVisual();
            using (var ctx = visual.RenderOpen())
            {
                var rect = new System.Windows.Rect(0, 0, 64, 64);
                var clip = new RectangleGeometry(rect, 12, 12);
                ctx.PushClip(clip);
                ctx.DrawImage(bitmap, rect);
                ctx.Pop();
            }

            var rtb = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));

            using var stream = File.Create(iconPath);
            encoder.Save(stream);

            return iconPath;
        }
    }
}
