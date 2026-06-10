using AsyncAwaitBestPractices.MVVM;
using MeshWave.Common.Core;
using MeshWave.Common.Core.Crypto;
using MeshWave.Common.Core.Models;
using MeshWave.Common.Core.Storage;
using MeshWave.LibraryManager;
using MeshWave.Synchronizer;
using MeshWave.Wpf.Models;
using MeshWave.Wpf.Mvvm;
using MeshWave.Wpf.Services;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Input;

namespace MeshWave.Wpf.ViewModels;

/// <summary>
/// Main application view model.
/// Manages overall application state, navigation, and P2P sync lifecycle.
/// </summary>
public class ApplicationViewModel : ViewModelBase
{
    private string _applicationTitle = "MeshWave";
    private ViewModelBase _currentViewModel;

    private readonly SettingsService _settingsService;
    private readonly UserProfileService _profileService;
    private readonly P2PIdentityService _identityService = new();
    private readonly ManifestManager _manifestManager = new();
    private readonly DownloadQueueService _downloadQueue = new();
    private readonly UserRepository _userRepository;
    private readonly MetadataLookupRepository _metadataLookup;
    private bool _resumeStateDirty;

    private bool _p2pIsConnected;
    private string _p2pStatusText = "Disconnected";
    private string _p2pStatusTooltip = "Disconnected from the mesh network.";
    private int _p2pPeerCount;
    private bool _p2pActAsListener = true;
    private NetworkStatus _networkStatus = NetworkStatus.Offline;
    private readonly Dictionary<string, int> _lastKnownReleaseSequenceByPeer = new(StringComparer.OrdinalIgnoreCase);

    public ApplicationViewModel(
        SettingsService settingsService,
        UserProfileService? profileService = null,
        Func<IAudioPlaybackService>? audioServiceFactory = null)
    {
        _settingsService = settingsService;
        _profileService = profileService ?? new UserProfileService();

        var settings = _settingsService.LoadSettings();
        _userRepository = new UserRepository(settings.BaseFolder);
        _metadataLookup = new MetadataLookupRepository(_settingsService.GetLocalMusicFolder());
        var catalogueService = new CatalogueService();

        // DI wire-up of the default file-based PeerManifestStore
        var manifestStore = PeerManifestStore.CreateAtBase(_userRepository.BaseDataFolder);

        SyncOrchestrator = new SyncOrchestrator(
            userRepository: _userRepository,
            catalogueService: catalogueService,
            peerManifestStore: manifestStore);

        Playback = new PlaybackViewModel(SyncOrchestrator, _userRepository, _metadataLookup, audioServiceFactory);
        _currentViewModel = new HomeViewModel();


        HomeMenuNavCommand = new RelayCommand(HomeMenuNav);


        // Apply persisted waveform style immediately
        var savedSettings = settings;
        if (Enum.TryParse<WaveformStyle>(savedSettings.Playback.WaveformStyle, out var savedStyle))
            Playback.WaveformStyle = savedStyle;

        Playback.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PlaybackViewModel.CurrentTrackTitle)
                || e.PropertyName == nameof(PlaybackViewModel.CurrentPosition)
                || e.PropertyName == nameof(PlaybackViewModel.IsPlaying)
                || e.PropertyName == nameof(PlaybackViewModel.Duration)
                || e.PropertyName == nameof(PlaybackViewModel.SelectedAlbumTrack)
                || e.PropertyName == nameof(PlaybackViewModel.AlbumTracks)
                || e.PropertyName == nameof(PlaybackViewModel.TrackContextTitle)
                || e.PropertyName == nameof(PlaybackViewModel.TrackContextIconPath))
                _resumeStateDirty = true;
        };

        RestorePlaybackState(savedSettings);

        ConnectP2PCommand = new RelayCommand(_ => _ = ConnectP2PAsync(), _ => !P2PIsConnected);
        DisconnectP2PCommand = new RelayCommand(_ => _ = DisconnectP2PAsync(), _ => P2PIsConnected);

        SyncOrchestrator.PeerCountChanged += (_, _) =>
        {
            P2PPeerCount = SyncOrchestrator.ConnectedPeerCount;
            UpdateP2PStatusText();
        };

        SyncOrchestrator.ManifestMerged += (_, e) =>
        {
            if (CurrentViewModel is CommunityViewModel)
                return;

            if (HasNewReleaseFromFollowedPeer(e.UserId))
                HasCommunityNotification = true;
        };

        SyncOrchestrator.PeerCountChanged += (_, _) =>
        {
            if (SyncOrchestrator.IsRunning)
            {
                var current = CurrentViewModel;
                if (current is LibraryViewModel lib && lib.IsMyMusicLibrary) lib.LoadFromConfiguredBaseFolder();
            }
        };

        // Record a signed Play operation the first time each track starts playing.
        Playback.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PlaybackViewModel.IsPlaying) && Playback.IsPlaying)
                SyncOrchestrator.RecordPlay(
                    Playback.CurrentTrackId,
                    Playback.TrackTitle,
                    Playback.Artist);
        };

        DownloadQueueItems.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                // We don't have a list of old items, so we should ideally unhook from everything,
                // but since we don't track all items we've ever hooked to, we just hook to the current ones.
                // In practice, DownloadQueueItems is only cleared or items are removed individually.
                UpdateDownloadStats();
            }
            else
            {
                if (e.NewItems != null)
                    foreach (DownloadQueueItem item in e.NewItems)
                        item.PropertyChanged += OnDownloadItemPropertyChanged;
                if (e.OldItems != null)
                    foreach (DownloadQueueItem item in e.OldItems)
                        item.PropertyChanged -= OnDownloadItemPropertyChanged;
                UpdateDownloadStats();
            }
        };

        foreach (var item in DownloadQueueItems)
            item.PropertyChanged += OnDownloadItemPropertyChanged;

        UpdateDownloadStats();

        InitializeP2PAsync();


    }

    private void OnDownloadItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DownloadQueueItem.State)) UpdateDownloadStats();
    }

    private void UpdateDownloadStats()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(UpdateDownloadStats);
            return;
        }

        ActiveDownloadCount = DownloadQueueItems.Count(i => i.State == DownloadState.Downloading || i.State == DownloadState.Pending);
        HasActiveDownloads = DownloadQueueItems.Any(i => i.State == DownloadState.Downloading || i.State == DownloadState.Pending || i.State == DownloadState.Failed);
        OnPropertyChanged(nameof(ActiveDownloads));
    }

    public string ApplicationTitle
    {
        get => _applicationTitle;
        set => SetProperty(ref _applicationTitle, value);
    }

    public ViewModelBase CurrentViewModel
    {
        get => _currentViewModel;
        set => SetProperty(ref _currentViewModel, value);
    }

    public bool P2PIsConnected
    {
        get => _p2pIsConnected;
        private set => SetProperty(ref _p2pIsConnected, value);
    }

    public string P2PStatusText
    {
        get => _p2pStatusText;
        private set => SetProperty(ref _p2pStatusText, value);
    }

    public string P2PStatusTooltip
    {
        get => _p2pStatusTooltip;
        private set => SetProperty(ref _p2pStatusTooltip, value);
    }

    public NetworkStatus NetworkStatus
    {
        get => _networkStatus;
        private set => SetProperty(ref _networkStatus, value);
    }

    public string P2PStatusColor
    {
        get
        {
            return NetworkStatus switch
            {
                NetworkStatus.Connected => "#27AE60",
                NetworkStatus.Restricted => "#F1C40F",
                NetworkStatus.Connecting => "#F1C40F",
                _ => "#E74C3C"
            };
        }
    }

    public int P2PPeerCount
    {
        get => _p2pPeerCount;
        private set => SetProperty(ref _p2pPeerCount, value);
    }

    public ICommand ConnectP2PCommand { get; }
    public ICommand DisconnectP2PCommand { get; }

    public SyncOrchestrator SyncOrchestrator { get; }

    public PlaybackViewModel Playback { get; }

    public ObservableCollection<DownloadQueueItem> DownloadQueueItems => _downloadQueue.AllItems;

    public int ActiveDownloadCount
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public bool HasActiveDownloads
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public IEnumerable<DownloadQueueItem> ActiveDownloads => DownloadQueueItems.Where(i => i.State == DownloadState.Downloading || i.State == DownloadState.Pending || i.State == DownloadState.Failed);

    public string BuildMeshDiagnosticsSummary()
    {
        var snapshots = SyncOrchestrator.GetPeerDiagnosticsSnapshots().ToList();
        var routingPeers = SyncOrchestrator.GetPeers().ToList();

        var routingMesh = routingPeers.Count(p => !p.UserId.StartsWith("bootstrap:", StringComparison.OrdinalIgnoreCase));
        var routingBootstrap = routingPeers.Count - routingMesh;

        var meshPeers = snapshots.Where(p => !p.IsBootstrap).ToList();
        var meshOnline = meshPeers.Count(p => p.IsOnline);
        var meshWithManifest = meshPeers.Count(p => p.HasManifest);
        var meshWithoutManifest = Math.Max(0, meshPeers.Count - meshWithManifest);

        var peerTracks = meshPeers.Sum(p => p.PublishedTrackCount);
        var peerAlbums = meshPeers.Sum(p => p.PublishedAlbumCount);

        return $"Routing: {SyncOrchestrator.ConnectedPeerCount} ({routingMesh} mesh, {routingBootstrap} bootstrap) · "
             + $"Mesh diagnostics: {meshOnline}/{meshPeers.Count} online, {meshWithManifest} with manifest, {meshWithoutManifest} without manifest · "
             + $"Local published: {SyncOrchestrator.LocalPublishedAlbumCount} albums, {SyncOrchestrator.LocalPublishedTrackCount} tracks · "
             + $"Peer totals: {peerAlbums} albums, {peerTracks} tracks";
    }

    public void NavigateToHome()
    {
        CurrentViewModel = new HomeViewModel();
    }

    public void NavigateToLibrary()
    {
        CurrentViewModel = new LibraryViewModel(this, isMyMusicLibrary: false);
    }

    public void NavigateToMyMusic()
    {
        CurrentViewModel = new LibraryViewModel(this, isMyMusicLibrary: true);
    }

    public void NavigateToSettings()
    {
        CurrentViewModel = new SettingsViewModel(style => Playback.WaveformStyle = style, SyncOrchestrator);
    }

    public void NavigateToBrowse(string? artistUserId = null)
    {
        var vm = new BrowseViewModel(SyncOrchestrator, _downloadQueue,
            (title, artist, duration, filePath, length) =>
            {
                Playback.Stop();
                Playback.LoadTrack(title, artist, duration, filePath, remoteContentLength: length);
                CurrentViewModel = Playback;
            },
            settingsService: _settingsService);
        if (!string.IsNullOrWhiteSpace(artistUserId))
            vm.NavigateToArtist(artistUserId);
        CurrentViewModel = vm;
    }

    private bool _hasCommunityNotification;

    public bool HasCommunityNotification
    {
        get => _hasCommunityNotification;
        private set => SetProperty(ref _hasCommunityNotification, value);
    }

    public ICommand HomeMenuNavCommand { get; }
    private void HomeMenuNav()
    {
        PersistPlaybackState();
        NavigateToHome();
    }

    public void NavigateToCommunity()
    {
        var vm = new CommunityViewModel(SyncOrchestrator, NavigateToBrowse, settingsService: _settingsService);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CommunityViewModel.HasNewReleases))
                HasCommunityNotification = vm.HasNewReleases;
        };
        HasCommunityNotification = false;
        CurrentViewModel = vm;
    }

    public void NavigateToPlayback()
    {
        CurrentViewModel = Playback;
    }

    public void PlayTrack(string trackTitle, string artist, TimeSpan duration, string filePath, IEnumerable<PlaybackTrackListItem>? contextTracks = null, string? selectedTrackId = null, string? contextTitle = null, string? contextIconPath = null)
    {
        if (contextTracks != null) Playback.SetAlbumTrackContext(contextTracks, selectedTrackId, contextTitle, contextIconPath);

        Playback.Stop();
        Playback.LoadTrack(trackTitle, artist, duration, filePath);
        _resumeStateDirty = true;
        PersistPlaybackState();
        CurrentViewModel = Playback;
    }

    /// <summary>
    /// Announces a released track to the P2P network.
    /// </summary>
    public void AnnounceTrackToNetwork(string trackId, string contentHash, string title, string artist, string album)
    {
        if (!P2PIsConnected) return;
        SyncOrchestrator.AnnounceTrack(trackId, contentHash, new Dictionary<string, string>
        {
            ["title"] = SecurityLimits.Truncate(title, SecurityLimits.MaxTrackTitleLength),
            ["artist"] = SecurityLimits.Truncate(artist, SecurityLimits.MaxArtistNameLength),
            ["album"] = SecurityLimits.Truncate(album, SecurityLimits.MaxAlbumNameLength)
        });
    }

    /// <summary>
    /// Updates a released track in the P2P network.
    /// </summary>
    public void UpdateTrackInNetwork(string trackId, string contentHash, string title, string artist, string album)
    {
        if (!P2PIsConnected) return;
        SyncOrchestrator.UpdateTrack(trackId, contentHash, new Dictionary<string, string>
        {
            ["title"] = SecurityLimits.Truncate(title, SecurityLimits.MaxTrackTitleLength),
            ["artist"] = SecurityLimits.Truncate(artist, SecurityLimits.MaxArtistNameLength),
            ["album"] = SecurityLimits.Truncate(album, SecurityLimits.MaxAlbumNameLength)
        });
    }

    /// <summary>
    /// Announces a released album to the P2P network.
    /// </summary>
    public void AnnounceAlbumToNetwork(string albumId, string name, string artist)
    {
        if (!P2PIsConnected) return;
        SyncOrchestrator.AnnounceAlbum(albumId, null, new Dictionary<string, string>
        {
            ["name"] = SecurityLimits.Truncate(name, SecurityLimits.MaxAlbumNameLength),
            ["artist"] = SecurityLimits.Truncate(artist, SecurityLimits.MaxArtistNameLength)
        });
    }

    /// <summary>
    /// Updates a released album in the P2P network.
    /// </summary>
    public void UpdateAlbumInNetwork(string albumId, string name, string artist)
    {
        if (!P2PIsConnected) return;
        SyncOrchestrator.UpdateAlbum(albumId, null, new Dictionary<string, string>
        {
            ["name"] = SecurityLimits.Truncate(name, SecurityLimits.MaxAlbumNameLength),
            ["artist"] = SecurityLimits.Truncate(artist, SecurityLimits.MaxArtistNameLength)
        });
    }

    /// <summary>
    /// Call during app shutdown to cleanly stop P2P sync.
    /// </summary>
    public async Task ShutdownAsync()
    {
        PersistPlaybackState(force: true);
        await SyncOrchestrator.StopAsync();
    }

    private async Task ConnectP2PAsync()
    {
        try
        {
            P2PStatusText = "Connecting…";
            var settings = _settingsService.LoadSettings();
            _p2pActAsListener = settings.P2P.ActAsListener;
            var profile = _profileService.LoadProfile();
            var identity = _identityService.LoadOrCreate(profile.DisplayName);
            identity.ManifestPort = settings.P2P.Port;

            var bootstrapNodes = settings.P2P.BootstrapNodes
                .Where(static n => !string.IsNullOrWhiteSpace(n))
                .ToList();

            if (!_p2pActAsListener)
            {
                if (bootstrapNodes.Count == 0)
                {
                    P2PStatusText = "Error: Outbound-only mode requires at least one bootstrap node.";
                    P2PIsConnected = false;
                    return;
                }

                var bootstrapReachable = await CanReachAnyBootstrapAsync(bootstrapNodes);
                if (!bootstrapReachable)
                {
                    P2PStatusText = "Error: Cannot reach configured bootstrap node(s). Check host/port and firewall.";
                    P2PIsConnected = false;
                    return;
                }
            }

            var localManifests = new List<Manifest>();
            foreach (ManifestStreamType streamType in Enum.GetValues(typeof(ManifestStreamType)))
            {
                var m = SyncOrchestrator.LoadLocalManifest(identity.UserId, streamType)
                        ?? _manifestManager.CreateManifest(identity.UserId);
                m.StreamType = streamType;
                localManifests.Add(m);
            }

            _userRepository.RegisterLocalUser(identity.UserId, profile.DisplayName, profile.AvatarIconPath);

            await SyncOrchestrator.StartAsync(
                identity,
                localManifests,
                bootstrapNodes,
                actAsListener: _p2pActAsListener,
                contentProvider: TryGetLocalContentByHash);
            P2PIsConnected = true;
            P2PPeerCount = SyncOrchestrator.ConnectedPeerCount;
            UpdateP2PStatusText();
            PublishReleasedMyMusicToMesh();
        }
        catch (Exception ex)
        {
            P2PStatusText = $"Error: {ex.Message}";
            P2PIsConnected = false;
        }
    }

    private async Task DisconnectP2PAsync()
    {
        await SyncOrchestrator.StopAsync();
        P2PIsConnected = false;
        P2PPeerCount = 0;
        P2PStatusText = "Disconnected";
    }

    private void InitializeP2PAsync()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var settings = _settingsService.LoadSettings();
                if (!settings.P2P.Enabled) return;

                await ConnectP2PAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[P2P] Auto-start error: {ex.Message}");
            }
        });
    }

    private bool HasNewReleaseFromFollowedPeer(string peerUserId)
    {
        if (string.IsNullOrWhiteSpace(peerUserId))
            return false;

        if (!IsPeerFollowed(peerUserId))
            return false;

        var manifest = SyncOrchestrator.GetPeerManifest(peerUserId);
        if (manifest == null)
            return false;

        var latestCreateSequence = manifest.Operations
            .Where(op => op.OperationType == ManifestOperationType.Create)
            .Select(op => op.SequenceNumber)
            .DefaultIfEmpty(0)
            .Max();

        if (latestCreateSequence <= 0)
            return false;

        var lastKnown = _lastKnownReleaseSequenceByPeer.GetValueOrDefault(peerUserId, 0);
        _lastKnownReleaseSequenceByPeer[peerUserId] = latestCreateSequence;
        return latestCreateSequence > lastKnown;
    }

    private bool IsPeerFollowed(string peerUserId)
    {
        var manifest = SyncOrchestrator.GetLocalManifest(ManifestStreamType.Social);
        if (manifest == null)
            return false;

        var latestFollowState = manifest.Operations
            .Where(op => op.TargetType == "User" && string.Equals(op.TargetId, peerUserId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(op => op.SequenceNumber)
            .LastOrDefault(op => op.OperationType == ManifestOperationType.Follow || op.OperationType == ManifestOperationType.Unfollow);

        return latestFollowState?.OperationType == ManifestOperationType.Follow;
    }

    private void UpdateP2PStatusText()
    {
        if (!P2PIsConnected)
        {
            System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                NetworkStatus = NetworkStatus.Offline;
                P2PStatusText = "Disconnected";
                P2PStatusTooltip = "Disconnected from the mesh network.";
                OnPropertyChanged(nameof(P2PStatusColor));
            });
        }
        else if (SyncOrchestrator.MeshPeerCount == 0)
        {
            var bootstrapPeers = SyncOrchestrator.BootstrapPeerCount;
            System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                NetworkStatus = NetworkStatus.Connecting;
                P2PStatusText = "Connecting…";
                P2PStatusTooltip = $"Connecting to {bootstrapPeers} bootstrap nodes...";
                OnPropertyChanged(nameof(P2PStatusColor));
            });
        }
        else
        {
            var meshPeers = SyncOrchestrator.MeshPeerCount;
            var bootstrapPeers = SyncOrchestrator.BootstrapPeerCount;
            var isRestricted = SyncOrchestrator.NatStatus?.Contains("No NAT device discovered", StringComparison.OrdinalIgnoreCase) == true ||
                               SyncOrchestrator.NatStatus?.Contains("error", StringComparison.OrdinalIgnoreCase) == true;

            System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (isRestricted && SyncOrchestrator.ExternalIPAddress == null)
                {
                    NetworkStatus = NetworkStatus.Restricted;
                    P2PStatusText = "Restricted";
                    P2PStatusTooltip = $"Connected to {meshPeers} mesh peers and {bootstrapPeers} bootstrap nodes.\nStrict NAT detected; inbound connections may fail.";
                }
                else
                {
                    NetworkStatus = NetworkStatus.Connected;
                    P2PStatusText = $"Connected · {meshPeers} mesh peer{(meshPeers == 1 ? "" : "s")}";
                    P2PStatusTooltip = $"Connected to {meshPeers} mesh peers and {bootstrapPeers} bootstrap nodes.";
                }
                OnPropertyChanged(nameof(P2PStatusColor));
            });
        }
    }

    private byte[]? TryGetLocalContentByHash(string contentHash)
    {
        if (string.IsNullOrWhiteSpace(contentHash))
            return null;

        try
        {
            var settings = _settingsService.LoadSettings();
            var supportedExtensions = settings.SupportedExtensions
                .Select(static ext => ext.StartsWith('.') ? ext : "." + ext)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var roots = new[]
            {
                _settingsService.GetLocalMusicFolder(),
                _settingsService.GetPeerMusicFolder(),
                Path.Combine(settings.BaseFolder, "UserCache", "Images")
            }
            .Where(static p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

            foreach (var root in roots)
            {
                if (!Directory.Exists(root))
                    continue;

                var isUserCache = root.Contains("UserCache");

                foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
                {
                    if (!isUserCache && !supportedExtensions.Contains(Path.GetExtension(file)))
                        continue;

                    if (isUserCache && !string.Equals(Path.GetExtension(file), ".png", StringComparison.OrdinalIgnoreCase) && !string.Equals(Path.GetExtension(file), ".jpg", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var hash = CryptoService.ComputeFileHash(file);
                    if (!string.Equals(hash, contentHash, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var info = new FileInfo(file);
                    if (!info.Exists || info.Length <= 0)
                        continue;

                    return File.ReadAllBytes(file);
                }
            }
        }
        catch
        {
            // best effort
        }

        return null;
    }

    private static bool TryParseEndpoint(string endpoint, out string host, out int port)
    {
        host = string.Empty;
        port = 0;

        var separator = endpoint.LastIndexOf(':');
        if (separator <= 0)
            return false;

        host = endpoint[..separator];
        return int.TryParse(endpoint[(separator + 1)..], out port) && port > 0 && port < 65536;
    }

    private static async Task<bool> CanReachAnyBootstrapAsync(IEnumerable<string> endpoints)
    {
        foreach (var endpoint in endpoints)
        {
            if (!TryParseEndpoint(endpoint, out var host, out var port))
                continue;

            try
            {
                using var client = new TcpClient();
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1500));
                await client.ConnectAsync(host, port, cts.Token);
                return true;
            }
            catch
            {
                // try next bootstrap endpoint
            }
        }

        return false;
    }

    private void PublishReleasedMyMusicToMesh()
    {
        if (!P2PIsConnected)
            return;

        try
        {
            var metadataService = new MyMusicMetadataService();
            var settings = _settingsService.LoadSettings();
            var myMusicFolder = _settingsService.GetLocalMusicFolder();
            if (!Directory.Exists(myMusicFolder))
                return;

            var manager = new LocalLibraryManager(myMusicFolder, settings.SupportedExtensions);
            manager.IndexLibrary();

            var tracks = manager.GetAllTracks().ToList();
            var albums = manager.GetAllAlbums().ToList();

            foreach (var album in albums)
            {
                if (SyncOrchestrator.LocalManifest?.Operations.Any(op => op.OperationType == ManifestOperationType.Create && op.TargetType == "Album" && string.Equals(op.TargetId, album.AlbumId, StringComparison.OrdinalIgnoreCase)) == true)
                    continue;

                var tracksInAlbum = tracks.Where(t => string.Equals(t.AlbumId, album.AlbumId, StringComparison.OrdinalIgnoreCase)).ToList();
                var firstPath = tracksInAlbum.Select(t => t.FilePath).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
                if (string.IsNullOrWhiteSpace(firstPath))
                    continue;

                var albumFolder = Path.GetDirectoryName(firstPath) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(albumFolder))
                    continue;

                var albumMeta = metadataService.LoadForAlbum(albumFolder);
                if (!albumMeta.IsReleased)
                    continue;

                var coverPath = manager.GetTrackCoverPath(firstPath);
                string? coverHash = null;
                string? iconHash = null;
                if (!string.IsNullOrWhiteSpace(coverPath) && File.Exists(coverPath))
                {
                    coverHash = CryptoService.ComputeFileHash(coverPath);
                    var iconBytes = metadataService.CreateIcon(coverPath);
                    if (iconBytes != null)
                    {
                        iconHash = CryptoService.ComputeHash(iconBytes);
                        _userRepository.SaveUserIcon(album.AlbumId, iconBytes);
                    }
                }

                var artistName = tracksInAlbum.FirstOrDefault()?.Description ?? string.Empty;
                var metadata = new Dictionary<string, string>
                {
                    ["name"] = SecurityLimits.Truncate(album.Title, SecurityLimits.MaxAlbumNameLength),
                    ["artist"] = SecurityLimits.Truncate(artistName, SecurityLimits.MaxArtistNameLength)
                };
                if (!string.IsNullOrWhiteSpace(iconHash))
                    metadata["iconHash"] = iconHash;

                SyncOrchestrator.AnnounceAlbum(album.AlbumId, coverHash, metadata);
            }

            foreach (var track in tracks)
            {
                if (SyncOrchestrator.LocalManifest?.Operations.Any(op => op.OperationType == ManifestOperationType.Create && op.TargetType == "Track" && string.Equals(op.TargetId, track.TrackId, StringComparison.OrdinalIgnoreCase)) == true)
                    continue;

                if (string.IsNullOrWhiteSpace(track.FilePath) || !File.Exists(track.FilePath))
                    continue;

                var trackMeta = metadataService.LoadForTrack(track.FilePath);
                if (!trackMeta.IsReleased)
                    continue;

                var coverPath = manager.GetTrackCoverPath(track.FilePath);
                string? iconHash = null;
                if (!string.IsNullOrWhiteSpace(coverPath) && File.Exists(coverPath))
                {
                    var iconBytes = metadataService.CreateIcon(coverPath);
                    if (iconBytes != null)
                    {
                        iconHash = CryptoService.ComputeHash(iconBytes);
                        _userRepository.SaveUserIcon(track.TrackId, iconBytes);
                    }
                }

                var albumTitle = albums.FirstOrDefault(a => string.Equals(a.AlbumId, track.AlbumId, StringComparison.OrdinalIgnoreCase))?.Title ?? string.Empty;
                var metadata = new Dictionary<string, string>
                {
                    ["title"] = SecurityLimits.Truncate(track.Title, SecurityLimits.MaxTrackTitleLength),
                    ["artist"] = SecurityLimits.Truncate(track.Description ?? string.Empty, SecurityLimits.MaxArtistNameLength),
                    ["album"] = SecurityLimits.Truncate(albumTitle, SecurityLimits.MaxAlbumNameLength)
                };
                if (!string.IsNullOrWhiteSpace(iconHash))
                    metadata["iconHash"] = iconHash;

                SyncOrchestrator.AnnounceTrack(track.TrackId, CryptoService.ComputeFileHash(track.FilePath), metadata);
            }
        }
        catch
        {
            // best-effort publish for diagnostics and availability
        }
    }

    private void RestorePlaybackState(AppSettings settings)
    {
        var resume = settings.Playback.ResumeState;
        if (resume == null || string.IsNullOrWhiteSpace(resume.TrackFilePath))
            return;

        Playback.RestoreFromResumeState(resume);
    }

    public void PersistPlaybackState(bool force = false)
    {
        if (!force && !_resumeStateDirty)
            return;

        var settings = _settingsService.LoadSettings();

        if (Playback.HasTrackLoaded)
            settings.Playback.ResumeState = Playback.BuildResumeState();
        else
            settings.Playback.ResumeState = new PlaybackResumeState();

        _settingsService.SaveSettings(settings);
        _resumeStateDirty = false;
    }
}

public enum NetworkStatus
{
    Offline,
    Connecting,
    Connected,
    Restricted
}
