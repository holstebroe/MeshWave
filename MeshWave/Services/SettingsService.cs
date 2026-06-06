using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using MeshWave.Common.Core;
using MeshWave.Common.Core.Models;
using MeshWave.Models;

namespace MeshWave.Services
{
    /// <summary>
    /// Service for loading and saving application settings
    /// </summary>
    public class SettingsService
    {
        private static readonly string DefaultBaseFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            "MeshWave");

        private AppSettings? _currentSettings;
        private readonly string _appDataRoot;

        public SettingsService(string? appDataRoot = null)
        {
            _appDataRoot = appDataRoot ?? MeshWaveEnvironment.GetAppDataRoot();
        }

        public AppSettings LoadSettings()
        {
            if (_currentSettings != null)
                return _currentSettings;

            var settingsFilePath = GetSettingsFilePath();

            try
            {
                if (File.Exists(settingsFilePath))
                {
                    var json = File.ReadAllText(settingsFilePath);
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

            _currentSettings.P2P ??= new P2PSettings();
            _currentSettings.Playback ??= new PlaybackSettings();
            _currentSettings.Playback.ResumeState ??= new PlaybackResumeState();
            _currentSettings.Storage ??= new StorageSettings();
            _currentSettings.Logging ??= new LoggingSettings();

            if (string.IsNullOrWhiteSpace(_currentSettings.BaseFolder))
            {
                _currentSettings.BaseFolder = DefaultBaseFolder;
            }

            ApplyLaunchOverrides(_currentSettings);
            return _currentSettings;
        }

        public void SaveSettings(AppSettings settings)
        {
            try
            {
                var appDataFolder = _appDataRoot;
                var settingsFilePath = GetSettingsFilePath();

                if (!Directory.Exists(appDataFolder))
                {
                    Directory.CreateDirectory(appDataFolder);
                }

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };
                var json = JsonSerializer.Serialize(settings, options);
                File.WriteAllText(settingsFilePath, json);

                _currentSettings = settings;
            }
            catch (Exception ex)
            {
                // TODO: Log error
                throw new InvalidOperationException("Failed to save settings", ex);
            }
        }

        public string GetLocalMusicFolder()
        {
            var settings = LoadSettings();
            return Path.Combine(settings.BaseFolder, "Local Music");
        }

        public string GetPeerMusicFolder()
        {
            var settings = LoadSettings();
            return Path.Combine(settings.BaseFolder, "Peer Music");
        }

        public void EnsureFoldersExist()
        {
            var settings = LoadSettings();

            if (!Directory.Exists(settings.BaseFolder))
            {
                Directory.CreateDirectory(settings.BaseFolder);
            }

            var localMusic = GetLocalMusicFolder();
            if (!Directory.Exists(localMusic))
            {
                Directory.CreateDirectory(localMusic);
            }

            var peerMusic = GetPeerMusicFolder();
            if (!Directory.Exists(peerMusic))
            {
                Directory.CreateDirectory(peerMusic);
            }
        }

        private AppSettings CreateDefaultSettings()
        {
            var installerBaseFolder = GetInstallerDefaultString("BaseFolder");

            return new AppSettings
            {
                BaseFolder = string.IsNullOrWhiteSpace(installerBaseFolder) ? DefaultBaseFolder : installerBaseFolder,
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

        private string GetSettingsFilePath() => Path.Combine(_appDataRoot, "settings.json");

        private static void ApplyLaunchOverrides(AppSettings settings)
        {
            var baseFolderOverride = Environment.GetEnvironmentVariable(MeshWaveEnvironment.BaseFolderEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(baseFolderOverride))
                settings.BaseFolder = Path.GetFullPath(baseFolderOverride);

            if (TryGetBooleanOverride(MeshWaveEnvironment.P2PEnabledEnvironmentVariable, out var p2pEnabled))
                settings.P2P.Enabled = p2pEnabled;

            if (TryGetPositiveIntOverride(MeshWaveEnvironment.P2PPortEnvironmentVariable, out var p2pPort))
                settings.P2P.Port = p2pPort;

            if (TryGetPositiveIntOverride(MeshWaveEnvironment.P2PMaxPeersEnvironmentVariable, out var maxPeers))
                settings.P2P.MaxPeers = maxPeers;

            if (TryGetNonNegativeIntOverride(MeshWaveEnvironment.P2PUploadLimitEnvironmentVariable, out var uploadLimit))
                settings.P2P.UploadLimit = uploadLimit;

            if (TryGetNonNegativeIntOverride(MeshWaveEnvironment.P2PDownloadLimitEnvironmentVariable, out var downloadLimit))
                settings.P2P.DownloadLimit = downloadLimit;

            if (TryGetBooleanOverride(MeshWaveEnvironment.P2PActAsListenerEnvironmentVariable, out var listener))
                settings.P2P.ActAsListener = listener;

            var bootstrapOverride = Environment.GetEnvironmentVariable(MeshWaveEnvironment.P2PBootstrapNodesEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(bootstrapOverride))
            {
                settings.P2P.BootstrapNodes = bootstrapOverride
                    .Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        private static bool TryGetPositiveIntOverride(string variableName, out int value)
        {
            value = 0;
            var raw = Environment.GetEnvironmentVariable(variableName);
            return int.TryParse(raw, out value) && value > 0;
        }

        private static bool TryGetNonNegativeIntOverride(string variableName, out int value)
        {
            value = 0;
            var raw = Environment.GetEnvironmentVariable(variableName);
            return int.TryParse(raw, out value) && value >= 0;
        }

        private static bool TryGetBooleanOverride(string variableName, out bool value)
        {
            value = false;
            var raw = Environment.GetEnvironmentVariable(variableName);
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            switch (raw.Trim().ToLowerInvariant())
            {
                case "1":
                case "true":
                case "yes":
                case "on":
                    value = true;
                    return true;

                case "0":
                case "false":
                case "no":
                case "off":
                    value = false;
                    return true;

                default:
                    return false;
            }
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
    }
}
