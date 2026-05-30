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
                return new LibraryTrackItem
                {
                    TrackId = t.TrackId,
                    Title = t.Title,
                    Artist = string.IsNullOrWhiteSpace(t.Description) ? "Unknown Artist" : t.Description!,
                    AlbumId = t.AlbumId ?? string.Empty,
                    CoverPath = coverPath,
                    FilePath = resolvedPath,
                    IsReleased = trackMeta.IsReleased,
                    Version = trackMeta.Version <= 0 ? 1 : trackMeta.Version
                };
            }).ToList();

            var albumItems = _albumObjects.Select(a =>
            {
                var tracksInAlbum = trackItems.Where(t => t.AlbumId == a.AlbumId).ToList();
                var coverPath = tracksInAlbum.Select(t => t.CoverPath).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p)) ?? string.Empty;
                var firstTrackPath = tracksInAlbum.Select(t => t.FilePath).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
                var albumFolder = string.IsNullOrWhiteSpace(firstTrackPath) ? string.Empty : Path.GetDirectoryName(firstTrackPath) ?? string.Empty;
                var albumMeta = _myMusicMetadataService.LoadForAlbum(albumFolder);
                return new LibraryAlbumItem
                {
                    AlbumId = a.AlbumId,
                    Artist = tracksInAlbum.FirstOrDefault()?.Artist ?? "Unknown Artist",
                    Name = a.Title,
                    CoverPath = coverPath,
                    TrackCount = tracksInAlbum.Count,
                    IsReleased = albumMeta.IsReleased,
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
                : _allTrackItems.Where(t => t.AlbumId == SelectedAlbum.AlbumId).ToList();
            Tracks = filteredTracks;
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
