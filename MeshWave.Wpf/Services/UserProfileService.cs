using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MeshWave.Common.Core;
using MeshWave.Wpf.Models;
using Microsoft.Win32;

namespace MeshWave.Wpf.Services
{
    public class UserProfileService
    {
        private readonly string _appDataRoot;

        public UserProfileService(string? appDataRoot = null)
        {
            _appDataRoot = appDataRoot ?? MeshWaveEnvironment.GetAppDataRoot();
        }

        public UserProfile LoadProfile()
        {
            var profileFilePath = GetProfileFilePath();

            try
            {
                if (File.Exists(profileFilePath))
                {
                    var json = File.ReadAllText(profileFilePath);
                    var profile = JsonSerializer.Deserialize<UserProfile>(json);
                    if (profile != null)
                    {
                        ApplyLaunchOverrides(profile);
                        return profile;
                    }
                }
            }
            catch
            {
                // fall through to defaults
            }

            var installerUsername = GetInstallerDefaultString("Username");
            var profileFromDefaults = new UserProfile
            {
                DisplayName = string.IsNullOrWhiteSpace(installerUsername) ? "You" : installerUsername
            };

            ApplyLaunchOverrides(profileFromDefaults);
            return profileFromDefaults;
        }

        public void SaveProfile(UserProfile profile)
        {
            var appDataFolder = _appDataRoot;
            var profileFilePath = GetProfileFilePath();

            if (!Directory.Exists(appDataFolder))
            {
                Directory.CreateDirectory(appDataFolder);
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
            File.WriteAllText(profileFilePath, json);
        }

        private static string GetInstallerDefaultString(string valueName)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\MeshWave\Installer");
                return key?.GetValue(valueName)?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private string GenerateRoundedIcon(string sourceImagePath)
        {
            var appDataFolder = _appDataRoot;
            // Use a unique filename to avoid locking issues (WPF might still hold the old file).
            var timestamp = DateTime.Now.Ticks;
            var iconPath = Path.Combine(appDataFolder, $"user_icon_{timestamp}.png");

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
                // Request high quality scaling
                RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);

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

            using (var stream = File.Create(iconPath))
                encoder.Save(stream);

            // Cleanup old icons
            try
            {
                foreach (var file in Directory.EnumerateFiles(appDataFolder, "user_icon_*.png"))
                {
                    if (Path.GetFileName(file) != Path.GetFileName(iconPath))
                    {
                        File.Delete(file);
                    }
                }
            }
            catch { /* best effort cleanup */ }

            return iconPath;
        }

        private string GetProfileFilePath() => Path.Combine(_appDataRoot, "profile.json");

        private static void ApplyLaunchOverrides(UserProfile profile)
        {
            var displayNameOverride = Environment.GetEnvironmentVariable(MeshWaveEnvironment.DisplayNameEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(displayNameOverride))
            {
                profile.DisplayName = displayNameOverride.Trim();
            }
        }
    }
}
