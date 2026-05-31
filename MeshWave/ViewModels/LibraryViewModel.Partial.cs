using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
                ? _settingsService.GetMyMusicFolder()
                : _settingsService.GetOtherMusicFolder();
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
            var myMusicFolder = _settingsService.GetMyMusicFolder();

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
            var myMusicFolder = _settingsService.GetMyMusicFolder();

            var imported = LocalLibraryManager.ImportSingleFileToOrganizedStructure(sourceFile, myMusicFolder, settings.SupportedExtensions);
            ImportSingleFileStatus = imported
                ? "File imported successfully."
                : "File already exists or is unsupported.";

            LoadFromConfiguredBaseFolder();
        }

        public void LoadLibrary(string folderPath, IEnumerable<string>? supportedExtensions = null)
        {
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
                    CoverPath = coverPath,
                    FilePath = resolvedPath,
                    IsReleased = effectiveRelease,
                    Version = trackMeta.Version <= 0 ? 1 : trackMeta.Version,
                    TrackNumber = trackMeta.TrackNumber,
                    Duration = t.Duration,
                    PlayCount = trackMeta.PlayCount
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
            SelectedArtist = artistItems.FirstOrDefault();
            _allAlbumItems = albumItems;
            _allTrackItems = trackItems;
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

        private void RefreshAlbumAndTrackSelection()
        {
            var filteredAlbums = SelectedArtist == null
                ? _allAlbumItems
                : _allAlbumItems.Where(a => a.Artist == SelectedArtist.Name).ToList();
            Albums = filteredAlbums;

            var filteredTracks = SelectedAlbum == null
                ? []
                : _allTrackItems
                    .Where(t => t.AlbumId == SelectedAlbum.AlbumId)
                    .OrderBy(t => t.TrackNumber <= 0 ? 1 : 0)
                    .ThenBy(t => t.TrackNumber <= 0 ? int.MaxValue : t.TrackNumber)
                    .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
                    .ToList();
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
                        MeshWave.Common.Core.Crypto.CryptoService.ComputeFileHash(track.FilePath),
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
            _folderWatcher?.Dispose();
            _importCancellation?.Dispose();
        }
    }
}
