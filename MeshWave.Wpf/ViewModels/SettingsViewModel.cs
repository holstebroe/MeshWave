using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.IO.Compression;
using System.Threading.Tasks;
using System.Windows.Input;
using MeshWave.Common.Core;
using MeshWave.LibraryManager;
using MeshWave.Synchronizer;
using MeshWave.Wpf.Models;
using MeshWave.Wpf.Mvvm;
using MeshWave.Wpf.Services;
using MeshWave.Wpf.Views;
using Clipboard = System.Windows.Clipboard;
using ColorConverter = System.Windows.Media.ColorConverter;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace MeshWave.Wpf.ViewModels;

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
    private bool _isInitialized;
    private string _theme = "Dark";
    private double _volume = 0.8;
    private string _supportedExtensionsText = string.Empty;
    private AudioQuality _streamingAudioQuality = AudioQuality.Compressed;
    private AudioQuality _downloadAudioQuality = AudioQuality.Original;
    private bool _useDynamicAccentColor;
    private string _avatarImagePath = string.Empty;
    private string _avatarIconPath = string.Empty;

    // Artist profile fields
    private bool _isArtist;
    private string _bio = string.Empty;
    private string _website = string.Empty;
    private string _bannerImagePath = string.Empty;
    private string _selectedTab = "General";

    // P2P settings
    private bool _p2pEnabled;
    private bool _p2pLoggingEnabled;
    private bool _p2pLoggingVerbose;
    private bool _p2pActAsListener = true;
    private int _p2pPort = 39877;
    private int _p2pMaxPeers = 50;
    private string _p2pBootstrapNodesText = string.Empty;
    private string _p2pIdentityInfo = string.Empty;
    private WaveformStyle _waveformStyle = WaveformStyle.Filled;
    private readonly SyncOrchestrator? _sync;

    private bool _isExportingDiagnostics;
    private readonly ObservableCollection<StorageCategoryUsage> _storageCategories = [];
    private string _storageStatusMessage = string.Empty;
    private string _networkDiagnosticsText = "No connection attempts recorded yet.";
    private double _storageQuotaWarningGb = 10;
    private long _totalDriveBytes;
    private long _freeDriveBytes;
    private long _usedDriveBytes;

    public SettingsViewModel(SettingsService settingsService, Action<WaveformStyle>? onWaveformStyleSaved = null, SyncOrchestrator? sync = null)
    {
        _onWaveformStyleSaved = onWaveformStyleSaved;
        _sync = sync;
        _settingsService = settingsService;
        _profileService = new UserProfileService();
        LoadSettings();

        SaveCommand = new RelayCommand(_ => SaveSettings());
        BrowseBaseFolderCommand = new RelayCommand(_ => BrowseStorageFolder());
        BrowseAvatarCommand = new RelayCommand(_ => BrowseAvatarImage());
        BrowseBannerCommand = new RelayCommand(_ => BrowseBannerImage());
        RegenerateIdentityCommand = new RelayCommand(_ => RegenerateIdentity());
        RefreshStorageCommand = new RelayCommand(_ => RefreshStorageStats());
        RefreshNetworkDiagnosticsCommand = new RelayCommand(_ => RefreshNetworkDiagnostics());
        OpenDetailedDiagnosticsWindowCommand = new RelayCommand(_ => OpenDetailedDiagnosticsWindow(), _ => _sync != null);
        ExportDiagnosticPackageCommand = new RelayCommand(_ => ExportDiagnosticPackageAsync(), _ => !IsExportingDiagnostics);
        ClearPeerManifestCacheCommand = new RelayCommand(_ => ClearPeerManifestCache());
        ClearWaveformCacheCommand = new RelayCommand(_ => ClearWaveformCache());
        OpenLogFolderCommand = new RelayCommand(_ => OpenLogFolder());
        CopyLogsToClipboardCommand = new RelayCommand(_ => CopyLogsToClipboard());
    }

    public ICommand SaveCommand { get; }
    public ICommand BrowseBaseFolderCommand { get; }
    public ICommand BrowseAvatarCommand { get; }
    public ICommand BrowseBannerCommand { get; }
    public ICommand RegenerateIdentityCommand { get; }
    public ICommand RefreshStorageCommand { get; }
    public ICommand RefreshNetworkDiagnosticsCommand { get; }
    public ICommand OpenDetailedDiagnosticsWindowCommand { get; }
    public ICommand ExportDiagnosticPackageCommand { get; }
    public ICommand ClearPeerManifestCacheCommand { get; }
    public ICommand ClearWaveformCacheCommand { get; }
    public ICommand OpenLogFolderCommand { get; }
    public ICommand CopyLogsToClipboardCommand { get; }

    public string BaseFolder
    {
        get => _baseFolder;
        set => SetProperty(ref _baseFolder, value);
    }

    public bool IsExportingDiagnostics
    {
        get => _isExportingDiagnostics;
        set => SetProperty(ref _isExportingDiagnostics, value);
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

    public AudioQuality StreamingAudioQuality
    {
        get => _streamingAudioQuality;
        set => SetProperty(ref _streamingAudioQuality, value);
    }

    public AudioQuality DownloadAudioQuality
    {
        get => _downloadAudioQuality;
        set => SetProperty(ref _downloadAudioQuality, value);
    }

    public bool UseDynamicAccentColor
    {
        get => _useDynamicAccentColor;
        set => SetProperty(ref _useDynamicAccentColor, value);
    }

    public IEnumerable<AudioQuality> AvailableAudioQualities => Enum.GetValues<AudioQuality>();

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

    public bool P2PLoggingEnabled
    {
        get => _p2pLoggingEnabled;
        set => SetProperty(ref _p2pLoggingEnabled, value);
    }

    public bool P2PLoggingVerbose
    {
        get => _p2pLoggingVerbose;
        set => SetProperty(ref _p2pLoggingVerbose, value);
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

    public string NetworkDiagnosticsText
    {
        get => _networkDiagnosticsText;
        set => SetProperty(ref _networkDiagnosticsText, value);
    }

    public double StorageQuotaWarningGb
    {
        get => _storageQuotaWarningGb;
        set
        {
            var normalized = Math.Clamp(value, 1, 5000);
            if (SetProperty(ref _storageQuotaWarningGb, normalized)) RecalculateStoragePercentages();
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
        StreamingAudioQuality = settings.Playback.StreamingAudioQuality;
        DownloadAudioQuality = settings.Playback.DownloadAudioQuality;
        UseDynamicAccentColor = settings.Playback.UseDynamicAccentColor;

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
        P2PLoggingEnabled = settings.Logging.Enabled;
        P2PLoggingVerbose = settings.Logging.Verbose;
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
        RefreshNetworkDiagnostics();
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
        var dialog = new OpenFileDialog
        {
            CheckFileExists = false,
            CheckPathExists = true,
            FileName = "Select Folder...",
            Filter = "Folders|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            var folder = Path.GetDirectoryName(dialog.FileName);
            if (folder != null) BaseFolder = folder;
        }
    }

    public void BrowseAvatarImage()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() == true) AvatarImagePath = dialog.FileName;
    }

    public void BrowseBannerImage()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() == true) BannerImagePath = dialog.FileName;
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
                WaveformStyle = WaveformStyle.ToString(),
                StreamingAudioQuality = StreamingAudioQuality,
                DownloadAudioQuality = DownloadAudioQuality,
                UseDynamicAccentColor = UseDynamicAccentColor
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
            },
            Logging = new LoggingSettings
            {
                Enabled = P2PLoggingEnabled,
                Verbose = P2PLoggingVerbose
            }
        };

        _settingsService.SaveSettings(settings);
        _settingsService.EnsureFoldersExist();
        LoggingConfiguration.Configure(settings.Logging);

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
            var myMusic = _settingsService.GetLocalMusicFolder();
            var otherMusic = _settingsService.GetPeerMusicFolder();
            var appDataRoot = MeshWaveEnvironment.GetAppDataRoot();
            var peerManifestFolder = Path.Combine(appDataRoot, "PeerManifests");

            var myMusicBytes = GetDirectorySizeSafe(myMusic);
            var otherMusicBytes = GetDirectorySizeSafe(otherMusic);
            var manifestsBytes = GetDirectorySizeSafe(peerManifestFolder);
            var cacheBytes = GetWaveformCacheSize(myMusic) + GetWaveformCacheSize(otherMusic);

            _storageCategories.Clear();
            _storageCategories.Add(new StorageCategoryUsage("Local Music", myMusic, myMusicBytes));
            _storageCategories.Add(new StorageCategoryUsage("Peer Music", otherMusic, otherMusicBytes));
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
        foreach (var category in _storageCategories) category.UpdateThreshold(quotaBytes);
    }

    private void ClearPeerManifestCache()
    {
        try
        {
            _sync?.ClearPeerManifestCache();

            var appDataRoot = MeshWaveEnvironment.GetAppDataRoot();
            var peerManifestFolder = Path.Combine(appDataRoot, "PeerManifests");
            if (Directory.Exists(peerManifestFolder))
                foreach (var file in Directory.EnumerateFiles(peerManifestFolder, "*", SearchOption.TopDirectoryOnly))
                    File.Delete(file);

            RefreshStorageStats();
            StorageStatusMessage = "Peer manifest cache cleared.";
        }
        catch (Exception ex)
        {
            StorageStatusMessage = $"Failed to clear peer manifest cache: {ex.Message}";
        }
    }

    private void RefreshNetworkDiagnostics()
    {
        var lines = new List<string>();

        if (_sync != null)
        {
            lines.Add("Network Statistics:");
            lines.Add($"- Routing table peers: {_sync.ConnectedPeerCount}");
            lines.Add($"- Mesh peers: {_sync.MeshPeerCount}");
            lines.Add($"- Bootstrap peers: {_sync.BootstrapPeerCount}");
            lines.Add($"- Inbound peer connections (manifest pushes to this node): {_sync.InboundManifestPushCount}");
            lines.Add($"- Outbound peer connections (manifest fetches from this node): {_sync.OutboundManifestFetchCount}");
            lines.Add($"- Configured bootstrap servers: {P2PBootstrapNodesText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length}");
            lines.Add("");
        }

        var report = _sync?.LastConnectionAttemptReport;
        if (report == null)
        {
            lines.Add("No connection attempts recorded yet.");
            NetworkDiagnosticsText = string.Join(Environment.NewLine, lines);
            return;
        }

        lines.Add($"Peer: {report.PeerUserId}");
        lines.Add($"Requested hash: {report.RequestedContentHash}");
        lines.Add($"Local endpoint suggestion: {(string.IsNullOrWhiteSpace(report.SuggestedLocalIp) ? "n/a" : report.SuggestedLocalIp)}:{(report.LocalManifestPort > 0 ? report.LocalManifestPort : ManifestExchangeServer.DefaultPort)}");
        lines.Add($"Remote endpoint: {(string.IsNullOrWhiteSpace(report.TargetAddress) ? "n/a" : report.TargetAddress)}:{report.TargetPort}");
        lines.Add($"Attempted at: {report.CreatedAtUtc:O}");
        lines.Add("");
        lines.Add("Attempts:");

        foreach (var attempt in report.Attempts)
        {
            lines.Add($"- {attempt.Method}: {(attempt.Success ? "ok" : "fail")}");
            lines.Add($"  {attempt.Details}");
        }

        NetworkDiagnosticsText = string.Join(Environment.NewLine, lines);
    }


    private async void ExportDiagnosticPackageAsync()
    {
        var saveFileDialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "ZIP Archive (*.zip)|*.zip",
            DefaultExt = ".zip",
            FileName = "meshwave-diagnostics.zip",
            Title = "Export Diagnostic Package"
        };

        if (saveFileDialog.ShowDialog() != true)
            return;

        IsExportingDiagnostics = true;
        StorageStatusMessage = "Exporting diagnostic package... Please wait.";

        // CommandManager needs this to update CanExecute
        System.Windows.Application.Current.Dispatcher.Invoke(CommandManager.InvalidateRequerySuggested);

        try
        {
            await Task.Run(() =>
            {
                var tempDir = Path.Combine(Path.GetTempPath(), "MeshWaveDiagnostics_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);

                try
                {
                    // 1. Gather logs
                    var logsDir = LoggingConfiguration.GetLogsFolder();
                    if (Directory.Exists(logsDir))
                    {
                        var logFiles = Directory.GetFiles(logsDir, "*.log");
                        foreach (var logFile in logFiles)
                        {
                            var destPath = Path.Combine(tempDir, Path.GetFileName(logFile));
                            try
                            {
                                using var sourceStream = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                                using var destStream = new FileStream(destPath, FileMode.Create, FileAccess.Write);
                                sourceStream.CopyTo(destStream);
                            }
                            catch
                            {
                                // Ignore files we can't read
                            }
                        }
                    }

                    // 2. Generate network-snapshot.json
                    object snapshotObj = null;
                    if (_sync != null)
                    {
                        var peers = _sync.GetPeerDiagnosticsSnapshots();
                        var peerLogs = peers.ToDictionary(
                            p => p.UserId,
                            p => p.RecentMessages.TakeLast(50).ToList()
                        );

                        var localManifest = _sync.GetPeerManifest(_sync.Identity?.UserId ?? string.Empty);
                        // The Manifest object contains no private keys, so we can serialize it.

                        snapshotObj = new
                        {
                            NetworkStats = new
                            {
                                ConnectedPeers = _sync.ConnectedPeerCount,
                                MeshPeers = _sync.MeshPeerCount,
                                BootstrapPeers = _sync.BootstrapPeerCount,
                                InboundManifestPushes = _sync.InboundManifestPushCount,
                                OutboundManifestFetches = _sync.OutboundManifestFetchCount,
                                NatStatus = _sync.NatStatus,
                                ExternalIP = _sync.ExternalIPAddress
                            },
                            PeerMessageLogs = peerLogs,
                            LocalManifest = localManifest
                        };
                    }
                    else
                    {
                        snapshotObj = new { Status = "SyncOrchestrator not initialized." };
                    }

                    var snapshotJson = JsonSerializer.Serialize(snapshotObj, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(Path.Combine(tempDir, "network-snapshot.json"), snapshotJson);

                    // 3. Zip it
                    if (File.Exists(saveFileDialog.FileName))
                    {
                        File.Delete(saveFileDialog.FileName);
                    }
                    ZipFile.CreateFromDirectory(tempDir, saveFileDialog.FileName);
                }
                finally
                {
                    // Clean up temp directory
                    if (Directory.Exists(tempDir))
                    {
                        try
                        {
                            Directory.Delete(tempDir, true);
                        }
                        catch
                        {
                            // ignore cleanup errors
                        }
                    }
                }
            });

            StorageStatusMessage = "Diagnostic package exported successfully.";
            System.Windows.MessageBox.Show("Diagnostic package exported successfully.", "Export Complete", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StorageStatusMessage = $"Failed to export diagnostic package: {ex.Message}";
            System.Windows.MessageBox.Show($"Failed to export diagnostic package: {ex.Message}", "Export Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsExportingDiagnostics = false;
            System.Windows.Application.Current.Dispatcher.Invoke(CommandManager.InvalidateRequerySuggested);
        }
    }

    private void OpenDetailedDiagnosticsWindow()
    {
        if (_sync == null)
            return;

        var appVm = Application.Current.MainWindow?.DataContext as ApplicationViewModel;
        if (appVm == null)
            return;

        var vm = new NetworkDiagnosticsWindowViewModel(_sync, appVm);
        var win = new NetworkDiagnosticsWindow
        {
            DataContext = vm,
            Owner = Application.Current.MainWindow
        };
        win.Show();
    }

    private void OpenLogFolder()
    {
        try
        {
            var folder = LoggingConfiguration.GetLogsFolder();
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            Process.Start("explorer.exe", folder);
        }
        catch (Exception ex)
        {
            StorageStatusMessage = $"Failed to open log folder: {ex.Message}";
        }
    }

    private void CopyLogsToClipboard()
    {
        try
        {
            var logs = LoggingConfiguration.GetRecentLogs();
            Clipboard.SetText(logs);
            StorageStatusMessage = "Recent logs copied to clipboard.";
        }
        catch (Exception ex)
        {
            StorageStatusMessage = $"Failed to copy logs to clipboard: {ex.Message}";
        }
    }

    private void ClearWaveformCache()
    {
        try
        {
            DeleteWaveformCache(_settingsService.GetLocalMusicFolder());
            DeleteWaveformCache(_settingsService.GetPeerMusicFolder());
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

        // Clean up both old and new format during transition
        foreach (var file in Directory.EnumerateFiles(rootFolder, "*.waveform*", SearchOption.AllDirectories))
            if (file.EndsWith(".waveform") || file.EndsWith(".waveform.json"))
                File.Delete(file);
    }

    private static long GetWaveformCacheSize(string rootFolder)
    {
        if (!Directory.Exists(rootFolder))
            return 0;

        long total = 0;
        foreach (var file in Directory.EnumerateFiles(rootFolder, "*.waveform*", SearchOption.AllDirectories))
        {
            if (!file.EndsWith(".waveform") && !file.EndsWith(".waveform.json"))
                continue;

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
            try
            {
                total += new FileInfo(file).Length;
            }
            catch
            {
                // ignore inaccessible files
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
    private Brush _barBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1DB954"));

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
    {
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
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
