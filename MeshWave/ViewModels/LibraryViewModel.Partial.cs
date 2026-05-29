using System.Collections.Generic;
using System.Linq;
using MeshWave.LibraryManager;
using MeshWave.Mvvm;
using MeshWave.Common.Core.Models;

namespace MeshWave.ViewModels
{
    public partial class LibraryViewModel : ViewModelBase, IDisposable
    {
        private LocalLibraryManager? _libraryManager;
        private MusicFolderWatcher? _folderWatcher;
        private List<Track> _trackObjects = new();
        private List<Album> _albumObjects = new();

        public void LoadLibrary(string folderPath)
        {
            _folderWatcher?.Dispose();
            _libraryManager = new LocalLibraryManager(folderPath);
            _libraryManager.IndexLibrary();
            _trackObjects = _libraryManager.GetAllTracks().ToList();
            _albumObjects = _libraryManager.GetAllAlbums().ToList();
            Tracks = _trackObjects.Select(t => t.Title).ToList();
            Albums = _albumObjects.Select(a => a.Title).ToList();

            // Set up file watcher for auto-refresh
            _folderWatcher = new MusicFolderWatcher(folderPath, () =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    LoadLibrary(folderPath);
                });
            });
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
        }
    }
}
