using System.Windows.Input;
using MeshWave.Mvvm;

namespace MeshWave.ViewModels;

/// <summary>
/// View model for library browsing and playback.
/// </summary>
public partial class LibraryViewModel : ViewModelBase
{
    private readonly ApplicationViewModel? _applicationViewModel;
    private string _searchQuery = string.Empty;
    private List<LibraryTrackItem> _tracks = [];
    private List<LibraryAlbumItem> _albums = [];
    private List<LibraryArtistItem> _artists = [];
    private LibraryArtistItem? _selectedArtist;
    private LibraryAlbumItem? _selectedAlbum;
    private bool _isImporting;
    private string _importCurrentFile = string.Empty;
    private string _importStatusMessage = "Idle";
    private int _importTotalFiles;
    private int _importRemainingFiles;
    private int _importImportedFiles;
    private string _importSingleFileStatus = string.Empty;
    private string _syncStatus = string.Empty;

    public LibraryViewModel(ApplicationViewModel? applicationViewModel = null, bool isMyMusicLibrary = false)
    {
        _applicationViewModel = applicationViewModel;
        IsMyMusicLibrary = isMyMusicLibrary;
        CancelImportCommand = new RelayCommand(_ => CancelImport(), _ => IsImporting);
        SyncAlbumCommand = new RelayCommand(_ => SyncSelectedAlbum(), _ => CanSyncToNetwork);
        SyncTrackCommand = new RelayCommand<LibraryTrackItem>(SyncTrack, t => CanSyncToNetwork && t != null);
        LoadFromConfiguredBaseFolder();
    }

    public ICommand CancelImportCommand { get; }
    public ICommand SyncAlbumCommand { get; }
    public ICommand SyncTrackCommand { get; }

    public bool IsMyMusicLibrary { get; }
    public bool CanImportMyMusic => IsMyMusicLibrary;

    /// <summary>
    /// True when the user is in My Music view and P2P is connected.
    /// </summary>
    public bool CanSyncToNetwork => IsMyMusicLibrary && (_applicationViewModel?.P2PIsConnected ?? false);

    public string SyncStatus
    {
        get => _syncStatus;
        set => SetProperty(ref _syncStatus, value);
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set => SetProperty(ref _searchQuery, value);
    }

    public List<LibraryTrackItem> Tracks
    {
        get => _tracks;
        set => SetProperty(ref _tracks, value);
    }

    public List<LibraryAlbumItem> Albums
    {
        get => _albums;
        set => SetProperty(ref _albums, value);
    }

    public List<LibraryArtistItem> Artists
    {
        get => _artists;
        set => SetProperty(ref _artists, value);
    }

    public LibraryArtistItem? SelectedArtist
    {
        get => _selectedArtist;
        set
        {
            if (SetProperty(ref _selectedArtist, value))
            {
                SelectedAlbum = null;
                RefreshAlbumAndTrackSelection();
            }
        }
    }

    public LibraryAlbumItem? SelectedAlbum
    {
        get => _selectedAlbum;
        set
        {
            if (SetProperty(ref _selectedAlbum, value))
            {
                RefreshAlbumAndTrackSelection();
                OnPropertyChanged(nameof(CanSyncToNetwork));
            }
        }
    }

    public bool IsImporting
    {
        get => _isImporting;
        set
        {
            if (SetProperty(ref _isImporting, value))
            {
                OnPropertyChanged(nameof(CanCancelImport));
            }
        }
    }

    public string ImportCurrentFile
    {
        get => _importCurrentFile;
        set => SetProperty(ref _importCurrentFile, value);
    }

    public string ImportStatusMessage
    {
        get => _importStatusMessage;
        set => SetProperty(ref _importStatusMessage, value);
    }

    public int ImportTotalFiles
    {
        get => _importTotalFiles;
        set => SetProperty(ref _importTotalFiles, value);
    }

    public int ImportRemainingFiles
    {
        get => _importRemainingFiles;
        set => SetProperty(ref _importRemainingFiles, value);
    }

    public int ImportImportedFiles
    {
        get => _importImportedFiles;
        set => SetProperty(ref _importImportedFiles, value);
    }

    public double ImportProgressPercent => ImportTotalFiles == 0
        ? 0
        : Math.Clamp((double)(ImportTotalFiles - ImportRemainingFiles) / ImportTotalFiles * 100.0, 0, 100);

    public bool CanCancelImport => IsImporting;

    public string ImportSingleFileStatus
    {
        get => _importSingleFileStatus;
        set => SetProperty(ref _importSingleFileStatus, value);
    }

    public void PlayTrackById(string trackId)
    {
        var track = GetTrackById(trackId);
        var playbackPath = track?.FilePath;
        if (string.IsNullOrWhiteSpace(playbackPath))
        {
            return;
        }

        var playbackContext = GetCurrentPlaybackContext(track);
        _applicationViewModel?.PlayTrack(
            track.Title,
            track.Description ?? "Unknown Artist",
            track.Duration,
            playbackPath,
            playbackContext,
            track.TrackId,
            SelectedAlbum?.Name,
            SelectedAlbum?.CoverPath);
    }

    public event EventHandler<string>? OpenMetadataEditorRequested;

    public void RequestOpenMetadataEditor(string trackFilePath)
    {
        if (!CanImportMyMusic || string.IsNullOrWhiteSpace(trackFilePath))
        {
            return;
        }

        OpenMetadataEditorRequested?.Invoke(this, trackFilePath);
    }

    private void SyncSelectedAlbum()
    {
        var album = SelectedAlbum;
        if (album == null || _applicationViewModel == null) return;

        _applicationViewModel.AnnounceAlbumToNetwork(album.AlbumId, album.Name, album.Artist);

        foreach (var track in Tracks.Where(t => t.IsReleased))
        {
            _applicationViewModel.AnnounceTrackToNetwork(
                track.TrackId,
                MeshWave.Common.Core.Crypto.CryptoService.ComputeFileHash(track.FilePath),
                track.Title,
                track.Artist,
                album.Name);
        }

        var count = Tracks.Count(t => t.IsReleased);
        SyncStatus = $"Announced album '{album.Name}' with {count} released track(s) to the network.";
    }

    private void SyncTrack(LibraryTrackItem? track)
    {
        if (track == null || _applicationViewModel == null) return;

        _applicationViewModel.AnnounceTrackToNetwork(
            track.TrackId,
            MeshWave.Common.Core.Crypto.CryptoService.ComputeFileHash(track.FilePath),
            track.Title,
            track.Artist,
            SelectedAlbum?.Name ?? string.Empty);

        SyncStatus = $"Announced '{track.Title}' to the network.";
    }

    public void Search()
    {
        // TODO: Implement library search
    }

    public void RefreshLibrary()
    {
        // TODO: Implement library refresh
    }
}

public sealed class LibraryArtistItem
{
    public required string Name { get; set; }
    public required string CoverPath { get; set; }
    public int AlbumCount { get; set; }
    public int TrackCount { get; set; }
    public override string ToString() => Name;
}

public sealed class LibraryAlbumItem
{
    public required string AlbumId { get; set; }
    public required string Artist { get; set; }
    public required string Name { get; set; }
    public required string CoverPath { get; set; }
    public int TrackCount { get; set; }
    public bool IsReleased { get; set; }
    public int Version { get; set; } = 1;
    public string ReleaseBadge => IsReleased ? "Public" : "Private";
    public override string ToString() => Name;
}

public sealed class LibraryTrackItem
{
    public required string TrackId { get; set; }
    public required string Title { get; set; }
    public required string Artist { get; set; }
    public required string AlbumId { get; set; }
    public required string CoverPath { get; set; }
    public required string FilePath { get; set; }
    public bool IsReleased { get; set; }
    public int Version { get; set; } = 1;
    public int TrackNumber { get; set; }
    public TimeSpan Duration { get; set; }
    public int PlayCount { get; set; }
    public string ReleaseBadge => IsReleased ? "Public" : "Private";
    public override string ToString() => Title;
}
