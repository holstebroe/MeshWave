using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeshWave.Common.Core.Crypto;
using MeshWave.Common.Core.Models;
using MeshWave.LibraryManager;
using MeshWave.Mvvm;
using MeshWave.Services;

namespace MeshWave.ViewModels
{
    public partial class LibraryViewModel : ViewModelBase, IDisposable
    {
        private readonly SettingsService _settingsService = new();
        private readonly MyMusicMetadataService _myMusicMetadataService = new();
        private LocalLibraryManager? _libraryManager;
        private MusicFolderWatcher? _folderWatcher;
        private List<Track> _trackObjects = new();
        private List<Album> _albumObjects = new();
        private CancellationTokenSource? _importCancellation;
        private readonly HashSet<string> _autoAnnouncedTrackIds = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _autoAnnouncedAlbumIds = new(StringComparer.OrdinalIgnoreCase);

        public void LoadFromConfiguredBaseFolder()
        {
            _settingsService.EnsureFoldersExist();
            var settings = _settingsService.LoadSettings();
            var libraryFolder = IsMyMusicLibrary
                ? _settingsService.GetLocalMusicFolder()
                : _settingsService.GetPeerMusicFolder();
            LoadLibrary(libraryFolder, settings.SupportedExtensions);
        }

        public async Task ImportMyMusicAsync(string sourceFolder)
        {
            if (!IsMyMusicLibrary)
            {
                return;
            }

            ImportSingleFileStatus = string.Empty;

            _settingsService.EnsureFoldersExist();
            var settings = _settingsService.LoadSettings();
            var myMusicFolder = _settingsService.GetLocalMusicFolder();

            _folderWatcher?.Dispose();
            _folderWatcher = null;

            _importCancellation?.Cancel();
            _importCancellation?.Dispose();
            _importCancellation = new CancellationTokenSource();

            IsImporting = true;
            ImportStatusMessage = "Starting import...";
            ImportCurrentFile = string.Empty;
            ImportTotalFiles = 0;
            ImportRemainingFiles = 0;
            ImportImportedFiles = 0;
            OnPropertyChanged(nameof(ImportProgressPercent));

            try
            {
                await Task.Run(() =>
                    LocalLibraryManager.ImportMusicToOrganizedStructure(
                        sourceFolder,
                        myMusicFolder,
                        settings.SupportedExtensions,
                        progress =>
                        {
                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                ImportTotalFiles = progress.TotalFiles;
                                ImportRemainingFiles = progress.RemainingFiles;
                                ImportImportedFiles = progress.ImportedFiles;
                                ImportCurrentFile = progress.CurrentFile;
                                ImportStatusMessage = progress.StatusMessage;
                                OnPropertyChanged(nameof(ImportProgressPercent));
                            });
                        },
                        _importCancellation.Token));

                ImportStatusMessage = "Import completed.";
            }
            catch (OperationCanceledException)
            {
                ImportStatusMessage = "Import cancelled.";
            }
            finally
            {
                IsImporting = false;
                _importCancellation?.Dispose();
                _importCancellation = null;
                LoadFromConfiguredBaseFolder();
            }
        }

        private void CancelImport()
        {
            _importCancellation?.Cancel();
        }

        public void ImportMyMusicFile(string sourceFile)
        {
            if (!IsMyMusicLibrary)
            {
                return;
            }

            _settingsService.EnsureFoldersExist();
            var settings = _settingsService.LoadSettings();
            var myMusicFolder = _settingsService.GetLocalMusicFolder();

            var imported = LocalLibraryManager.ImportSingleFileToOrganizedStructure(sourceFile, myMusicFolder, settings.SupportedExtensions);
            ImportSingleFileStatus = imported
                ? "File imported successfully."
                : "File already exists or is unsupported.";

            LoadFromConfiguredBaseFolder();
        }

        public void LoadLibrary(string folderPath, IEnumerable<string>? supportedExtensions = null)
        {
            var previousArtistName = SelectedArtist?.Name;
            var previousAlbumId = SelectedAlbum?.AlbumId;

            _folderWatcher?.Dispose();
            _libraryManager = new LocalLibraryManager(folderPath, supportedExtensions);
            _libraryManager.IndexLibrary();
            _trackObjects = _libraryManager.GetAllTracks().ToList();
            _albumObjects = _libraryManager.GetAllAlbums().ToList();

            var trackItems = _trackObjects.Select(t =>
            {
                var album = _albumObjects.FirstOrDefault(a => a.AlbumId == t.AlbumId);
                var resolvedPath = string.IsNullOrWhiteSpace(t.FilePath) ? t.FileHash : t.FilePath;
                var coverPath = _libraryManager.GetTrackCoverPath(resolvedPath);
                var trackMeta = _myMusicMetadataService.LoadForTrack(resolvedPath);
                var effectiveRelease = trackMeta.IsReleased;
                return new LibraryTrackItem
                {
                    TrackId = t.TrackId,
                    Title = t.Title,
                    Artist = string.IsNullOrWhiteSpace(t.Description) ? "Unknown Artist" : t.Description!,
                    AlbumId = t.AlbumId ?? string.Empty,
                    AlbumName = album?.Title ?? string.Empty,
                    CoverPath = coverPath,
                    FilePath = resolvedPath,
                    ContentHash = CryptoService.ComputeFileHash(resolvedPath),
                    IsReleased = effectiveRelease,
                    Version = trackMeta.Version <= 0 ? 1 : trackMeta.Version,
                    TrackNumber = trackMeta.TrackNumber,
                    Duration = t.Duration,
                    PlayCount = trackMeta.PlayCount,
                    SourcePeerUserId = string.Empty
                };
            }).ToList();

            var albumItems = _albumObjects.Select(a =>
            {
                var tracksInAlbum = trackItems.Where(t => t.AlbumId == a.AlbumId).ToList();
                var coverPath = tracksInAlbum.Select(t => t.CoverPath).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p)) ?? string.Empty;
                var firstTrackPath = tracksInAlbum.Select(t => t.FilePath).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
                var albumFolder = string.IsNullOrWhiteSpace(firstTrackPath) ? string.Empty : Path.GetDirectoryName(firstTrackPath) ?? string.Empty;
                var albumMeta = _myMusicMetadataService.LoadForAlbum(albumFolder);
                var albumReleased = albumMeta.IsReleased || tracksInAlbum.Any(t => t.IsReleased);

                return new LibraryAlbumItem
                {
                    AlbumId = a.AlbumId,
                    Artist = tracksInAlbum.FirstOrDefault()?.Artist ?? "Unknown Artist",
                    Name = a.Title,
                    CoverPath = coverPath,
                    TrackCount = tracksInAlbum.Count,
                    IsReleased = albumReleased,
                    Version = albumMeta.Version <= 0 ? 1 : albumMeta.Version
                };
            }).ToList();

            var artistItems = trackItems
                .GroupBy(t => t.Artist)
                .Select(g => new LibraryArtistItem
                {
                    Name = g.Key,
                    CoverPath = g.Select(t => t.CoverPath).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p)) ?? string.Empty,
                    AlbumCount = albumItems.Count(a => a.Artist == g.Key),
                    TrackCount = g.Count()
                })
                .OrderBy(a => a.Name)
                .ToList();

            Artists = artistItems;

            if (previousArtistName != null)
            {
                SelectedArtist = artistItems.FirstOrDefault(a => a.Name == previousArtistName) ?? artistItems.FirstOrDefault();
            }
            else
            {
                SelectedArtist = artistItems.FirstOrDefault();
            }

            _allAlbumItems = albumItems;
            _allTrackItems = trackItems;

            if (previousAlbumId != null)
            {
                SelectedAlbum = _allAlbumItems.FirstOrDefault(a => a.AlbumId == previousAlbumId);
            }

            RefreshAlbumAndTrackSelection();

            AnnounceReleasedContentToMesh();

            if (Directory.Exists(folderPath))
            {
                _folderWatcher = new MusicFolderWatcher(folderPath, supportedExtensions ?? LocalLibraryManager.SupportedExtensions, () =>
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        LoadLibrary(folderPath, supportedExtensions);
                    });
                });
            }
        }

        private List<LibraryAlbumItem> _allAlbumItems = [];
        private List<LibraryTrackItem> _allTrackItems = [];

        private void EnsureQueueAlbumShells(List<LibraryAlbumItem> targetAlbums, IEnumerable<DownloadQueueItem> queueItems)
        {
            var knownAlbumKeys = targetAlbums
                .Select(a => $"{a.Artist}|{a.Name}")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var q in queueItems.Where(q => !string.IsNullOrWhiteSpace(q.Artist) && !string.IsNullOrWhiteSpace(q.Album)))
            {
                var key = $"{q.Artist}|{q.Album}";
                if (knownAlbumKeys.Contains(key))
                    continue;

                targetAlbums.Add(new LibraryAlbumItem
                {
                    AlbumId = ResolveAlbumId(targetAlbums, q.Artist, q.Album),
                    Artist = q.Artist,
                    Name = q.Album,
                    CoverPath = string.Empty,
                    TrackCount = 0,
                    IsReleased = true,
                    Version = 1
                });

                knownAlbumKeys.Add(key);
            }

            var refreshedArtists = targetAlbums
                .GroupBy(a => a.Artist)
                .Select(g => new LibraryArtistItem
                {
                    Name = g.Key,
                    CoverPath = g.Select(a => a.CoverPath).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p)) ?? string.Empty,
                    AlbumCount = g.Count(),
                    TrackCount = _allTrackItems.Count(t => string.Equals(t.Artist, g.Key, StringComparison.OrdinalIgnoreCase))
                })
                .OrderBy(a => a.Name)
                .ToList();

            _allAlbumItems = targetAlbums
                .OrderBy(a => a.Artist, StringComparer.OrdinalIgnoreCase)
                .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            Artists = refreshedArtists;

            if (SelectedArtist != null)
            {
                var existing = Artists.FirstOrDefault(a => string.Equals(a.Name, SelectedArtist.Name, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                    _selectedArtist = existing;
            }
        }

        private static string ResolveAlbumId(IEnumerable<LibraryAlbumItem> albums, string artist, string albumName)
        {
            var existing = albums.FirstOrDefault(a => string.Equals(a.Artist, artist, StringComparison.OrdinalIgnoreCase)
                                                   && string.Equals(a.Name, albumName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
                return existing.AlbumId;

            return $"placeholder:{artist}:{albumName}";
        }

        private void QueueTrackRedownload(LibraryTrackItem track)
        {
            if (_applicationViewModel == null || string.IsNullOrWhiteSpace(track.ContentHash))
                return;

            var existing = _applicationViewModel.DownloadQueueItems.FirstOrDefault(q =>
                string.Equals(q.ContentHash, track.ContentHash, StringComparison.OrdinalIgnoreCase)
                && q.State != DownloadState.Done);
            if (existing != null)
            {
                if (existing.State == DownloadState.Failed)
                {
                    existing.State = DownloadState.Pending;
                    existing.StatusMessage = "Queued from Library placeholder.";
                }
                return;
            }

            _applicationViewModel.DownloadQueueItems.Add(new DownloadQueueItem
            {
                PeerUserId = track.SourcePeerUserId,
                ContentHash = track.ContentHash,
                Title = track.Title,
                Artist = track.Artist,
                Album = track.AlbumName,
                TargetType = "Track",
                State = DownloadState.Pending,
                StatusMessage = "Queued from Library placeholder."
            });
        }

        private void RefreshAlbumAndTrackSelection()
        {
            var removedEntries = !IsMyMusicLibrary
                ? _downloadStateService.GetRemovedEntries()
                : [];

            if (!IsMyMusicLibrary && _applicationViewModel != null)
            {
                EnsureQueueAlbumShells(_allAlbumItems, _applicationViewModel.DownloadQueueItems);

                var addedRemovedShells = false;
                foreach (var removed in removedEntries)
                {
                    if (string.IsNullOrWhiteSpace(removed.Artist) || string.IsNullOrWhiteSpace(removed.Album))
                        continue;

                    var exists = _allAlbumItems.Any(a => string.Equals(a.Artist, removed.Artist, StringComparison.OrdinalIgnoreCase)
                                                      && string.Equals(a.Name, removed.Album, StringComparison.OrdinalIgnoreCase));
                    if (exists)
                        continue;

                    _allAlbumItems.Add(new LibraryAlbumItem
                    {
                        AlbumId = ResolveAlbumId(_allAlbumItems, removed.Artist, removed.Album),
                        Artist = removed.Artist,
                        Name = removed.Album,
                        CoverPath = string.Empty,
                        TrackCount = 0,
                        IsReleased = true,
                        Version = 1
                    });
                    addedRemovedShells = true;
                }

                if (addedRemovedShells)
                {
                    EnsureQueueAlbumShells(_allAlbumItems, []);
                }
            }

            var filteredAlbums = SelectedArtist == null
                ? _allAlbumItems
                : _allAlbumItems.Where(a => a.Artist == SelectedArtist.Name).ToList();

            if (!IsMyMusicLibrary && _applicationViewModel != null)
            {
                foreach (var album in filteredAlbums)
                {
                    var albumQueueItems = _applicationViewModel.DownloadQueueItems
                        .Where(q => string.Equals(q.Album, album.Name, StringComparison.OrdinalIgnoreCase)
                                 && string.Equals(q.Artist, album.Artist, StringComparison.OrdinalIgnoreCase)
                                 && (q.State == DownloadState.Pending || q.State == DownloadState.Downloading || q.State == DownloadState.Failed))
                        .ToList();

                    album.PendingDownloadCount = albumQueueItems.Count(q => q.State == DownloadState.Pending);
                    album.DownloadingCount = albumQueueItems.Count(q => q.State == DownloadState.Downloading);
                    album.FailedDownloadCount = albumQueueItems.Count(q => q.State == DownloadState.Failed);
                }
            }
            else
            {
                foreach (var album in filteredAlbums)
                {
                    album.PendingDownloadCount = 0;
                    album.DownloadingCount = 0;
                    album.FailedDownloadCount = 0;
                }
            }

            Albums = filteredAlbums;

            var filteredTracks = SelectedAlbum == null
                ? []
                : _allTrackItems
                    .Where(t => t.AlbumId == SelectedAlbum.AlbumId)
                    .OrderBy(t => t.TrackNumber <= 0 ? 1 : 0)
                    .ThenBy(t => t.TrackNumber <= 0 ? int.MaxValue : t.TrackNumber)
                    .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
                    .ToList();

            var removedTrackPlaceholders = removedEntries
                .Where(e => string.IsNullOrWhiteSpace(SelectedAlbum?.Name)
                         || string.Equals(e.Album, SelectedAlbum.Name, StringComparison.OrdinalIgnoreCase))
                .Select(e => new LibraryTrackItem
                {
                    TrackId = string.IsNullOrWhiteSpace(e.TrackId) ? e.ContentHash : e.TrackId,
                    Title = e.Title,
                    Artist = e.Artist,
                    AlbumId = string.IsNullOrWhiteSpace(e.AlbumId) ? ResolveAlbumId(_allAlbumItems, e.Artist, e.Album) : e.AlbumId,
                    AlbumName = e.Album,
                    CoverPath = string.Empty,
                    FilePath = string.Empty,
                    ContentHash = e.ContentHash,
                    SourcePeerUserId = e.PeerUserId,
                    IsReleased = true,
                    Version = 1,
                    TrackNumber = int.MaxValue,
                    Duration = TimeSpan.Zero,
                    PlayCount = 0,
                    IsDownloadPlaceholder = true,
                    IsRemovedFromLibrary = true,
                    DownloadStateLabel = "Not Downloaded"
                })
                .ToList();

            if (!IsMyMusicLibrary && _applicationViewModel != null)
            {
                var existingHashes = filteredTracks
                    .Select(t => t.ContentHash)
                    .Where(static h => !string.IsNullOrWhiteSpace(h))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var activeAlbumName = SelectedAlbum?.Name;
                var activeAlbumArtist = SelectedAlbum?.Artist;
                var pendingPlaceholders = _applicationViewModel.DownloadQueueItems
                    .Where(q => !string.IsNullOrWhiteSpace(q.ContentHash)
                             && (q.State == DownloadState.Pending || q.State == DownloadState.Downloading || q.State == DownloadState.Failed)
                             && !existingHashes.Contains(q.ContentHash)
                             && (string.IsNullOrWhiteSpace(activeAlbumName)
                                 || (string.Equals(q.Album, activeAlbumName, StringComparison.OrdinalIgnoreCase)
                                  && (string.IsNullOrWhiteSpace(activeAlbumArtist)
                                      || string.Equals(q.Artist, activeAlbumArtist, StringComparison.OrdinalIgnoreCase)))))
                    .Select(q => new LibraryTrackItem
                    {
                        TrackId = q.ContentHash,
                        Title = q.Title,
                        Artist = q.Artist,
                        AlbumId = ResolveAlbumId(_allAlbumItems, q.Artist, q.Album),
                        AlbumName = q.Album,
                        CoverPath = string.Empty,
                        FilePath = string.Empty,
                        ContentHash = q.ContentHash,
                        SourcePeerUserId = q.PeerUserId,
                        IsReleased = true,
                        Version = 1,
                        TrackNumber = int.MaxValue,
                        Duration = TimeSpan.Zero,
                        PlayCount = 0,
                        IsDownloadPlaceholder = true,
                        DownloadStateLabel = q.State switch
                        {
                            DownloadState.Pending => "Pending",
                            DownloadState.Downloading => "Downloading",
                            DownloadState.Failed => "Failed",
                            _ => "Pending"
                        }
                    })
                    .OrderBy(p => p.AlbumName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(p => p.Title, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var queueItem in _applicationViewModel.DownloadQueueItems.Where(q => q.State == DownloadState.Done))
                {
                    if (!string.IsNullOrWhiteSpace(queueItem.ContentHash))
                    {
                        _downloadStateService.ClearRemoved(queueItem.ContentHash);
                    }
                }

                var pendingHashes = pendingPlaceholders
                    .Select(p => p.ContentHash)
                    .Where(static h => !string.IsNullOrWhiteSpace(h))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var removedFiltered = removedTrackPlaceholders
                    .Where(p => string.IsNullOrWhiteSpace(activeAlbumName)
                             || string.Equals(p.AlbumName, activeAlbumName, StringComparison.OrdinalIgnoreCase))
                    .Where(p => string.IsNullOrWhiteSpace(p.ContentHash)
                             || (!existingHashes.Contains(p.ContentHash) && !pendingHashes.Contains(p.ContentHash)))
                    .ToList();

                filteredTracks.AddRange(pendingPlaceholders);
                filteredTracks.AddRange(removedFiltered);
            }

            Tracks = filteredTracks;
        }

        private void AnnounceReleasedContentToMesh()
        {
            if (!IsMyMusicLibrary || _applicationViewModel == null || !_applicationViewModel.P2PIsConnected)
                return;

            foreach (var album in _allAlbumItems.Where(a => a.IsReleased))
            {
                if (_autoAnnouncedAlbumIds.Add(album.AlbumId))
                {
                    _applicationViewModel.AnnounceAlbumToNetwork(album.AlbumId, album.Name, album.Artist);
                }
            }

            foreach (var track in _allTrackItems.Where(t => t.IsReleased && !string.IsNullOrWhiteSpace(t.FilePath) && File.Exists(t.FilePath)))
            {
                if (_autoAnnouncedTrackIds.Add(track.TrackId))
                {
                    _applicationViewModel.AnnounceTrackToNetwork(
                        track.TrackId,
                        CryptoService.ComputeFileHash(track.FilePath),
                        track.Title,
                        track.Artist,
                        _allAlbumItems.FirstOrDefault(a => a.AlbumId == track.AlbumId)?.Name ?? string.Empty);
                }
            }
        }

        private IEnumerable<PlaybackTrackListItem> GetCurrentPlaybackContext(Track currentTrack)
        {
            var sameAlbumTracks = _trackObjects
                .Where(t => string.Equals(t.AlbumId, currentTrack.AlbumId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var contextTracks = sameAlbumTracks.Select(t =>
            {
                var path = t.FilePath ?? string.Empty;
                var matchedTrack = _allTrackItems.FirstOrDefault(x => string.Equals(x.TrackId, t.TrackId, StringComparison.OrdinalIgnoreCase));
                var meta = string.IsNullOrWhiteSpace(path) ? null : _myMusicMetadataService.LoadForTrack(path);
                return new PlaybackTrackListItem
                {
                    TrackId = t.TrackId,
                    Title = t.Title,
                    Artist = string.IsNullOrWhiteSpace(t.Description) ? "Unknown Artist" : t.Description!,
                    Duration = t.Duration,
                    FilePath = path,
                    TrackNumber = meta?.TrackNumber ?? 0,
                    PlayCount = matchedTrack?.PlayCount ?? meta?.PlayCount ?? 0
                };
            }).Where(t => !string.IsNullOrWhiteSpace(t.FilePath));

            return contextTracks
                .OrderBy(t => t.TrackNumber <= 0 ? 1 : 0)
                .ThenBy(t => t.TrackNumber <= 0 ? int.MaxValue : t.TrackNumber)
                .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase);
        }

        public Track? GetTrackById(string trackId)
        {
            return _trackObjects.FirstOrDefault(t => t.TrackId == trackId);
        }

        public Album? GetAlbumById(string albumId)
        {
            return _albumObjects.FirstOrDefault(a => a.AlbumId == albumId);
        }

        public void Dispose()
        {
            if (!IsMyMusicLibrary && _applicationViewModel != null)
            {
                _applicationViewModel.DownloadQueueItems.CollectionChanged -= OnDownloadQueueChanged;
            }

            _folderWatcher?.Dispose();
            _importCancellation?.Dispose();
        }
    }
}
