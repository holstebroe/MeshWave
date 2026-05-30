using MeshWave.LibraryManager;
using System.Collections.Generic;
using System.Windows.Input;
using MeshWave.Models;
using MeshWave.Mvvm;
using MeshWave.Services;
using MeshWave.Synchronizer;

namespace MeshWave.ViewModels;

/// <summary>
/// View model for application settings and configuration.
/// </summary>
public class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService;
    private readonly UserProfileService _profileService;
    private readonly P2PIdentityService _identityService = new();
    private readonly Action<WaveformStyle>? _onWaveformStyleSaved;
    private string _baseFolder = string.Empty;
    private string _username = string.Empty;
    private bool _isInitialized = false;
    private string _theme = "Dark";
    private double _volume = 0.8;
    private string _supportedExtensionsText = string.Empty;
    private string _avatarImagePath = string.Empty;
    private string _avatarIconPath = string.Empty;

    // P2P settings
    private bool _p2pEnabled;
    private int _p2pPort = 39877;
    private int _p2pMaxPeers = 50;
    private string _p2pBootstrapNodesText = string.Empty;
    private string _p2pIdentityInfo = string.Empty;
    private WaveformStyle _waveformStyle = WaveformStyle.Filled;

    public SettingsViewModel(Action<WaveformStyle>? onWaveformStyleSaved = null)
    {
        _onWaveformStyleSaved = onWaveformStyleSaved;
        _settingsService = new SettingsService();
        _profileService = new UserProfileService();
        LoadSettings();

        SaveCommand = new RelayCommand(_ => SaveSettings());
        BrowseBaseFolderCommand = new RelayCommand(_ => BrowseStorageFolder());
        BrowseAvatarCommand = new RelayCommand(_ => BrowseAvatarImage());
        RegenerateIdentityCommand = new RelayCommand(_ => RegenerateIdentity());
    }

    public ICommand SaveCommand { get; }
    public ICommand BrowseBaseFolderCommand { get; }
    public ICommand BrowseAvatarCommand { get; }
    public ICommand RegenerateIdentityCommand { get; }

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

    // ---- P2P Properties ----

    public bool P2PEnabled
    {
        get => _p2pEnabled;
        set => SetProperty(ref _p2pEnabled, value);
    }

    public int P2PPort
    {
        get => _p2pPort;
        set => SetProperty(ref _p2pPort, value);
    }

    public int P2PMaxPeers
    {
        get => _p2pMaxPeers;
        set => SetProperty(ref _p2pMaxPeers, value);
    }

    /// <summary>Bootstrap nodes, one per line (host:port).</summary>
    public string P2PBootstrapNodesText
    {
        get => _p2pBootstrapNodesText;
        set => SetProperty(ref _p2pBootstrapNodesText, value);
    }

    /// <summary>Read-only display of the local peer UserId (public key fingerprint).</summary>
    public string P2PIdentityInfo
    {
        get => _p2pIdentityInfo;
        set => SetProperty(ref _p2pIdentityInfo, value);
    }

    public WaveformStyle WaveformStyle
    {
        get => _waveformStyle;
        set => SetProperty(ref _waveformStyle, value);
    }

    public IEnumerable<WaveformStyle> AvailableWaveformStyles => Enum.GetValues<WaveformStyle>();

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

        P2PEnabled = settings.P2P.Enabled;
        P2PPort = settings.P2P.Port;
        P2PMaxPeers = Math.Min(settings.P2P.MaxPeers, SecurityLimits.MaxRoutingTableSize);
        P2PBootstrapNodesText = string.Join(Environment.NewLine, settings.P2P.BootstrapNodes);

        if (Enum.TryParse<WaveformStyle>(settings.Playback.WaveformStyle, out var parsedStyle))
            WaveformStyle = parsedStyle;

        RefreshIdentityInfo();

        IsInitialized = !string.IsNullOrEmpty(settings.BaseFolder);
    }

    private void RefreshIdentityInfo()
    {
        if (_identityService.IdentityExists())
        {
            var identity = _identityService.LoadOrCreate(Username);
            P2PIdentityInfo = $"Peer ID: {identity.UserId}";
        }
        else
        {
            P2PIdentityInfo = "No identity yet — will be created on first connect.";
        }
    }

    private void RegenerateIdentity()
    {
        var profile = _profileService.LoadProfile();
        var identity = _identityService.Regenerate(profile.DisplayName);
        P2PIdentityInfo = $"Peer ID: {identity.UserId}  (regenerated — peers will see you as a new user)";
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
        var bootstrapNodes = P2PBootstrapNodesText
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(SecurityLimits.MaxBootstrapNodes)
            .ToList();

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
                RegisterPlayAt = 0.5,
                WaveformStyle = WaveformStyle.ToString()
            },
            P2P = new P2PSettings
            {
                Enabled = P2PEnabled,
                Port = P2PPort,
                MaxPeers = Math.Clamp(P2PMaxPeers, 1, SecurityLimits.MaxRoutingTableSize),
                BootstrapNodes = bootstrapNodes
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

        _onWaveformStyleSaved?.Invoke(WaveformStyle);

        IsInitialized = true;
    }
}
