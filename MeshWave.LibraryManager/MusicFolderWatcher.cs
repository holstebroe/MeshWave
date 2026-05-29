using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MeshWave.LibraryManager
{
    /// <summary>
    /// Watches a folder for music file changes and triggers re-indexing.
    /// </summary>
    public class MusicFolderWatcher : IDisposable
    {
        private readonly FileSystemWatcher _watcher;
        private readonly Action _onChanged;
        private readonly HashSet<string> _supportedExtensions;
        private DateTime _lastTriggerUtc = DateTime.MinValue;

        public MusicFolderWatcher(string folderPath, Action onChanged)
            : this(folderPath, LocalLibraryManager.SupportedExtensions, onChanged)
        {
        }

        public MusicFolderWatcher(string folderPath, IEnumerable<string> supportedExtensions, Action onChanged)
        {
            _onChanged = onChanged;
            _supportedExtensions = new HashSet<string>(
                supportedExtensions.Select(ext => ext.StartsWith('.') ? ext : $".{ext}"),
                StringComparer.OrdinalIgnoreCase);

            _watcher = new FileSystemWatcher(folderPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };

            _watcher.Created += OnChanged;
            _watcher.Deleted += OnChanged;
            _watcher.Renamed += OnChanged;
        }

        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            if (!ShouldTrigger(e.FullPath))
            {
                return;
            }

            var now = DateTime.UtcNow;
            if ((now - _lastTriggerUtc).TotalMilliseconds < 500)
            {
                return;
            }

            _lastTriggerUtc = now;
            _onChanged();
        }

        private bool ShouldTrigger(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            if (path.Contains($"{Path.DirectorySeparatorChar}.cache{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                path.Contains($"{Path.DirectorySeparatorChar}.comments{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var extension = Path.GetExtension(path);
            return _supportedExtensions.Contains(extension);
        }

        public void Dispose() => _watcher.Dispose();
    }
}
