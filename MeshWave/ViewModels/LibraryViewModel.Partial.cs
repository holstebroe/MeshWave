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
        private LocalLibraryManager? _libraryManager;
        private MusicFolderWatcher? _folderWatcher;
        private List<Track> _trackObjects = new();
        private List<Album> _albumObjects = new();
        private CancellationTokenSource? _importCancellation;

        public void LoadFromConfiguredBaseFolder()
        {
            _settingsService.EnsureFoldersExist();
            var settings = _settingsService.LoadSettings();
            var myMusicFolder = _settingsService.GetMyMusicFolder();
            LoadLibrary(myMusicFolder, settings.SupportedExtensions);
        }

        public async Task ImportMyMusicAsync(string sourceFolder)
        {
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

        public void LoadLibrary(string folderPath, IEnumerable<string>? supportedExtensions = null)
        {
            _folderWatcher?.Dispose();
            _libraryManager = new LocalLibraryManager(folderPath, supportedExtensions);
            _libraryManager.IndexLibrary();
            _trackObjects = _libraryManager.GetAllTracks().ToList();
            _albumObjects = _libraryManager.GetAllAlbums().ToList();
            Tracks = _trackObjects.Select(t => t.Title).ToList();
            Albums = _albumObjects.Select(a => a.Title).ToList();
            Artists = _trackObjects
                .Select(t => string.IsNullOrWhiteSpace(t.Description) ? "Unknown Artist" : t.Description!)
                .Distinct()
                .OrderBy(a => a)
                .ToList();

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

        public Track? GetTrackByTitle(string title)
        {
            return _trackObjects.FirstOrDefault(t => t.Title == title);
        }

        public Album? GetAlbumByTitle(string title)
        {
            return _albumObjects.FirstOrDefault(a => a.Title == title);
        }

        public void Dispose()
        {
            _folderWatcher?.Dispose();
            _importCancellation?.Dispose();
        }
    }
}
