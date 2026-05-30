using System;
using System.IO;
using System.Text.Json;
using MeshWave.Models;

namespace MeshWave.Services
{
    /// <summary>
    /// Service for loading and saving application settings
    /// </summary>
    public class SettingsService
    {
        private static readonly string AppDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MeshWave");

        private static readonly string SettingsFilePath = Path.Combine(AppDataFolder, "settings.json");
        private static readonly string DefaultBaseFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            "MeshWave");

        private static AppSettings? _currentSettings;

        public AppSettings LoadSettings()
        {
            if (_currentSettings != null)
                return _currentSettings;

            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    var json = File.ReadAllText(SettingsFilePath);
                    _currentSettings = JsonSerializer.Deserialize<AppSettings>(json);
                }
            }
            catch
            {
                // If load fails, use defaults
            }

            _currentSettings ??= CreateDefaultSettings();

            if (_currentSettings.SupportedExtensions == null || _currentSettings.SupportedExtensions.Count == 0)
            {
                _currentSettings.SupportedExtensions = [".mp3", ".flac", ".wav", ".ogg", ".m4a"];
            }

            return _currentSettings;
        }

        public void SaveSettings(AppSettings settings)
        {
            try
            {
                // Ensure AppData folder exists
                if (!Directory.Exists(AppDataFolder))
                {
                    Directory.CreateDirectory(AppDataFolder);
                }

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };
                var json = JsonSerializer.Serialize(settings, options);
                File.WriteAllText(SettingsFilePath, json);

                _currentSettings = settings;
            }
            catch (Exception ex)
            {
                // TODO: Log error
                throw new InvalidOperationException("Failed to save settings", ex);
            }
        }

        public string GetMyMusicFolder()
        {
            var settings = LoadSettings();
            return Path.Combine(settings.BaseFolder, "My Music");
        }

        public string GetOtherMusicFolder()
        {
            var settings = LoadSettings();
            return Path.Combine(settings.BaseFolder, "Other Music");
        }

        public void EnsureFoldersExist()
        {
            var settings = LoadSettings();

            if (!Directory.Exists(settings.BaseFolder))
            {
                Directory.CreateDirectory(settings.BaseFolder);
            }

            var myMusic = GetMyMusicFolder();
            if (!Directory.Exists(myMusic))
            {
                Directory.CreateDirectory(myMusic);
            }

            var otherMusic = GetOtherMusicFolder();
            if (!Directory.Exists(otherMusic))
            {
                Directory.CreateDirectory(otherMusic);
            }
        }

        private AppSettings CreateDefaultSettings()
        {
            return new AppSettings
            {
                BaseFolder = DefaultBaseFolder,
                Theme = "Dark",
                AudioDevice = "Default",
                SupportedExtensions = [".mp3", ".flac", ".wav", ".ogg", ".m4a"],
                Playback = new PlaybackSettings
                {
                    Volume = 0.8,
                    RegisterPlayAt = 0.5
                }
            };
        }
    }
}
