using System.IO;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Collections;
using System.ComponentModel;
using MeshWave.LibraryManager;
using MeshWave.Wpf.Models;
using MeshWave.Wpf.Mvvm;
using MeshWave.Wpf.Services;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using System.Windows;

namespace MeshWave.Wpf.ViewModels;

public class MyMusicMetadataEditorViewModel : ViewModelBase, INotifyDataErrorInfo
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
    private string _tags = string.Empty;
    private int _year;
    private int _trackNumber;
    private bool _isReleased;
    private int _version = 1;
    private object? _coverArtSource;

    private readonly Dictionary<string, List<string>> _errors = new();
    public bool HasErrors => _errors.Any();
    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public IEnumerable GetErrors(string? propertyName)
    {
        if (string.IsNullOrEmpty(propertyName) || !_errors.ContainsKey(propertyName))
            return Enumerable.Empty<string>();
        return _errors[propertyName];
    }

    private void AddError(string propertyName, string error)
    {
        if (!_errors.ContainsKey(propertyName))
            _errors[propertyName] = new List<string>();
        if (!_errors[propertyName].Contains(error))
        {
            _errors[propertyName].Add(error);
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
            OnPropertyChanged(nameof(HasErrors));
        }
    }

    private void ClearErrors(string propertyName)
    {
        if (_errors.ContainsKey(propertyName))
        {
            _errors.Remove(propertyName);
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
            OnPropertyChanged(nameof(HasErrors));
        }
    }

    private void ValidateProperties()
    {
        ClearErrors(nameof(Title));
        if (string.IsNullOrWhiteSpace(Title))
            AddError(nameof(Title), "Title cannot be empty.");

        ClearErrors(nameof(Year));
        if (Year < 1000 || Year > 9999)
            AddError(nameof(Year), "Year must be a valid 4-digit number.");
    }

    public MyMusicMetadataEditorViewModel()
    {
        SaveCommand = new RelayCommand(_ => Save(), _ => !HasErrors && (IsAlbumEditor ? !string.IsNullOrWhiteSpace(AlbumFolderPath) : !string.IsNullOrWhiteSpace(TrackFilePath)));
        ToggleReleaseCommand = new RelayCommand(_ => ToggleRelease());
        ReplaceImageCommand = new RelayCommand(_ => ReplaceImage());
    }

    public ICommand SaveCommand { get; }
    public ICommand ToggleReleaseCommand { get; }
    public ICommand ReplaceImageCommand { get; }

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
        set
        {
            if (SetProperty(ref _title, value))
            {
                ValidateProperties();
            }
        }
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

    public string Tags
    {
        get => _tags;
        set => SetProperty(ref _tags, value);
    }

    public int Year
    {
        get => _year;
        set
        {
            if (SetProperty(ref _year, value))
            {
                ValidateProperties();
            }
        }
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
            if (SetProperty(ref _isReleased, value)) OnPropertyChanged(nameof(ReleaseButtonText));
        }
    }

    public int Version
    {
        get => _version;
        set => SetProperty(ref _version, value < 1 ? 1 : value);
    }

    public object? CoverArtSource
    {
        get => _coverArtSource;
        set => SetProperty(ref _coverArtSource, value);
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
        Tags = metadata.Tags;
        Year = metadata.Year;
        TrackNumber = metadata.TrackNumber;
        IsReleased = metadata.IsReleased;
        Version = metadata.Version <= 0 ? 1 : metadata.Version;
        ValidateProperties();
        UpdateCoverArtSource(_metadataService.GetCoverArtPath(trackFilePath));
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
        Tags = metadata.Tags;
        Year = metadata.Year;
        TrackNumber = metadata.TrackNumber;
        IsReleased = metadata.IsReleased;
        Version = metadata.Version <= 0 ? 1 : metadata.Version;
        ValidateProperties();
        UpdateCoverArtSource(_metadataService.GetAlbumCoverArtPath(albumFolderPath));
    }

    private void Save()
    {
        ValidateProperties();
        if (HasErrors)
            return;

        var metadata = new MyMusicMetadata
        {
            Title = Title,
            Artist = Artist,
            Album = Album,
            Description = Description,
            Genre = Genre,
            Tags = Tags,
            Year = Year,
            TrackNumber = TrackNumber,
            IsReleased = IsReleased,
            Version = Version
        };

        var appVm = Application.Current?.MainWindow?.DataContext as ApplicationViewModel;
        string? contentHash = null;
        if (!IsAlbumEditor && !string.IsNullOrWhiteSpace(TrackFilePath) && File.Exists(TrackFilePath))
            contentHash = MeshWave.Common.Core.Crypto.CryptoService.ComputeFileHash(TrackFilePath);

        if (IsAlbumEditor)
        {
            _metadataService.SaveForAlbum(AlbumFolderPath, metadata);
            PropagateReleaseStatusToTracks(AlbumFolderPath, IsReleased);

            if (IsReleased && appVm != null && appVm.P2PIsConnected)
            {
                var tempLibrary = new LibraryViewModel(appVm, isMyMusicLibrary: true);
                string? albumId = null;

                var firstTrackInAlbum = tempLibrary.Tracks.FirstOrDefault(t =>
                    !string.IsNullOrWhiteSpace(t.FilePath) &&
                    string.Equals(Path.GetDirectoryName(t.FilePath), AlbumFolderPath, StringComparison.OrdinalIgnoreCase));

                if (firstTrackInAlbum != null)
                {
                    albumId = firstTrackInAlbum.AlbumId;
                }

                if (!string.IsNullOrWhiteSpace(albumId))
                {
                    appVm.UpdateAlbumInNetwork(albumId, metadata.Title, metadata.Artist);
                }
            }
        }
        else
        {
            _metadataService.SaveForTrack(TrackFilePath, metadata);

            if (IsReleased && appVm != null && appVm.P2PIsConnected && !string.IsNullOrWhiteSpace(contentHash))
            {
                var tempLibrary = new LibraryViewModel(appVm, isMyMusicLibrary: true);
                string? trackId = null;

                var matchedTrack = tempLibrary.Tracks.FirstOrDefault(t =>
                    string.Equals(t.FilePath, TrackFilePath, StringComparison.OrdinalIgnoreCase));

                if (matchedTrack != null)
                {
                    trackId = matchedTrack.TrackId;
                }

                if (!string.IsNullOrWhiteSpace(trackId))
                {
                    appVm.UpdateTrackInNetwork(trackId, contentHash, metadata.Title, metadata.Artist, metadata.Album);
                }
            }
        }

        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    private void PropagateReleaseStatusToTracks(string albumFolder, bool isReleased)
    {
        if (string.IsNullOrWhiteSpace(albumFolder) || !Directory.Exists(albumFolder)) return;

        var supportedExtensions = new HashSet<string>(LocalLibraryManager.SupportedExtensions, StringComparer.OrdinalIgnoreCase);
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

    private void ReplaceImage()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Image Files|*.jpg;*.jpeg;*.png;*.webp|All Files|*.*",
            Title = "Select New Album Art"
        };

        if (dialog.ShowDialog() == true)
        {
            var filePath = IsAlbumEditor ? GetFirstTrackPath(AlbumFolderPath) : TrackFilePath;
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                _metadataService.SaveCoverArt(filePath, dialog.FileName);

                if (IsAlbumEditor)
                {
                    // Update all tracks in the album to use the same cover
                    var supportedExtensions = new HashSet<string>(LocalLibraryManager.SupportedExtensions, StringComparer.OrdinalIgnoreCase);
                    var tracks = Directory.EnumerateFiles(AlbumFolderPath, "*.*", SearchOption.TopDirectoryOnly)
                        .Where(f => supportedExtensions.Contains(Path.GetExtension(f)));

                    foreach (var trackPath in tracks) _metadataService.SaveCoverArt(trackPath, dialog.FileName);
                }

                UpdateCoverArtSource(_metadataService.GetCoverArtPath(filePath));
            }
        }
    }

    private void UpdateCoverArtSource(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            CoverArtSource = null;
            return;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.EndInit();
            bitmap.Freeze(); // Crucial for cross-thread UI access
            CoverArtSource = bitmap;
        }
        catch
        {
            CoverArtSource = null;
        }
    }

    private static string? TryReadIdFile(string folderPath)
    {
        try
        {
            var idFilePath = Path.Combine(folderPath, ".meshwave-id");
            if (File.Exists(idFilePath))
            {
                var id = File.ReadAllText(idFilePath).Trim();
                if (!string.IsNullOrWhiteSpace(id)) return id;
            }
        }
        catch { }
        return null;
    }

    private string GetFirstTrackPath(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath)) return string.Empty;
        var supportedExtensions = new HashSet<string>(LocalLibraryManager.SupportedExtensions, StringComparer.OrdinalIgnoreCase);
        return Directory.EnumerateFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(f => supportedExtensions.Contains(Path.GetExtension(f))) ?? string.Empty;
    }
}