using MeshWave.LibraryManager;
using System.Windows.Input;
using MeshWave.Models;
using MeshWave.Mvvm;
using MeshWave.Services;

namespace MeshWave.ViewModels;

/// <summary>
/// View model for application settings and configuration.
/// </summary>
public class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService;
    private readonly UserProfileService _profileService;
    private string _baseFolder = string.Empty;
    private string _username = string.Empty;
    private bool _isInitialized = false;
    private string _theme = "Dark";
    private double _volume = 0.8;
    private string _supportedExtensionsText = string.Empty;
    private string _avatarImagePath = string.Empty;
    private string _avatarIconPath = string.Empty;

    public SettingsViewModel()
    {
        _settingsService = new SettingsService();
        _profileService = new UserProfileService();
        LoadSettings();

        SaveCommand = new RelayCommand(_ => SaveSettings());
        BrowseBaseFolderCommand = new RelayCommand(_ => BrowseStorageFolder());
        BrowseAvatarCommand = new RelayCommand(_ => BrowseAvatarImage());
    }

    public ICommand SaveCommand { get; }
    public ICommand BrowseBaseFolderCommand { get; }
    public ICommand BrowseAvatarCommand { get; }

    public string BaseFolder
    {
        get => _baseFolder;
        set => SetProperty(ref _baseFolder, value);
    }

    public string Username
    {
        get => _username;
        set => SetProperty(ref _username, value);
    }

    public string AvatarImagePath
    {
        get => _avatarImagePath;
        set => SetProperty(ref _avatarImagePath, value);
    }

    public string AvatarIconPath
    {
        get => _avatarIconPath;
        set => SetProperty(ref _avatarIconPath, value);
    }

    public string Theme
    {
        get => _theme;
        set => SetProperty(ref _theme, value);
    }

    public double Volume
    {
        get => _volume;
        set => SetProperty(ref _volume, value);
    }

    public string SupportedExtensionsText
    {
        get => _supportedExtensionsText;
        set => SetProperty(ref _supportedExtensionsText, value);
    }

    public bool IsInitialized
    {
        get => _isInitialized;
        set => SetProperty(ref _isInitialized, value);
    }

    private void LoadSettings()
    {
        var settings = _settingsService.LoadSettings();
        BaseFolder = settings.BaseFolder;
        Theme = settings.Theme;
        Volume = settings.Playback.Volume;

        var extensions = settings.SupportedExtensions.Count > 0
            ? settings.SupportedExtensions
            : LocalLibraryManager.SupportedExtensions;
        SupportedExtensionsText = string.Join(", ", extensions);

        var profile = _profileService.LoadProfile();
        Username = profile.DisplayName;
        AvatarImagePath = profile.AvatarImagePath;
        AvatarIconPath = profile.AvatarIconPath;

        IsInitialized = !string.IsNullOrEmpty(settings.BaseFolder);
    }

    public void BrowseStorageFolder()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            CheckFileExists = false,
            CheckPathExists = true,
            FileName = "Select Folder...",
            Filter = "Folders|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            var folder = System.IO.Path.GetDirectoryName(dialog.FileName);
            if (folder != null)
            {
                BaseFolder = folder;
            }
        }
    }

    public void GenerateKeypair()
    {
        // TODO: Implement keypair generation
    }

    public void BrowseAvatarImage()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            AvatarImagePath = dialog.FileName;
        }
    }

    public void SaveSettings()
    {
        var settings = new AppSettings
        {
            BaseFolder = BaseFolder,
            Theme = Theme,
            AudioDevice = "Default",
            SupportedExtensions = SupportedExtensionsText
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(ext => ext.StartsWith('.') ? ext : $".{ext}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Playback = new PlaybackSettings
            {
                Volume = Volume,
                RegisterPlayAt = 0.5
            }
        };

        _settingsService.SaveSettings(settings);
        _settingsService.EnsureFoldersExist();

        _profileService.SaveProfile(new UserProfile
        {
            DisplayName = string.IsNullOrWhiteSpace(Username) ? "You" : Username,
            AvatarImagePath = AvatarImagePath,
            AvatarIconPath = AvatarIconPath
        });

        var savedProfile = _profileService.LoadProfile();
        AvatarIconPath = savedProfile.AvatarIconPath;

        IsInitialized = true;

        // TODO: Show success message
    }
}
