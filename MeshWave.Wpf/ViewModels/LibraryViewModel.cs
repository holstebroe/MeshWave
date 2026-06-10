using System.Collections.Specialized;
using System.IO;
using System.Windows.Input;
using MeshWave.Common.Core.Crypto;
using MeshWave.Wpf.Mvvm;
using MeshWave.Wpf.Services;

namespace MeshWave.Wpf.ViewModels;

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
    private readonly LibraryDownloadStateService _downloadStateService = new();

    public LibraryViewModel(ApplicationViewModel applicationViewModel, bool isMyMusicLibrary = false)
    {
        _applicationViewModel = applicationViewModel;
        _settingsService = applicationViewModel.SettingsService;
        IsMyMusicLibrary = isMyMusicLibrary;
        CancelImportCommand = new RelayCommand(_ => CancelImport(), _ => IsImporting);
        SyncAlbumCommand = new RelayCommand(_ => SyncSelectedAlbum(), _ => CanSyncToNetwork);
        SyncTrackCommand = new RelayCommand<LibraryTrackItem>(SyncTrack, t => CanSyncToNetwork && t != null);
        RemoveTrackFromLibraryCommand = new RelayCommand<LibraryTrackItem>(RemoveTrackFromLibrary, t => t != null && !IsMyMusicLibrary && !t.IsDownloadPlaceholder);
        ReDownloadTrackCommand = new RelayCommand<LibraryTrackItem>(ReDownloadTrack, t => t != null && ((!IsMyMusicLibrary && t.IsRemovedFromLibrary) || (IsMyMusicLibrary && !t.IsDownloaded)) && !string.IsNullOrWhiteSpace(t.ContentHash));
        if (!IsMyMusicLibrary && _applicationViewModel != null) _applicationViewModel.DownloadQueueItems.CollectionChanged += OnDownloadQueueChanged;

        LoadFromConfiguredBaseFolder();
    }

    public ICommand CancelImportCommand { get; }
    public ICommand SyncAlbumCommand { get; }
    public ICommand SyncTrackCommand { get; }
    public ICommand RemoveTrackFromLibraryCommand { get; }
    public ICommand ReDownloadTrackCommand { get; }

    public bool IsMyMusicLibrary { get; }
    public bool CanImportMyMusic => IsMyMusicLibrary;

    /// <summary>
    /// True when the user is in Local Music view and P2P is connected.
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
            if (SetProperty(ref _isImporting, value)) OnPropertyChanged(nameof(CanCancelImport));
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
        if (string.IsNullOrWhiteSpace(playbackPath)) return;

        var playbackContext = GetCurrentPlaybackContext(track!);
        _applicationViewModel?.PlayTrack(
            track!.Title,
            track!.Description ?? "Unknown Artist",
            track!.Duration,
            playbackPath,
            playbackContext,
            track!.TrackId,
            SelectedAlbum?.Name,
            SelectedAlbum?.CoverPath);
    }

    public event EventHandler<string>? OpenMetadataEditorRequested;

    public void RequestOpenMetadataEditor(string trackFilePath)
    {
        if (!CanImportMyMusic || string.IsNullOrWhiteSpace(trackFilePath)) return;

        OpenMetadataEditorRequested?.Invoke(this, trackFilePath);
    }

    private void SyncSelectedAlbum()
    {
        var album = SelectedAlbum;
        if (album == null || _applicationViewModel == null) return;

        _applicationViewModel.AnnounceAlbumToNetwork(album.AlbumId, album.Name, album.Artist);

        foreach (var track in Tracks.Where(t => t.IsReleased))
            _applicationViewModel.AnnounceTrackToNetwork(
                track.TrackId,
                CryptoService.ComputeFileHash(track.FilePath),
                track.Title,
                track.Artist,
                album.Name);

        var count = Tracks.Count(t => t.IsReleased);
        SyncStatus = $"Announced album '{album.Name}' with {count} released track(s) to the network.";
    }

    private void SyncTrack(LibraryTrackItem? track)
    {
        if (track == null || _applicationViewModel == null) return;

        _applicationViewModel.AnnounceTrackToNetwork(
            track.TrackId,
            CryptoService.ComputeFileHash(track.FilePath),
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

    private void RemoveTrackFromLibrary(LibraryTrackItem? track)
    {
        if (track == null || IsMyMusicLibrary)
            return;

        if (!track.IsDownloadPlaceholder && !string.IsNullOrWhiteSpace(track.ContentHash))
            _downloadStateService.MarkRemoved(new RemovedLibraryTrackEntry
            {
                ContentHash = track.ContentHash,
                TrackId = track.TrackId,
                Title = track.Title,
                Artist = track.Artist,
                Album = track.AlbumName,
                AlbumId = track.AlbumId,
                PeerUserId = track.SourcePeerUserId
            });

        if (!string.IsNullOrWhiteSpace(track.FilePath) && File.Exists(track.FilePath))
            try
            {
                File.Delete(track.FilePath);
                var folder = Path.GetDirectoryName(track.FilePath);
                if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                    Directory.Delete(folder, recursive: false);
            }
            catch
            {
                // best-effort delete
            }

        LoadFromConfiguredBaseFolder();
    }

    private void ReDownloadTrack(LibraryTrackItem? track)
    {
        if (track == null)
            return;

        QueueTrackRedownload(track);
    }

    private void OnDownloadQueueChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null)
            dispatcher.Invoke(RefreshAlbumAndTrackSelection);
        else
            RefreshAlbumAndTrackSelection();
    }
}

public sealed class LibraryArtistItem
{
    public required string Name { get; set; }
    public required string CoverPath { get; set; }
    public int AlbumCount { get; set; }
    public int TrackCount { get; set; }
    public override string ToString()
    {
        return Name;
    }
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
    public int PendingDownloadCount { get; set; }
    public int DownloadingCount { get; set; }
    public int FailedDownloadCount { get; set; }
    public bool HasDownloadActivity => PendingDownloadCount > 0 || DownloadingCount > 0 || FailedDownloadCount > 0;
    public string DownloadStatusBadge => HasDownloadActivity
        ? $"Pending {PendingDownloadCount} · Downloading {DownloadingCount} · Failed {FailedDownloadCount}"
        : string.Empty;
    public string ReleaseBadgeColor => IsReleased ? "#27AE60" : "#E67E22"; // Green for Public, Orange for Private
    public string VersionLabel => Version > 1 ? $"v{Version}" : string.Empty;
    public override string ToString()
    {
        return Name;
    }
}

public sealed class LibraryTrackItem
{
    public required string TrackId { get; set; }
    public required string Title { get; set; }
    public required string Artist { get; set; }
    public required string AlbumId { get; set; }
    public string AlbumName { get; set; } = string.Empty;
    public required string CoverPath { get; set; }
    public required string FilePath { get; set; }
    public string? ContentHash { get; set; }
    public string SourcePeerUserId { get; set; } = string.Empty;
    public bool IsReleased { get; set; }
    public int Version { get; set; } = 1;
    public int TrackNumber { get; set; }
    public TimeSpan Duration { get; set; }
    public int PlayCount { get; set; }
    public bool IsDownloadPlaceholder { get; set; }
    public bool IsRemovedFromLibrary { get; set; }
    public bool IsDownloaded { get; set; } = true;
    public string DownloadStateLabel { get; set; } = "Downloaded";
    public string StatusBadge => IsRemovedFromLibrary || !IsDownloaded ? "Not Downloaded" : IsDownloadPlaceholder ? DownloadStateLabel : string.Empty;
    public string ReleaseBadgeColor => IsReleased ? "#27AE60" : "#E67E22"; // Green for Public, Orange for Private
    public string VersionLabel => Version > 1 ? $"v{Version}" : string.Empty;
    public bool CanPlay => !IsDownloadPlaceholder && !IsRemovedFromLibrary && IsDownloaded && !string.IsNullOrWhiteSpace(FilePath);
    public override string ToString()
    {
        return Title;
    }
}
