using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows.Input;
using MeshWave.Common.Core.Models;
using MeshWave.Synchronizer;
using MeshWave.Wpf.Mvvm;
using MeshWave.Wpf.Services;

namespace MeshWave.Wpf.ViewModels;

public enum BrowseTab { Artists, Albums, Tracks, Playlists, Downloads }

// ─────────────────────────────────────────────────────────────────────────
// Data items
// ─────────────────────────────────────────────────────────────────────────

public class BrowseArtistItem : ViewModelBase
{
    private string _avatarIconPath = string.Empty;
    private string _iconPath = string.Empty;
    private bool _isLocal;

    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string AvatarIconPath
    {
        get => _avatarIconPath;
        set => SetProperty(ref _avatarIconPath, value);
    }
    public string IconPath
    {
        get => _iconPath;
        set => SetProperty(ref _iconPath, value);
    }
    public string Bio { get; set; } = string.Empty;
    public int TrackCount { get; set; }
    public int AlbumCount { get; set; }
    public long TotalSize { get; set; }
    public string TotalSizeDisplay { get; set; } = string.Empty;
    public bool IsLocal
    {
        get => _isLocal;
        set => SetProperty(ref _isLocal, value);
    }
}

public class BrowseAlbumItem : ViewModelBase
{
    private string _iconPath = string.Empty;
    private bool _isLocal;

    public string AlbumId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ArtistUserId { get; set; } = string.Empty;
    public string ArtistDisplayName { get; set; } = string.Empty;
    public int TrackCount { get; set; }
    public long TotalSize { get; set; }
    public string TotalSizeDisplay { get; set; } = string.Empty;
    public DateTime? ReleasedAt { get; set; }
    public string ReleasedAtDisplay => ReleasedAt.HasValue ? ReleasedAt.Value.ToLocalTime().ToString("MMM yyyy") : string.Empty;
    public string IconPath
    {
        get => _iconPath;
        set => SetProperty(ref _iconPath, value);
    }
    public bool IsLocal
    {
        get => _isLocal;
        set => SetProperty(ref _isLocal, value);
    }
}

public class BrowsePlaylistItem : ViewModelBase
{
    private string _iconPath = string.Empty;
    private bool _isLocal;

    public string PlaylistId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ArtistUserId { get; set; } = string.Empty;
    public string ArtistDisplayName { get; set; } = string.Empty;
    public int TrackCount { get; set; }
    public List<string> TrackIds { get; set; } = [];
    public DateTime? ReleasedAt { get; set; }
    public string ReleasedAtDisplay => ReleasedAt.HasValue ? ReleasedAt.Value.ToLocalTime().ToString("MMM d, yyyy") : string.Empty;
    public string IconPath
    {
        get => _iconPath;
        set => SetProperty(ref _iconPath, value);
    }
    public bool IsLocal
    {
        get => _isLocal;
        set => SetProperty(ref _isLocal, value);
    }
}

public class BrowseTrackItem : ViewModelBase
{
    private bool _isQueued;
    private bool _isDownloaded;
    private bool _isLocal;
    private bool _needsUpdate;
    private string _iconPath = string.Empty;

    public string TrackId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ArtistUserId { get; set; } = string.Empty;
    public string ArtistDisplayName { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    public string? ContentHash { get; set; }
    public long FileSize { get; set; }
    public string FileSizeDisplay { get; set; } = string.Empty;
    public DateTime? ReleasedAt { get; set; }
    public string ReleasedAtDisplay => ReleasedAt.HasValue ? ReleasedAt.Value.ToLocalTime().ToString("MMM d, yyyy") : string.Empty;

    public bool IsQueued
    {
        get => _isQueued;
        set
        {
            SetProperty(ref _isQueued, value);
            OnPropertyChanged(nameof(DownloadButtonLabel));
            OnPropertyChanged(nameof(CanDownload));
        }
    }

    public bool IsDownloaded
    {
        get => _isDownloaded;
        set
        {
            SetProperty(ref _isDownloaded, value);
            OnPropertyChanged(nameof(DownloadButtonLabel));
            OnPropertyChanged(nameof(CanDownload));
        }
    }

    public bool IsLocal
    {
        get => _isLocal;
        set
        {
            SetProperty(ref _isLocal, value);
            OnPropertyChanged(nameof(DownloadButtonLabel));
            OnPropertyChanged(nameof(CanDownload));
        }
    }

    public bool NeedsUpdate
    {
        get => _needsUpdate;
        set
        {
            SetProperty(ref _needsUpdate, value);
            OnPropertyChanged(nameof(DownloadButtonLabel));
            OnPropertyChanged(nameof(CanDownload));
        }
    }

    public string IconPath
    {
        get => _iconPath;
        set => SetProperty(ref _iconPath, value);
    }

    public string DownloadButtonLabel =>
        IsLocal ? "Local" :
        IsQueued ? "⏳ Queued" :
        NeedsUpdate ? "Update Available" :
        IsDownloaded ? "✅ Downloaded" :
        "⬇ Download";

    public bool CanDownload => !IsLocal && !IsQueued && (!IsDownloaded || NeedsUpdate);
}

// ─────────────────────────────────────────────────────────────────────────
// BrowseViewModel
// ─────────────────────────────────────────────────────────────────────────

/// <summary>
/// View model for browsing community music from the P2P network.
/// Provides Artists / Albums / Tracks / Downloads sub-tabs.
/// </summary>
public class BrowseViewModel : ViewModelBase
{
    private readonly ISyncBrowseClient? _sync;
    private readonly DownloadQueueService _downloadQueue;
    private readonly SettingsService _settingsService;
    private readonly LibraryDownloadStateService _downloadState;

    private BrowseTab _activeTab = BrowseTab.Artists;
    private string _statusText = "Connect to the Mesh network to discover community music.";
    private string _filterText = string.Empty;
    private string? _activeArtistUserId;

    private ObservableCollection<BrowseArtistItem> _artists = [];
    private ObservableCollection<BrowseAlbumItem> _albums = [];
    private ObservableCollection<BrowseTrackItem> _tracks = [];
    private ObservableCollection<BrowsePlaylistItem> _playlists = [];

    public BrowseViewModel(
        ISyncBrowseClient? sync = null,
        DownloadQueueService? downloadQueue = null,
        Action<string, string, TimeSpan, string, long>? onPlayRemote = null,
        SettingsService? settingsService = null,
        LibraryDownloadStateService? downloadState = null)
    {
        _sync = sync;
        _downloadQueue = downloadQueue ?? new DownloadQueueService();
        _settingsService = settingsService ?? new SettingsService();
        _downloadState = downloadState ?? new LibraryDownloadStateService();

        SetTabCommand = new RelayCommand<string>(tab =>
        {
            if (Enum.TryParse<BrowseTab>(tab, out var parsed))
                ActiveTab = parsed;
        });

        ViewArtistCommand = new RelayCommand<BrowseArtistItem>(a =>
        {
            if (a != null) NavigateToArtist(a.UserId);
        }, a => a != null);

        DownloadTrackCommand = new RelayCommand<BrowseTrackItem>(EnqueueTrackDownload,
            t => t != null && !string.IsNullOrWhiteSpace(t.ContentHash) && !t.IsQueued);

        PlayRemoteTrackCommand = new RelayCommand<BrowseTrackItem>(async t =>
        {
            if (t == null || string.IsNullOrWhiteSpace(t.ContentHash)) return;
            if (_sync == null) return;

            // Trigger download + stream playback
            _settingsService.EnsureFoldersExist();
            var tempRoot = Path.Combine(Path.GetTempPath(), "MeshWave", "Streaming");
            Directory.CreateDirectory(tempRoot);

            var extension = ".mp3"; // default
            if (!string.IsNullOrWhiteSpace(t.Title) && Path.HasExtension(t.Title))
                extension = Path.GetExtension(t.Title);

            var tempPath = Path.Combine(tempRoot, t.ContentHash + extension);

            // If it already exists in Library, just play it
            // (A more robust check would be to see if it's already fully downloaded)

            var (stream, length) = await _sync.RequestContentStreamAsync(t.ArtistUserId, t.ContentHash);
            if (stream == null) return;

            // Start writing to temp file in background
            _ = Task.Run(async () =>
            {
                try
                {
                    using (stream)
                    using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                    {
                        await stream.CopyToAsync(fs);
                    }
                }
                catch { }
            });

            // Signal to ApplicationViewModel to load this into PlaybackViewModel
            // Attempt to resolve duration if the manifest provides it, or use a 3-min default for remote
            var duration = TimeSpan.FromMinutes(3);
            onPlayRemote?.Invoke(t.Title, t.ArtistDisplayName, duration, tempPath, length);
        }, t => t != null && !string.IsNullOrWhiteSpace(t.ContentHash));

        DownloadArtistCommand = new RelayCommand<BrowseArtistItem>(a =>
        {
            if (a == null) return;
            var artistTracks = Tracks.Where(t => string.Equals(t.ArtistUserId, a.UserId, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var track in artistTracks)
                EnqueueTrackDownload(track);
        }, a => a != null);

        DownloadAlbumCommand = new RelayCommand<BrowseAlbumItem>(album =>
        {
            if (album == null) return;
            var albumTracks = Tracks.Where(t => string.Equals(t.ArtistUserId, album.ArtistUserId, StringComparison.OrdinalIgnoreCase)
                                            && string.Equals(t.Album, album.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var track in albumTracks)
                EnqueueTrackDownload(track);
        }, album => album != null);

        DownloadPlaylistCommand = new RelayCommand<BrowsePlaylistItem>(p =>
        {
            if (p == null) return;
            foreach (var trackId in p.TrackIds)
            {
                var track = Tracks.FirstOrDefault(t => string.Equals(t.TrackId, trackId, StringComparison.OrdinalIgnoreCase));
                if (track != null) EnqueueTrackDownload(track);
            }
        }, p => p != null);

        CancelDownloadCommand = new RelayCommand<DownloadQueueItem>(item =>
        {
            if (item != null) _downloadQueue.Remove(item);
        }, item => item != null && item.IsPending);

        RetryDownloadCommand = new RelayCommand<DownloadQueueItem>(item =>
        {
            if (item == null) return;
            _downloadQueue.Remove(item);
            var track = Tracks.FirstOrDefault(t =>
                string.Equals(t.ContentHash, item.ContentHash, StringComparison.OrdinalIgnoreCase));
            if (track != null)
            {
                track.IsQueued = false;
                EnqueueTrackDownload(track);
            }
        }, item => item != null && item.IsFailed);

        ClearCompletedCommand = new RelayCommand(_ => _downloadQueue.ClearCompleted());

        BackToAllArtistsCommand = new RelayCommand(_ =>
        {
            _activeArtistUserId = null;
            OnPropertyChanged(nameof(IsShowingArtistDetail));
            OnPropertyChanged(nameof(ActiveArtistName));
            Refresh();
        });

        if (_sync != null)
        {
            _sync.ManifestMerged += (_, _) =>
                ExecuteOnUiOrCurrent(Refresh);
        }

        Refresh();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Commands
    // ─────────────────────────────────────────────────────────────────────
    public ICommand SetTabCommand { get; }
    public ICommand ViewArtistCommand { get; }
    public ICommand DownloadTrackCommand { get; }
    public ICommand PlayRemoteTrackCommand { get; }
    public ICommand DownloadArtistCommand { get; }
    public ICommand DownloadAlbumCommand { get; }
    public ICommand DownloadPlaylistCommand { get; }
    public ICommand CancelDownloadCommand { get; }
    public ICommand RetryDownloadCommand { get; }
    public ICommand ClearCompletedCommand { get; }
    public ICommand BackToAllArtistsCommand { get; }

    // ─────────────────────────────────────────────────────────────────────
    // Properties
    // ─────────────────────────────────────────────────────────────────────
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string FilterText
    {
        get => _filterText;
        set
        {
            SetProperty(ref _filterText, value);
            Refresh();
        }
    }

    public BrowseTab ActiveTab
    {
        get => _activeTab;
        set
        {
            SetProperty(ref _activeTab, value);
            OnPropertyChanged(nameof(IsTabArtists));
            OnPropertyChanged(nameof(IsTabAlbums));
            OnPropertyChanged(nameof(IsTabTracks));
            OnPropertyChanged(nameof(IsTabPlaylists));
            OnPropertyChanged(nameof(IsTabDownloads));
        }
    }

    public bool IsTabArtists   => ActiveTab == BrowseTab.Artists;
    public bool IsTabAlbums    => ActiveTab == BrowseTab.Albums;
    public bool IsTabTracks    => ActiveTab == BrowseTab.Tracks;
    public bool IsTabPlaylists => ActiveTab == BrowseTab.Playlists;
    public bool IsTabDownloads => ActiveTab == BrowseTab.Downloads;

    public ObservableCollection<BrowseArtistItem> Artists
    {
        get => _artists;
        private set => SetProperty(ref _artists, value);
    }

    public ObservableCollection<BrowseAlbumItem> Albums
    {
        get => _albums;
        private set => SetProperty(ref _albums, value);
    }

    public ObservableCollection<BrowseTrackItem> Tracks
    {
        get => _tracks;
        private set => SetProperty(ref _tracks, value);
    }

    public ObservableCollection<BrowsePlaylistItem> Playlists
    {
        get => _playlists;
        private set => SetProperty(ref _playlists, value);
    }

    public ObservableCollection<DownloadQueueItem> DownloadQueue => _downloadQueue.AllItems;

    public bool IsShowingArtistDetail => _activeArtistUserId != null;

    public string ActiveArtistName
    {
        get
        {
            if (_activeArtistUserId == null) return string.Empty;
            return _artists.FirstOrDefault(a =>
                string.Equals(a.UserId, _activeArtistUserId, StringComparison.OrdinalIgnoreCase))?.DisplayName
                ?? _activeArtistUserId;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Navigation
    // ─────────────────────────────────────────────────────────────────────
    public void NavigateToArtist(string userId)
    {
        _activeArtistUserId = userId;
        ActiveTab = BrowseTab.Tracks;
        OnPropertyChanged(nameof(IsShowingArtistDetail));
        OnPropertyChanged(nameof(ActiveArtistName));
        Refresh();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Data loading
    // ─────────────────────────────────────────────────────────────────────
    private void Refresh()
    {
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_sync == null || !_sync.IsRunning)
        {
            Artists = [];
            Albums = [];
            Tracks = [];
            StatusText = "Connect to the Mesh network to discover community music.";
            return;
        }

        var manifests = _sync.PeerManifests.Where(m => m.StreamType == ManifestStreamType.Content).ToList();
        if (_sync.LocalManifest != null)
            manifests.Add(_sync.LocalManifest);

        var filter = FilterText.Trim();

        // ── Artists ────────────────────────────────────────────────────
        var artists = new List<BrowseArtistItem>();
        foreach (var manifest in manifests)
        {
            var profileOp = manifest.Operations
                .Where(op => op.OperationType == ManifestOperationType.Profile)
                .OrderByDescending(op => op.SequenceNumber)
                .FirstOrDefault();

            var displayName = profileOp?.Metadata.GetValueOrDefault("displayName")
                ?? _sync?.GetPeers().FirstOrDefault(p => string.Equals(p.UserId, manifest.UserId, StringComparison.OrdinalIgnoreCase))?.DisplayName
                ?? manifest.UserId;
            var isArtist = bool.TryParse(profileOp?.Metadata.GetValueOrDefault("isArtist"), out var ia) && ia;
            var bio = profileOp?.Metadata.GetValueOrDefault("bio") ?? string.Empty;

            var resolvedTracks = GetLatestEntities(manifest, "Track");
            var trackCount = resolvedTracks.Count;
            var artistTotalSize = resolvedTracks.Sum(e => long.TryParse(e.Metadata.GetValueOrDefault("fileSize"), out var fs) ? fs : 0);
            var resolvedAlbums = GetLatestEntities(manifest, "Album");
            var albumCount = resolvedAlbums.Count;

            if (!isArtist && trackCount == 0 && albumCount == 0)
                continue;

            if (!string.IsNullOrWhiteSpace(filter)
                && !displayName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                && !bio.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;

            if (_activeArtistUserId != null
                && !string.Equals(manifest.UserId, _activeArtistUserId, StringComparison.OrdinalIgnoreCase))
                continue;

            artists.Add(new BrowseArtistItem
            {
                UserId = manifest.UserId,
                DisplayName = displayName,
                AvatarIconPath = _sync?.UserRepository?.GetUserIconPath(manifest.UserId) ?? string.Empty,
                IconPath = _sync?.UserRepository?.GetUserIconPath(manifest.UserId) ?? string.Empty,
                Bio = bio,
                TrackCount = trackCount,
                AlbumCount = albumCount,
                TotalSize = artistTotalSize,
                TotalSizeDisplay = FormatFileSize(artistTotalSize),
                IsLocal = _sync?.LocalManifest != null && string.Equals(manifest.UserId, _sync.LocalManifest.UserId, StringComparison.OrdinalIgnoreCase)
            });
        }
        Artists = new ObservableCollection<BrowseArtistItem>(artists.OrderByDescending(a => a.TrackCount));

        // ── Albums ─────────────────────────────────────────────────────
        var albums = new List<BrowseAlbumItem>();
        foreach (var manifest in manifests)
        {
            if (_activeArtistUserId != null
                && !string.Equals(manifest.UserId, _activeArtistUserId, StringComparison.OrdinalIgnoreCase))
                continue;

            var artistProfileOp = manifest.Operations
                .Where(op => op.OperationType == ManifestOperationType.Profile)
                .OrderByDescending(op => op.SequenceNumber)
                .FirstOrDefault();
            var artistName = artistProfileOp?.Metadata.GetValueOrDefault("displayName")
                ?? _sync?.GetPeers().FirstOrDefault(p => string.Equals(p.UserId, manifest.UserId, StringComparison.OrdinalIgnoreCase))?.DisplayName
                ?? manifest.UserId;

            var albumEntities = GetLatestEntities(manifest, "Album");
            var trackEntities = GetLatestEntities(manifest, "Track");

            foreach (var entity in albumEntities)
            {
                var name = entity.Metadata.GetValueOrDefault("name") ?? entity.TargetId;
                var albumTracks = trackEntities
                    .Where(t => string.Equals(t.Metadata.GetValueOrDefault("album"), name, StringComparison.OrdinalIgnoreCase));
                var albumTotalSize = albumTracks.Sum(t => long.TryParse(t.Metadata.GetValueOrDefault("fileSize"), out var fs) ? fs : 0);

                if (!string.IsNullOrWhiteSpace(filter)
                    && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    && !artistName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    continue;

                DateTime? releasedAt = null;
                if (entity.Metadata.TryGetValue("releasedAt", out var rat) && DateTime.TryParse(rat, out var dt))
                    releasedAt = dt;

                albums.Add(new BrowseAlbumItem
                {
                    AlbumId = entity.TargetId,
                    Name = name,
                    ArtistUserId = manifest.UserId,
                    ArtistDisplayName = artistName,
                    IconPath = _sync?.UserRepository?.GetUserIconPath(entity.TargetId) ?? string.Empty,
                    ReleasedAt = releasedAt,
                    TotalSize = albumTotalSize,
                    TotalSizeDisplay = FormatFileSize(albumTotalSize),
                    IsLocal = _sync?.LocalManifest != null && string.Equals(manifest.UserId, _sync.LocalManifest.UserId, StringComparison.OrdinalIgnoreCase)
                });
            }
        }
        Albums = new ObservableCollection<BrowseAlbumItem>(albums.OrderByDescending(a => a.ReleasedAt ?? DateTime.MinValue));

        // ── Tracks ─────────────────────────────────────────────────────
        var tracks = new List<BrowseTrackItem>();
        foreach (var manifest in manifests)
        {
            if (_activeArtistUserId != null
                && !string.Equals(manifest.UserId, _activeArtistUserId, StringComparison.OrdinalIgnoreCase))
                continue;

            var artistProfileOp = manifest.Operations
                .Where(op => op.OperationType == ManifestOperationType.Profile)
                .OrderByDescending(op => op.SequenceNumber)
                .FirstOrDefault();
            var artistName = artistProfileOp?.Metadata.GetValueOrDefault("displayName")
                ?? _sync?.GetPeers().FirstOrDefault(p => string.Equals(p.UserId, manifest.UserId, StringComparison.OrdinalIgnoreCase))?.DisplayName
                ?? manifest.UserId;

            var trackEntities = GetLatestEntities(manifest, "Track");
            var downloadedEntries = _downloadState.GetDownloadedEntries();

            foreach (var entity in trackEntities)
            {
                var title = entity.Metadata.GetValueOrDefault("title") ?? entity.TargetId;
                var album = entity.Metadata.GetValueOrDefault("album") ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(filter)
                    && !title.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    && !artistName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    && !album.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    continue;

                DateTime? releasedAt = null;
                if (entity.Metadata.TryGetValue("releasedAt", out var rat) && DateTime.TryParse(rat, out var dt))
                    releasedAt = dt;

                var queueItem = !string.IsNullOrWhiteSpace(entity.ContentHash)
                    ? _downloadQueue.AllItems.FirstOrDefault(i => string.Equals(i.ContentHash, entity.ContentHash, StringComparison.OrdinalIgnoreCase))
                    : null;
                var isQueued = queueItem != null && (queueItem.State == DownloadState.Pending || queueItem.State == DownloadState.Downloading);
                var isDownloaded = queueItem?.State == DownloadState.Done;
                if (!isDownloaded && !string.IsNullOrWhiteSpace(entity.ContentHash) && _sync != null)
                    isDownloaded = await _sync.IsContentAvailableLocallyAsync(entity.ContentHash);

                var isLocal = _sync?.LocalManifest != null && string.Equals(manifest.UserId, _sync.LocalManifest.UserId, StringComparison.OrdinalIgnoreCase);

                var needsUpdate = false;
                if (!isLocal && !string.IsNullOrWhiteSpace(entity.ContentHash))
                {
                    var downloaded = downloadedEntries.FirstOrDefault(e => string.Equals(e.TrackId, entity.TargetId, StringComparison.OrdinalIgnoreCase));
                    if (downloaded != null)
                    {
                        if (!string.Equals(downloaded.ContentHash, entity.ContentHash, StringComparison.OrdinalIgnoreCase) ||
                            entity.SequenceNumber > downloaded.SequenceNumber)
                        {
                            needsUpdate = true;
                        }
                    }
                }

                var fileSize = long.TryParse(entity.Metadata.GetValueOrDefault("fileSize"), out var fs) ? fs : 0;

                tracks.Add(new BrowseTrackItem
                {
                    TrackId = entity.TargetId,
                    Title = title,
                    ArtistUserId = manifest.UserId,
                    ArtistDisplayName = artistName,
                    Album = album,
                    ContentHash = entity.ContentHash,
                    FileSize = fileSize,
                    FileSizeDisplay = FormatFileSize(fileSize),
                    ReleasedAt = releasedAt,
                    IconPath = _sync?.UserRepository?.GetUserIconPath(entity.TargetId) ?? string.Empty,
                    IsQueued = isQueued,
                    IsDownloaded = isDownloaded,
                    IsLocal = isLocal,
                    NeedsUpdate = needsUpdate
                });
            }
        }
        Tracks = new ObservableCollection<BrowseTrackItem>(tracks.OrderByDescending(t => t.ReleasedAt ?? DateTime.MinValue));

        // ── Playlists ──────────────────────────────────────────────────
        var playlists = new List<BrowsePlaylistItem>();
        foreach (var manifest in manifests)
        {
            if (_activeArtistUserId != null
                && !string.Equals(manifest.UserId, _activeArtistUserId, StringComparison.OrdinalIgnoreCase))
                continue;

            var artistProfileOp = manifest.Operations
                .Where(op => op.OperationType == ManifestOperationType.Profile)
                .OrderByDescending(op => op.SequenceNumber)
                .FirstOrDefault();
            var artistName = artistProfileOp?.Metadata.GetValueOrDefault("displayName")
                ?? _sync?.GetPeers().FirstOrDefault(p => string.Equals(p.UserId, manifest.UserId, StringComparison.OrdinalIgnoreCase))?.DisplayName
                ?? manifest.UserId;

            var playlistEntities = GetLatestEntities(manifest, "Playlist");

            foreach (var entity in playlistEntities)
            {
                var name = entity.Metadata.GetValueOrDefault("name") ?? entity.TargetId;
                var description = entity.Metadata.GetValueOrDefault("description") ?? string.Empty;
                var trackIdsJson = entity.Metadata.GetValueOrDefault("trackIds") ?? "[]";
                List<string> trackIds = [];
                try
                {
                    trackIds = System.Text.Json.JsonSerializer.Deserialize<List<string>>(trackIdsJson) ?? [];
                }
                catch { }

                if (!string.IsNullOrWhiteSpace(filter)
                    && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    && !artistName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    && !description.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    continue;

                DateTime? releasedAt = null;
                if (entity.Metadata.TryGetValue("releasedAt", out var rat) && DateTime.TryParse(rat, out var dt))
                    releasedAt = dt;

                playlists.Add(new BrowsePlaylistItem
                {
                    PlaylistId = entity.TargetId,
                    Name = name,
                    Description = description,
                    ArtistUserId = manifest.UserId,
                    ArtistDisplayName = artistName,
                    TrackIds = trackIds,
                    TrackCount = trackIds.Count,
                    ReleasedAt = releasedAt,
                    IconPath = _sync?.UserRepository?.GetUserIconPath(entity.TargetId) ?? string.Empty,
                    IsLocal = _sync?.LocalManifest != null && string.Equals(manifest.UserId, _sync.LocalManifest.UserId, StringComparison.OrdinalIgnoreCase)
                });
            }
        }
        Playlists = new ObservableCollection<BrowsePlaylistItem>(playlists.OrderByDescending(p => p.ReleasedAt ?? DateTime.MinValue));

        OnPropertyChanged(nameof(ActiveArtistName));

        var totalArtists = Artists.Count;
        var totalTracks = Tracks.Count;
        var totalPlaylists = Playlists.Count;
        StatusText = $"{totalArtists} artist{(totalArtists == 1 ? "" : "s")}, {totalTracks} track{(totalTracks == 1 ? "" : "s")}, {totalPlaylists} playlist{(totalPlaylists == 1 ? "" : "s")} discovered from the mesh.";
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes <= 0) return "Unknown";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        int unitIndex = 0;
        double size = bytes;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }
        return $"{size:N1} {units[unitIndex]}";
    }


    // ─────────────────────────────────────────────────────────────────────
    // Download
    // ─────────────────────────────────────────────────────────────────────
    private void EnqueueTrackDownload(BrowseTrackItem? track)
    {
        if (track == null || string.IsNullOrWhiteSpace(track.ContentHash)) return;

        var item = _downloadQueue.Enqueue(
            track.ArtistUserId,
            track.ContentHash!,
            track.Title,
            track.ArtistDisplayName,
            track.Album,
            "Track",
            track.TrackId);

        track.IsQueued = true;

        EnsureDownloadFolderPlaceholder(track.ArtistDisplayName, track.Album);

        if (_sync == null) return;

        _ = Task.Run(async () =>
        {
            ExecuteOnUiOrCurrent(() => item.State = DownloadState.Downloading);

            try
            {
                var manifests = _sync.PeerManifests.Where(m => m.StreamType == ManifestStreamType.Content).ToList();
                if (_sync.LocalManifest != null)
                    manifests.Add(_sync.LocalManifest);

                var bytes = await _sync.RequestContentAsync(item.PeerUserId, item.ContentHash);
                if (bytes == null || bytes.Length == 0)
                {
                    var details = _sync.LastConnectionAttemptReport?.BuildUserFacingSummary() ?? "Peer did not return content.";
                    ExecuteOnUiOrCurrent(() =>
                    {
                        item.State = DownloadState.Failed;
                        item.StatusMessage = details;
                        StatusText = $"Download failed for \"{track.Title}\". {details}";
                        track.IsQueued = false;
                    });
                    ScheduleAutoRetry(track, item);
                    return;
                }

                _settingsService.EnsureFoldersExist();
                var otherMusicFolder = _settingsService.GetPeerMusicFolder();
                Directory.CreateDirectory(otherMusicFolder);

                var safeArtist = SanitizeForPath(item.Artist, "Unknown Artist");
                var safeAlbum = SanitizeForPath(string.IsNullOrWhiteSpace(item.Album) ? "Downloads" : item.Album, "Downloads");
                var ext = ResolveFileExtension(bytes, item.Title);
                var safeName = SanitizeForPath(item.Title, item.Id);
                var destFolder = Path.Combine(otherMusicFolder, safeArtist, safeAlbum);
                Directory.CreateDirectory(destFolder);
                var destPath = Path.Combine(destFolder, safeName + ext);
                await File.WriteAllBytesAsync(destPath, bytes);

                ExecuteOnUiOrCurrent(() =>
                {
                    item.State = DownloadState.Done;
                    item.PercentComplete = 100;
                    item.StatusMessage = destPath;
                    track.IsQueued = false;
                    track.IsDownloaded = true;
                    if (!string.IsNullOrWhiteSpace(track.TrackId) && !string.IsNullOrWhiteSpace(track.ContentHash))
                    {
                        var meshTrack = manifests.SelectMany(m => GetLatestEntities(m, "Track"))
                            .FirstOrDefault(e => string.Equals(e.TargetId, track.TrackId, StringComparison.OrdinalIgnoreCase));
                        _downloadState.MarkDownloaded(track.TrackId, track.ContentHash, meshTrack?.SequenceNumber ?? 0);
                    }
                    StatusText = $"Downloaded \"{track.Title}\" to Library.";
                    Refresh(); // Full refresh to re-evaluate states
                });
            }
            catch (Exception ex)
            {
                ExecuteOnUiOrCurrent(() =>
                {
                    item.State = DownloadState.Failed;
                    item.StatusMessage = ex.Message;
                    StatusText = $"Download failed for \"{track.Title}\": {ex.Message}";
                    track.IsQueued = false;
                });
                ScheduleAutoRetry(track, item);
            }
        });
    }

    private void ScheduleAutoRetry(BrowseTrackItem track, DownloadQueueItem item)
    {
        _ = Task.Delay(TimeSpan.FromSeconds(15)).ContinueWith(_ =>
        {
            ExecuteOnUiOrCurrent(() =>
            {
                if (item.State != DownloadState.Failed)
                    return;

                item.State = DownloadState.Pending;
                item.StatusMessage = "Auto-retrying...";
                StatusText = $"Retrying download for \"{track.Title}\"...";
                track.IsQueued = false;
                track.IsDownloaded = false;
                EnqueueTrackDownload(track);
            });
        });
    }

    private static void ExecuteOnUiOrCurrent(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null)
            dispatcher.Invoke(action);
        else
            action();
    }

    private void EnsureDownloadFolderPlaceholder(string artist, string album)
    {
        try
        {
            _settingsService.EnsureFoldersExist();
            var otherMusicFolder = _settingsService.GetPeerMusicFolder();
            var safeArtist = SanitizeForPath(artist, "Unknown Artist");
            var safeAlbum = SanitizeForPath(string.IsNullOrWhiteSpace(album) ? "Downloads" : album, "Downloads");
            var destFolder = Path.Combine(otherMusicFolder, safeArtist, safeAlbum);
            Directory.CreateDirectory(destFolder);
        }
        catch
        {
            // best-effort — folder will be created at download time if this fails
        }
    }

    private static string ResolveFileExtension(byte[] bytes, string title)
    {
        if (bytes.Length >= 12)
        {
            if (bytes[0] == 0x49 && bytes[1] == 0x44 && bytes[2] == 0x33) return ".mp3";
            if (bytes[0] == 0x66 && bytes[1] == 0x4C && bytes[2] == 0x61 && bytes[3] == 0x43) return ".flac";
            if (bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46) return ".wav";
            if (bytes[4] == 0x66 && bytes[5] == 0x74 && bytes[6] == 0x79 && bytes[7] == 0x70) return ".m4a";
            if (bytes[0] == 0x4F && bytes[1] == 0x67 && bytes[2] == 0x67 && bytes[3] == 0x53) return ".ogg";
        }
        var n = title.ToLowerInvariant();
        if (n.EndsWith(".mp3")) return ".mp3";
        if (n.EndsWith(".flac")) return ".flac";
        if (n.EndsWith(".wav")) return ".wav";
        if (n.EndsWith(".ogg")) return ".ogg";
        if (n.EndsWith(".m4a")) return ".m4a";
        return ".mp3";
    }

    private static string SanitizeForPath(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
            if (!invalid.Contains(c)) sb.Append(c);
        var result = sb.ToString().Trim();
        return string.IsNullOrWhiteSpace(result) ? fallback : result;
    }

    private class ResolvedEntity
    {
        public string TargetId { get; set; } = string.Empty;
        public string? ContentHash { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = [];
        public int SequenceNumber { get; set; }
        public DateTime Timestamp { get; set; }
        public bool IsDeleted { get; set; }
    }

    private static List<ResolvedEntity> GetLatestEntities(Manifest manifest, string targetType)
    {
        var resolved = new Dictionary<string, ResolvedEntity>(StringComparer.OrdinalIgnoreCase);

        // 1. Load from Snapshot
        if (manifest.Snapshot != null)
        {
            foreach (var state in manifest.Snapshot.EntityStates)
            {
                if (string.Equals(state.TargetType, targetType, StringComparison.OrdinalIgnoreCase))
                {
                    resolved[state.TargetId] = new ResolvedEntity
                    {
                        TargetId = state.TargetId,
                        ContentHash = state.ContentHash,
                        Metadata = new Dictionary<string, string>(state.Metadata, StringComparer.OrdinalIgnoreCase),
                        SequenceNumber = manifest.Snapshot.LastSequenceNumber,
                        Timestamp = manifest.Snapshot.Timestamp,
                        IsDeleted = false
                    };
                }
            }
        }

        // 2. Apply Operations
        foreach (var op in manifest.Operations)
        {
            if (!string.Equals(op.TargetType, targetType, StringComparison.OrdinalIgnoreCase))
                continue;

            if (op.OperationType != ManifestOperationType.Create &&
                op.OperationType != ManifestOperationType.Update &&
                op.OperationType != ManifestOperationType.Delete)
                continue;

            if (resolved.TryGetValue(op.TargetId, out var existing) && op.SequenceNumber <= existing.SequenceNumber)
                continue;

            resolved[op.TargetId] = new ResolvedEntity
            {
                TargetId = op.TargetId,
                ContentHash = op.ContentHash,
                Metadata = new Dictionary<string, string>(op.Metadata, StringComparer.OrdinalIgnoreCase),
                SequenceNumber = op.SequenceNumber,
                Timestamp = op.Timestamp,
                IsDeleted = op.OperationType == ManifestOperationType.Delete
            };
        }

        return resolved.Values.Where(e => !e.IsDeleted).ToList();
    }
}
