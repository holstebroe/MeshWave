using System;
using System.IO;

namespace MeshWave.LibraryManager
{
    /// <summary>
    /// Watches a folder for music file changes and triggers re-indexing.
    /// </summary>
    public class MusicFolderWatcher : IDisposable
    {
        private readonly FileSystemWatcher _watcher;
        private readonly Action _onChanged;

        public MusicFolderWatcher(string folderPath, Action onChanged)
        {
            _onChanged = onChanged;
            _watcher = new FileSystemWatcher(folderPath)
            {
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };
            _watcher.Created += OnChanged;
            _watcher.Deleted += OnChanged;
            _watcher.Renamed += OnChanged;
            _watcher.Changed += OnChanged;
        }

        private void OnChanged(object sender, FileSystemEventArgs e) => _onChanged();

        public void Dispose() => _watcher.Dispose();
    }
}
