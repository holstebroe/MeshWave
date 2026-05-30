using MeshWave.Mvvm;
using MeshWave.Common.Core.Models;
using MeshWave.Services;
using MeshWave.Synchronizer;
using System.Collections.Generic;
using System.Windows.Input;

namespace MeshWave.ViewModels;

/// <summary>
/// Main application view model.
/// Manages overall application state, navigation, and P2P sync lifecycle.
/// </summary>
public class ApplicationViewModel : ViewModelBase
{
    private string _applicationTitle = "MeshWave";
    private ViewModelBase _currentViewModel;
    private readonly PlaybackViewModel _playbackViewModel;

    private readonly SettingsService _settingsService = new();
    private readonly UserProfileService _profileService = new();
    private readonly P2PIdentityService _identityService = new();
    private readonly ManifestManager _manifestManager = new();
    private readonly SyncOrchestrator _syncOrchestrator = new();

    private MeshWave.Common.Core.Models.Manifest? _localManifest;
    private bool _p2pIsConnected;
    private string _p2pStatusText = "Disconnected";
    private int _p2pPeerCount;
    private readonly Dictionary<string, int> _lastKnownReleaseSequenceByPeer = new(StringComparer.OrdinalIgnoreCase);

    public ApplicationViewModel()
    {
        _playbackViewModel = new PlaybackViewModel(_syncOrchestrator);
        _currentViewModel = new HomeViewModel();

        // Apply persisted waveform style immediately
        var savedSettings = _settingsService.LoadSettings();
        if (Enum.TryParse<WaveformStyle>(savedSettings.Playback.WaveformStyle, out var savedStyle))
            _playbackViewModel.WaveformStyle = savedStyle;

        ConnectP2PCommand = new RelayCommand(_ => _ = ConnectP2PAsync(), _ => !P2PIsConnected);
        DisconnectP2PCommand = new RelayCommand(_ => _ = DisconnectP2PAsync(), _ => P2PIsConnected);

        _syncOrchestrator.PeerCountChanged += (_, _) =>
        {
            P2PPeerCount = _syncOrchestrator.ConnectedPeerCount;
            P2PStatusText = $"Connected · {P2PPeerCount} peer{(P2PPeerCount == 1 ? "" : "s")}";
        };

        _syncOrchestrator.ManifestMerged += (_, e) =>
        {
            if (CurrentViewModel is CommunityViewModel)
                return;

            if (HasNewReleaseFromFollowedPeer(e.UserId))
                HasCommunityNotification = true;
        };

        // Record a signed Play operation the first time each track starts playing.
        _playbackViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PlaybackViewModel.IsPlaying) && _playbackViewModel.IsPlaying)
            {
                _syncOrchestrator.RecordPlay(
                    _playbackViewModel.CurrentTrackId,
                    _playbackViewModel.TrackTitle,
                    _playbackViewModel.Artist);
            }
        };

        InitializeP2PAsync();
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

    public int P2PPeerCount
    {
        get => _p2pPeerCount;
        private set => SetProperty(ref _p2pPeerCount, value);
    }

    public ICommand ConnectP2PCommand { get; }
    public ICommand DisconnectP2PCommand { get; }

    public SyncOrchestrator SyncOrchestrator => _syncOrchestrator;
    public PlaybackViewModel Playback => _playbackViewModel;

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
        CurrentViewModel = new SettingsViewModel(style => _playbackViewModel.WaveformStyle = style, _syncOrchestrator);
    }

    public void NavigateToBrowse()
    {
        CurrentViewModel = new BrowseViewModel();
    }

    private bool _hasCommunityNotification;

    public bool HasCommunityNotification
    {
        get => _hasCommunityNotification;
        private set => SetProperty(ref _hasCommunityNotification, value);
    }

    public void NavigateToCommunity()
    {
        var vm = new CommunityViewModel(_syncOrchestrator);
        // Forward badge state to the shell so the nav button can show the dot.
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CommunityViewModel.HasNewReleases))
                HasCommunityNotification = vm.HasNewReleases;
        };
        HasCommunityNotification = false;   // clear on open
        CurrentViewModel = vm;
    }

    public void NavigateToPlayback()
    {
        CurrentViewModel = _playbackViewModel;
    }

    public void PlayTrack(string trackTitle, string artist, TimeSpan duration, string filePath, IEnumerable<PlaybackTrackListItem>? contextTracks = null, string? selectedTrackId = null, string? contextTitle = null, string? contextIconPath = null)
    {
        if (contextTracks != null)
        {
            _playbackViewModel.SetAlbumTrackContext(contextTracks, selectedTrackId, contextTitle, contextIconPath);
        }

        _playbackViewModel.Stop();
        _playbackViewModel.LoadTrack(trackTitle, artist, duration, filePath);
        CurrentViewModel = _playbackViewModel;
    }

    /// <summary>
    /// Announces a released track to the P2P network.
    /// </summary>
    public void AnnounceTrackToNetwork(string trackId, string contentHash, string title, string artist, string album)
    {
        if (!P2PIsConnected) return;
        _syncOrchestrator.AnnounceTrack(trackId, contentHash, new Dictionary<string, string>
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
        _syncOrchestrator.AnnounceAlbum(albumId, null, new Dictionary<string, string>
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
        await _syncOrchestrator.StopAsync();
    }

    private async Task ConnectP2PAsync()
    {
        try
        {
            P2PStatusText = "Connecting…";
            var settings = _settingsService.LoadSettings();
            var profile = _profileService.LoadProfile();
            var identity = _identityService.LoadOrCreate(profile.DisplayName);
            identity.ManifestPort = settings.P2P.Port;

            _localManifest ??= _manifestManager.CreateManifest(identity.UserId);

            await _syncOrchestrator.StartAsync(identity, _localManifest, settings.P2P.BootstrapNodes);
            P2PIsConnected = true;
            P2PStatusText = "Connected · 0 peers";
        }
        catch (Exception ex)
        {
            P2PStatusText = $"Error: {ex.Message}";
            P2PIsConnected = false;
        }
    }

    private async Task DisconnectP2PAsync()
    {
        await _syncOrchestrator.StopAsync();
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
                System.Diagnostics.Debug.WriteLine($"[P2P] Auto-start error: {ex.Message}");
            }
        });
    }

    private bool HasNewReleaseFromFollowedPeer(string peerUserId)
    {
        if (string.IsNullOrWhiteSpace(peerUserId))
            return false;

        if (!IsPeerFollowed(peerUserId))
            return false;

        var manifest = _syncOrchestrator.GetPeerManifest(peerUserId);
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
        if (_localManifest == null)
            return false;

        var latestFollowState = _localManifest.Operations
            .Where(op => op.TargetType == "User" && string.Equals(op.TargetId, peerUserId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(op => op.SequenceNumber)
            .LastOrDefault(op => op.OperationType == ManifestOperationType.Follow || op.OperationType == ManifestOperationType.Unfollow);

        return latestFollowState?.OperationType == ManifestOperationType.Follow;
    }
}
