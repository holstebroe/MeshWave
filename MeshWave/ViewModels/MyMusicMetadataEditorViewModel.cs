using System.IO;
using System.Windows.Input;
using MeshWave.Models;
using MeshWave.Mvvm;
using MeshWave.Services;

namespace MeshWave.ViewModels
{
    public class MyMusicMetadataEditorViewModel : ViewModelBase
    {
        private readonly MyMusicMetadataService _metadataService = new();
        private string _trackFilePath = string.Empty;
        private string _albumFolderPath = string.Empty;
        private bool _isAlbumEditor;
        private string _title = string.Empty;
        private string _artist = string.Empty;
        private string _album = string.Empty;
        private string _description = string.Empty;
        private string _genre = string.Empty;
        private int _year;
        private int _trackNumber;
        private bool _isReleased;
        private int _version = 1;
        private string _coverArtPath = string.Empty;

        public MyMusicMetadataEditorViewModel()
        {
            SaveCommand = new RelayCommand(_ => Save(), _ => IsAlbumEditor ? !string.IsNullOrWhiteSpace(AlbumFolderPath) : !string.IsNullOrWhiteSpace(TrackFilePath));
            ToggleReleaseCommand = new RelayCommand(_ => ToggleRelease());
        }

        public ICommand SaveCommand { get; }
        public ICommand ToggleReleaseCommand { get; }

        public event EventHandler? RequestClose;

        public string TrackFilePath
        {
            get => _trackFilePath;
            set => SetProperty(ref _trackFilePath, value);
        }

        public string AlbumFolderPath
        {
            get => _albumFolderPath;
            set => SetProperty(ref _albumFolderPath, value);
        }

        public bool IsAlbumEditor
        {
            get => _isAlbumEditor;
            set => SetProperty(ref _isAlbumEditor, value);
        }

        public string EditorTitle => IsAlbumEditor ? "✏️ Local Music Album Metadata Editor" : "✏️ Local Music Track Metadata Editor";

        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public string Artist
        {
            get => _artist;
            set => SetProperty(ref _artist, value);
        }

        public string Album
        {
            get => _album;
            set => SetProperty(ref _album, value);
        }

        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
        }

        public string Genre
        {
            get => _genre;
            set => SetProperty(ref _genre, value);
        }

        public int Year
        {
            get => _year;
            set => SetProperty(ref _year, value);
        }

        public int TrackNumber
        {
            get => _trackNumber;
            set => SetProperty(ref _trackNumber, value < 0 ? 0 : value);
        }

        public bool IsReleased
        {
            get => _isReleased;
            set
            {
                if (SetProperty(ref _isReleased, value))
                {
                    OnPropertyChanged(nameof(ReleaseButtonText));
                }
            }
        }

        public int Version
        {
            get => _version;
            set => SetProperty(ref _version, value < 1 ? 1 : value);
        }

        public string CoverArtPath
        {
            get => _coverArtPath;
            set => SetProperty(ref _coverArtPath, value);
        }

        public string ReleaseButtonText => IsReleased ? "Unrelease" : "Release";

        public void LoadTrack(string trackFilePath)
        {
            IsAlbumEditor = false;
            TrackFilePath = trackFilePath;
            AlbumFolderPath = string.Empty;
            OnPropertyChanged(nameof(EditorTitle));

            var metadata = _metadataService.LoadForTrack(trackFilePath);
            Title = metadata.Title;
            Artist = metadata.Artist;
            Album = metadata.Album;
            Description = metadata.Description;
            Genre = metadata.Genre;
            Year = metadata.Year;
            TrackNumber = metadata.TrackNumber;
            IsReleased = metadata.IsReleased;
            Version = metadata.Version <= 0 ? 1 : metadata.Version;
            CoverArtPath = _metadataService.GetCoverArtPath(trackFilePath);
        }

        public void LoadAlbum(string albumFolderPath)
        {
            IsAlbumEditor = true;
            AlbumFolderPath = albumFolderPath;
            TrackFilePath = string.Empty;
            OnPropertyChanged(nameof(EditorTitle));

            var metadata = _metadataService.LoadForAlbum(albumFolderPath);
            Title = metadata.Title;
            Artist = metadata.Artist;
            Album = metadata.Album;
            Description = metadata.Description;
            Genre = metadata.Genre;
            Year = metadata.Year;
            TrackNumber = metadata.TrackNumber;
            IsReleased = metadata.IsReleased;
            Version = metadata.Version <= 0 ? 1 : metadata.Version;
            CoverArtPath = _metadataService.GetAlbumCoverArtPath(albumFolderPath);
        }

        private void Save()
        {
            var metadata = new MyMusicMetadata
            {
                Title = Title,
                Artist = Artist,
                Album = Album,
                Description = Description,
                Genre = Genre,
                Year = Year,
                TrackNumber = TrackNumber,
                IsReleased = IsReleased,
                Version = Version
            };

            if (IsAlbumEditor)
            {
                _metadataService.SaveForAlbum(AlbumFolderPath, metadata);
                PropagateReleaseStatusToTracks(AlbumFolderPath, IsReleased);
            }
            else
            {
                _metadataService.SaveForTrack(TrackFilePath, metadata);
            }

            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        private void PropagateReleaseStatusToTracks(string albumFolder, bool isReleased)
        {
            if (string.IsNullOrWhiteSpace(albumFolder) || !Directory.Exists(albumFolder))
            {
                return;
            }

            var supportedExtensions = new HashSet<string>(MeshWave.LibraryManager.LocalLibraryManager.SupportedExtensions, StringComparer.OrdinalIgnoreCase);
            var tracks = Directory.EnumerateFiles(albumFolder, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => supportedExtensions.Contains(Path.GetExtension(f)));

            foreach (var trackPath in tracks)
            {
                var trackMeta = _metadataService.LoadForTrack(trackPath);
                if (trackMeta.IsReleased != isReleased)
                {
                    trackMeta.IsReleased = isReleased;
                    _metadataService.SaveForTrack(trackPath, trackMeta);
                }
            }
        }

        private void ToggleRelease()
        {
            IsReleased = !IsReleased;
            Save();
        }
    }
}
