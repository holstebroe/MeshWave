using MeshWave.Common.Core.Models;
using MeshWave.Mvvm;
using MeshWave.Services;
using MeshWave.Synchronizer;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows.Input;

namespace MeshWave.ViewModels;

public enum BrowseTab { Artists, Albums, Tracks, Downloads }

// ─────────────────────────────────────────────────────────────────────────
// Data items
// ─────────────────────────────────────────────────────────────────────────

public class BrowseArtistItem : ViewModelBase
{
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string AvatarIconPath { get; set; } = string.Empty;
    public string Bio { get; set; } = string.Empty;
    public int TrackCount { get; set; }
    public int AlbumCount { get; set; }
}

public class BrowseAlbumItem : ViewModelBase
{
    public string AlbumId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ArtistUserId { get; set; } = string.Empty;
    public string ArtistDisplayName { get; set; } = string.Empty;
    public int TrackCount { get; set; }
    public DateTime? ReleasedAt { get; set; }
    public string ReleasedAtDisplay => ReleasedAt.HasValue ? ReleasedAt.Value.ToLocalTime().ToString("MMM yyyy") : string.Empty;
}

public class BrowseTrackItem : ViewModelBase
{
    private bool _isQueued;
    private bool _isDownloaded;

    public string TrackId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ArtistUserId { get; set; } = string.Empty;
    public string ArtistDisplayName { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    public string? ContentHash { get; set; }
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

    public string DownloadButtonLabel => IsQueued ? "⏳ Queued" : IsDownloaded ? "✅ Downloaded" : "⬇ Download";
    public bool CanDownload => !IsQueued && !IsDownloaded;
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
    private readonly SettingsService _settingsService = new();

    private BrowseTab _activeTab = BrowseTab.Artists;
    private string _statusText = "Connect to the Mesh network to discover community music.";
    private string _filterText = string.Empty;
    private string? _activeArtistUserId;

    private ObservableCollection<BrowseArtistItem> _artists = [];
    private ObservableCollection<BrowseAlbumItem> _albums = [];
    private ObservableCollection<BrowseTrackItem> _tracks = [];

    public BrowseViewModel(ISyncBrowseClient? sync = null, DownloadQueueService? downloadQueue = null)
    {
        _sync = sync;
        _downloadQueue = downloadQueue ?? new DownloadQueueService();

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
                System.Windows.Application.Current?.Dispatcher.Invoke(Refresh);
        }

        Refresh();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Commands
    // ─────────────────────────────────────────────────────────────────────
    public ICommand SetTabCommand { get; }
    public ICommand ViewArtistCommand { get; }
    public ICommand DownloadTrackCommand { get; }
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
            OnPropertyChanged(nameof(IsTabDownloads));
        }
    }

    public bool IsTabArtists   => ActiveTab == BrowseTab.Artists;
    public bool IsTabAlbums    => ActiveTab == BrowseTab.Albums;
    public bool IsTabTracks    => ActiveTab == BrowseTab.Tracks;
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
        if (_sync == null || !_sync.IsRunning)
        {
            Artists = [];
            Albums = [];
            Tracks = [];
            StatusText = "Connect to the Mesh network to discover community music.";
            return;
        }

        var manifests = _sync.PeerManifests.ToList();
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
                ?? _sync.GetPeers().FirstOrDefault(p => string.Equals(p.UserId, manifest.UserId, StringComparison.OrdinalIgnoreCase))?.DisplayName
                ?? manifest.UserId;
            var isArtist = bool.TryParse(profileOp?.Metadata.GetValueOrDefault("isArtist"), out var ia) && ia;
            var bio = profileOp?.Metadata.GetValueOrDefault("bio") ?? string.Empty;

            var publicTrackOps = GetLatestPublicTrackOperations(manifest);
            var trackCount = publicTrackOps.Count;
            var albumCount = manifest.Operations.Count(op =>
                op.OperationType == ManifestOperationType.Create
                && string.Equals(op.TargetType, "Album", StringComparison.OrdinalIgnoreCase));

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
                AvatarIconPath = profileOp?.Metadata.GetValueOrDefault("iconPath") ?? string.Empty,
                Bio = bio,
                TrackCount = trackCount,
                AlbumCount = albumCount
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
                ?? _sync.GetPeers().FirstOrDefault(p => string.Equals(p.UserId, manifest.UserId, StringComparison.OrdinalIgnoreCase))?.DisplayName
                ?? manifest.UserId;

            var albumOps = manifest.Operations
                .Where(op => op.OperationType == ManifestOperationType.Create
                          && string.Equals(op.TargetType, "Album", StringComparison.OrdinalIgnoreCase));

            foreach (var op in albumOps)
            {
                var name = op.Metadata.GetValueOrDefault("name") ?? op.TargetId;
                if (!string.IsNullOrWhiteSpace(filter)
                    && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    && !artistName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    continue;

                DateTime? releasedAt = null;
                if (op.Metadata.TryGetValue("releasedAt", out var rat) && DateTime.TryParse(rat, out var dt))
                    releasedAt = dt;

                albums.Add(new BrowseAlbumItem
                {
                    AlbumId = op.TargetId,
                    Name = name,
                    ArtistUserId = manifest.UserId,
                    ArtistDisplayName = artistName,
                    ReleasedAt = releasedAt
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
                ?? _sync.GetPeers().FirstOrDefault(p => string.Equals(p.UserId, manifest.UserId, StringComparison.OrdinalIgnoreCase))?.DisplayName
                ?? manifest.UserId;

            var trackOps = GetLatestPublicTrackOperations(manifest);

            foreach (var op in trackOps)
            {
                var title = op.Metadata.GetValueOrDefault("title") ?? op.TargetId;
                var album = op.Metadata.GetValueOrDefault("album") ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(filter)
                    && !title.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    && !artistName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    && !album.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    continue;

                DateTime? releasedAt = null;
                if (op.Metadata.TryGetValue("releasedAt", out var rat) && DateTime.TryParse(rat, out var dt))
                    releasedAt = dt;

                var queueItem = !string.IsNullOrWhiteSpace(op.ContentHash)
                    ? _downloadQueue.AllItems.FirstOrDefault(i => string.Equals(i.ContentHash, op.ContentHash, StringComparison.OrdinalIgnoreCase))
                    : null;
                var isQueued = queueItem != null && (queueItem.State == DownloadState.Pending || queueItem.State == DownloadState.Downloading);
                var isDownloaded = queueItem?.State == DownloadState.Done;

                tracks.Add(new BrowseTrackItem
                {
                    TrackId = op.TargetId,
                    Title = title,
                    ArtistUserId = manifest.UserId,
                    ArtistDisplayName = artistName,
                    Album = album,
                    ContentHash = op.ContentHash,
                    ReleasedAt = releasedAt,
                    IsQueued = isQueued,
                    IsDownloaded = isDownloaded
                });
            }
        }
        Tracks = new ObservableCollection<BrowseTrackItem>(tracks.OrderByDescending(t => t.ReleasedAt ?? DateTime.MinValue));

        OnPropertyChanged(nameof(ActiveArtistName));

        var totalArtists = Artists.Count;
        var totalTracks = Tracks.Count;
        StatusText = $"{totalArtists} artist{(totalArtists == 1 ? "" : "s")}, {totalTracks} track{(totalTracks == 1 ? "" : "s")} discovered from the mesh.";
    }

    private static List<ManifestOperation> GetLatestPublicTrackOperations(Manifest manifest)
    {
        return manifest.Operations
            .Where(op => string.Equals(op.TargetType, "Track", StringComparison.OrdinalIgnoreCase)
                      && (op.OperationType == ManifestOperationType.Create
                       || op.OperationType == ManifestOperationType.Update
                       || op.OperationType == ManifestOperationType.Delete))
            .GroupBy(op => op.TargetId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(op => op.SequenceNumber).First())
            .Where(op => op.OperationType != ManifestOperationType.Delete)
            .OrderByDescending(op => op.Timestamp)
            .ToList();
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
            "Track");

        track.IsQueued = true;

        if (_sync == null) return;

        _ = Task.Run(async () =>
        {
            ExecuteOnUiOrCurrent(() => item.State = DownloadState.Downloading);

            try
            {
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
                var otherMusicFolder = _settingsService.GetOtherMusicFolder();
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
                    item.ProgressPercent = 100;
                    item.StatusMessage = destPath;
                    track.IsQueued = false;
                    track.IsDownloaded = true;
                    StatusText = $"Downloaded \"{track.Title}\" to Library.";
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
}
