using MeshWave.LibraryManager;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using MeshWave.Models;
using MeshWave.Mvvm;
using MeshWave.Services;
using MeshWave.Synchronizer;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

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

    // Artist profile fields
    private bool _isArtist = false;
    private string _bio = string.Empty;
    private string _website = string.Empty;
    private string _bannerImagePath = string.Empty;
    private string _selectedTab = "General";

    // P2P settings
    private bool _p2pEnabled;
    private bool _p2pActAsListener = true;
    private int _p2pPort = 39877;
    private int _p2pMaxPeers = 50;
    private string _p2pBootstrapNodesText = string.Empty;
    private string _p2pIdentityInfo = string.Empty;
    private WaveformStyle _waveformStyle = WaveformStyle.Filled;
    private readonly SyncOrchestrator? _sync;

    private readonly ObservableCollection<StorageCategoryUsage> _storageCategories = [];
    private string _storageStatusMessage = string.Empty;
    private double _storageQuotaWarningGb = 10;
    private long _totalDriveBytes;
    private long _freeDriveBytes;
    private long _usedDriveBytes;

    public SettingsViewModel(Action<WaveformStyle>? onWaveformStyleSaved = null, SyncOrchestrator? sync = null)
    {
        _onWaveformStyleSaved = onWaveformStyleSaved;
        _sync = sync;
        _settingsService = new SettingsService();
        _profileService = new UserProfileService();
        LoadSettings();

        SaveCommand = new RelayCommand(_ => SaveSettings());
        BrowseBaseFolderCommand = new RelayCommand(_ => BrowseStorageFolder());
        BrowseAvatarCommand = new RelayCommand(_ => BrowseAvatarImage());
        BrowseBannerCommand = new RelayCommand(_ => BrowseBannerImage());
        RegenerateIdentityCommand = new RelayCommand(_ => RegenerateIdentity());
        RefreshStorageCommand = new RelayCommand(_ => RefreshStorageStats());
        ClearPeerManifestCacheCommand = new RelayCommand(_ => ClearPeerManifestCache());
        ClearWaveformCacheCommand = new RelayCommand(_ => ClearWaveformCache());
    }

    public ICommand SaveCommand { get; }
    public ICommand BrowseBaseFolderCommand { get; }
    public ICommand BrowseAvatarCommand { get; }
    public ICommand BrowseBannerCommand { get; }
    public ICommand RegenerateIdentityCommand { get; }
    public ICommand RefreshStorageCommand { get; }
    public ICommand ClearPeerManifestCacheCommand { get; }
    public ICommand ClearWaveformCacheCommand { get; }

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

    // ---- Role and artist profile ----

    public bool IsArtist
    {
        get => _isArtist;
        set => SetProperty(ref _isArtist, value);
    }

    public string Bio
    {
        get => _bio;
        set => SetProperty(ref _bio, value);
    }

    public string Website
    {
        get => _website;
        set => SetProperty(ref _website, value);
    }

    public string BannerImagePath
    {
        get => _bannerImagePath;
        set => SetProperty(ref _bannerImagePath, value);
    }

    /// <summary>Currently selected settings tab name.</summary>
    public string SelectedTab
    {
        get => _selectedTab;
        set => SetProperty(ref _selectedTab, value);
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

    public bool P2PActAsListener
    {
        get => _p2pActAsListener;
        set => SetProperty(ref _p2pActAsListener, value);
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

    public IReadOnlyList<StorageCategoryUsage> StorageCategories => _storageCategories;

    public string StorageStatusMessage
    {
        get => _storageStatusMessage;
        set => SetProperty(ref _storageStatusMessage, value);
    }

    public double StorageQuotaWarningGb
    {
        get => _storageQuotaWarningGb;
        set
        {
            var normalized = Math.Clamp(value, 1, 5000);
            if (SetProperty(ref _storageQuotaWarningGb, normalized))
            {
                RecalculateStoragePercentages();
            }
        }
    }

    public long TotalDriveBytes
    {
        get => _totalDriveBytes;
        private set => SetProperty(ref _totalDriveBytes, value);
    }

    public long FreeDriveBytes
    {
        get => _freeDriveBytes;
        private set => SetProperty(ref _freeDriveBytes, value);
    }

    public long UsedDriveBytes
    {
        get => _usedDriveBytes;
        private set => SetProperty(ref _usedDriveBytes, value);
    }

    public string TotalDriveDisplay => FormatBytes(TotalDriveBytes);
    public string FreeDriveDisplay => FormatBytes(FreeDriveBytes);
    public string UsedDriveDisplay => FormatBytes(UsedDriveBytes);

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
        IsArtist = profile.IsArtist;
        Bio = profile.Bio;
        Website = profile.Website;
        BannerImagePath = profile.BannerImagePath;

        P2PEnabled = settings.P2P.Enabled;
        P2PActAsListener = settings.P2P.ActAsListener;
        P2PPort = settings.P2P.Port;
        P2PMaxPeers = Math.Min(settings.P2P.MaxPeers, SecurityLimits.MaxRoutingTableSize);
        P2PBootstrapNodesText = string.Join(Environment.NewLine, settings.P2P.BootstrapNodes);

        if (Enum.TryParse<WaveformStyle>(settings.Playback.WaveformStyle, out var parsedStyle))
            WaveformStyle = parsedStyle;

        StorageQuotaWarningGb = Math.Clamp(settings.Storage.QuotaWarningGb, 1, 5000);

        RefreshIdentityInfo();

        IsInitialized = !string.IsNullOrEmpty(settings.BaseFolder);
        RefreshStorageStats();
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

    public void BrowseBannerImage()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            BannerImagePath = dialog.FileName;
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
                ActAsListener = P2PActAsListener,
                Port = P2PPort,
                MaxPeers = Math.Clamp(P2PMaxPeers, 1, SecurityLimits.MaxRoutingTableSize),
                BootstrapNodes = bootstrapNodes
            },
            Storage = new StorageSettings
            {
                QuotaWarningGb = Math.Clamp(StorageQuotaWarningGb, 1, 5000)
            }
        };

        _settingsService.SaveSettings(settings);
        _settingsService.EnsureFoldersExist();

        _profileService.SaveProfile(new UserProfile
        {
            DisplayName = string.IsNullOrWhiteSpace(Username) ? "You" : Username,
            AvatarImagePath = AvatarImagePath,
            AvatarIconPath = AvatarIconPath,
            IsArtist = IsArtist,
            Bio = Bio.Length > 1000 ? Bio[..1000] : Bio,
            Website = Website,
            BannerImagePath = BannerImagePath
        });

        var savedProfile = _profileService.LoadProfile();
        AvatarIconPath = savedProfile.AvatarIconPath;

        _onWaveformStyleSaved?.Invoke(WaveformStyle);

        IsInitialized = true;
        RefreshStorageStats();

        // Broadcast the updated profile to the P2P network as a signed Profile op.
        _sync?.BroadcastProfile(
            displayName: string.IsNullOrWhiteSpace(Username) ? "You" : Username,
            isArtist: IsArtist,
            bio: Bio.Length > 1000 ? Bio[..1000] : Bio,
            website: Website,
            bannerImageHash: null);   // TODO: compute hash when content exchange is implemented
    }

    private void RefreshStorageStats()
    {
        try
        {
            var myMusic = _settingsService.GetMyMusicFolder();
            var otherMusic = _settingsService.GetOtherMusicFolder();
            var appDataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MeshWave");
            var peerManifestFolder = Path.Combine(appDataRoot, "PeerManifests");

            var myMusicBytes = GetDirectorySizeSafe(myMusic);
            var otherMusicBytes = GetDirectorySizeSafe(otherMusic);
            var manifestsBytes = GetDirectorySizeSafe(peerManifestFolder);
            var cacheBytes = GetWaveformCacheSize(myMusic) + GetWaveformCacheSize(otherMusic);

            _storageCategories.Clear();
            _storageCategories.Add(new StorageCategoryUsage("My Music", myMusic, myMusicBytes));
            _storageCategories.Add(new StorageCategoryUsage("Other Music", otherMusic, otherMusicBytes));
            _storageCategories.Add(new StorageCategoryUsage("Manifests", peerManifestFolder, manifestsBytes));
            _storageCategories.Add(new StorageCategoryUsage("Cache", "Waveform .cache folders", cacheBytes));

            var driveRoot = ResolveDriveRoot(BaseFolder, appDataRoot);
            if (!string.IsNullOrWhiteSpace(driveRoot))
            {
                var drive = new DriveInfo(driveRoot);
                TotalDriveBytes = drive.TotalSize;
                FreeDriveBytes = drive.AvailableFreeSpace;
                UsedDriveBytes = drive.TotalSize - drive.AvailableFreeSpace;
            }
            else
            {
                TotalDriveBytes = 0;
                FreeDriveBytes = 0;
                UsedDriveBytes = 0;
            }

            OnPropertyChanged(nameof(StorageCategories));
            OnPropertyChanged(nameof(TotalDriveDisplay));
            OnPropertyChanged(nameof(FreeDriveDisplay));
            OnPropertyChanged(nameof(UsedDriveDisplay));

            RecalculateStoragePercentages();
            StorageStatusMessage = "Storage details refreshed.";
        }
        catch (Exception ex)
        {
            StorageStatusMessage = $"Storage refresh failed: {ex.Message}";
        }
    }

    private void RecalculateStoragePercentages()
    {
        var quotaBytes = (long)(StorageQuotaWarningGb * 1024 * 1024 * 1024);
        foreach (var category in _storageCategories)
        {
            category.UpdateThreshold(quotaBytes);
        }
    }

    private void ClearPeerManifestCache()
    {
        try
        {
            _sync?.ClearPeerManifestCache();

            var appDataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MeshWave");
            var peerManifestFolder = Path.Combine(appDataRoot, "PeerManifests");
            if (Directory.Exists(peerManifestFolder))
            {
                foreach (var file in Directory.EnumerateFiles(peerManifestFolder, "*", SearchOption.TopDirectoryOnly))
                {
                    File.Delete(file);
                }
            }

            RefreshStorageStats();
            StorageStatusMessage = "Peer manifest cache cleared.";
        }
        catch (Exception ex)
        {
            StorageStatusMessage = $"Failed to clear peer manifest cache: {ex.Message}";
        }
    }

    private void ClearWaveformCache()
    {
        try
        {
            DeleteWaveformCache(_settingsService.GetMyMusicFolder());
            DeleteWaveformCache(_settingsService.GetOtherMusicFolder());
            RefreshStorageStats();
            StorageStatusMessage = "Waveform cache cleared.";
        }
        catch (Exception ex)
        {
            StorageStatusMessage = $"Failed to clear waveform cache: {ex.Message}";
        }
    }

    private static void DeleteWaveformCache(string rootFolder)
    {
        if (!Directory.Exists(rootFolder))
            return;

        foreach (var file in Directory.EnumerateFiles(rootFolder, "*.waveform.json", SearchOption.AllDirectories))
        {
            File.Delete(file);
        }
    }

    private static long GetWaveformCacheSize(string rootFolder)
    {
        if (!Directory.Exists(rootFolder))
            return 0;

        long total = 0;
        foreach (var file in Directory.EnumerateFiles(rootFolder, "*.waveform.json", SearchOption.AllDirectories))
        {
            try
            {
                total += new FileInfo(file).Length;
            }
            catch
            {
                // ignore unreadable files
            }
        }

        return total;
    }

    private static long GetDirectorySizeSafe(string folder)
    {
        if (!Directory.Exists(folder))
            return 0;

        long total = 0;
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            try
            {
                total += new FileInfo(file).Length;
            }
            catch
            {
                // ignore inaccessible files
            }
        }

        return total;
    }

    private static string ResolveDriveRoot(string primaryPath, string fallbackPath)
    {
        if (!string.IsNullOrWhiteSpace(primaryPath))
        {
            var root = Path.GetPathRoot(primaryPath);
            if (!string.IsNullOrWhiteSpace(root))
                return root;
        }

        var fallbackRoot = Path.GetPathRoot(fallbackPath);
        return fallbackRoot ?? string.Empty;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Max(0, bytes);
        var index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }

        return $"{value:0.##} {units[index]}";
    }
}

public sealed class StorageCategoryUsage : ViewModelBase
{
    private const double GreenThreshold = 70;
    private const double AmberThreshold = 90;

    private double _percentage;
    private Brush _barBrush = new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString("#1DB954"));

    public StorageCategoryUsage(string category, string path, long bytes)
    {
        Category = category;
        Path = path;
        Bytes = bytes;
    }

    public string Category { get; }
    public string Path { get; }
    public long Bytes { get; }

    public string SizeDisplay => FormatBytes(Bytes);

    public double Percentage
    {
        get => _percentage;
        private set => SetProperty(ref _percentage, value);
    }

    public Brush BarBrush
    {
        get => _barBrush;
        private set => SetProperty(ref _barBrush, value);
    }

    public void UpdateThreshold(long quotaBytes)
    {
        if (quotaBytes <= 0)
        {
            Percentage = 0;
            BarBrush = CreateBrush("#1DB954");
            return;
        }

        Percentage = Math.Clamp((Bytes / (double)quotaBytes) * 100.0, 0, 100);
        BarBrush = Percentage switch
        {
            < GreenThreshold => CreateBrush("#1DB954"),
            < AmberThreshold => CreateBrush("#E67E22"),
            _ => CreateBrush("#C0392B")
        };
    }

    private static SolidColorBrush CreateBrush(string hex)
        => new((Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Max(0, bytes);
        var index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }

        return $"{value:0.##} {units[index]}";
    }
}
