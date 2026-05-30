using MeshWave.Mvvm;
using MeshWave.Services;
using MeshWave.Synchronizer;

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

    public ApplicationViewModel()
    {
        _playbackViewModel = new PlaybackViewModel();
        _currentViewModel = new HomeViewModel();

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

    public SyncOrchestrator SyncOrchestrator => _syncOrchestrator;

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
        CurrentViewModel = new SettingsViewModel();
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
    /// Call during app shutdown to cleanly stop P2P sync.
    /// </summary>
    public async Task ShutdownAsync()
    {
        await _syncOrchestrator.StopAsync();
    }

    private void InitializeP2PAsync()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var settings = _settingsService.LoadSettings();
                if (!settings.P2P.Enabled) return;

                var profile = _profileService.LoadProfile();
                var identity = _identityService.LoadOrCreate(profile.DisplayName);
                identity.ManifestPort = settings.P2P.Port;

                _localManifest = _manifestManager.CreateManifest(identity.UserId);

                _syncOrchestrator.ManifestMerged += (_, e) =>
                {
                    // Post to UI thread if needed for future dashboard updates
                    System.Diagnostics.Debug.WriteLine($"[P2P] Merged {e.OperationsAdded} ops from {e.UserId}");
                };

                await _syncOrchestrator.StartAsync(
                    identity,
                    _localManifest,
                    settings.P2P.BootstrapNodes);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[P2P] Startup error: {ex.Message}");
            }
        });
    }
}
