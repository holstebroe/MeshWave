using System.Windows.Input;
using MeshWave.Mvvm;

namespace MeshWave.ViewModels;

/// <summary>
/// View model for library browsing and playback.
/// </summary>
public partial class LibraryViewModel : ViewModelBase
{
    private readonly ApplicationViewModel? _applicationViewModel;
    private string _searchQuery = string.Empty;
    private List<LibraryTrackItem> _tracks = [];
    private List<LibraryAlbumItem> _albums = [];
    private List<LibraryArtistItem> _artists = [];
    private LibraryArtistItem? _selectedArtist;
    private LibraryAlbumItem? _selectedAlbum;
    private bool _isImporting;
    private string _importCurrentFile = string.Empty;
    private string _importStatusMessage = "Idle";
    private int _importTotalFiles;
    private int _importRemainingFiles;
    private int _importImportedFiles;

    public LibraryViewModel(ApplicationViewModel? applicationViewModel = null, bool isMyMusicLibrary = false)
    {
        _applicationViewModel = applicationViewModel;
        IsMyMusicLibrary = isMyMusicLibrary;
        CancelImportCommand = new RelayCommand(_ => CancelImport(), _ => IsImporting);
        LoadFromConfiguredBaseFolder();
    }

    public ICommand CancelImportCommand { get; }
    public bool IsMyMusicLibrary { get; }
    public bool CanImportMyMusic => IsMyMusicLibrary;

    public string SearchQuery
    {
        get => _searchQuery;
        set => SetProperty(ref _searchQuery, value);
    }

    public List<LibraryTrackItem> Tracks
    {
        get => _tracks;
        set => SetProperty(ref _tracks, value);
    }

    public List<LibraryAlbumItem> Albums
    {
        get => _albums;
        set => SetProperty(ref _albums, value);
    }

    public List<LibraryArtistItem> Artists
    {
        get => _artists;
        set => SetProperty(ref _artists, value);
    }

    public LibraryArtistItem? SelectedArtist
    {
        get => _selectedArtist;
        set
        {
            if (SetProperty(ref _selectedArtist, value))
            {
                SelectedAlbum = null;
                RefreshAlbumAndTrackSelection();
            }
        }
    }

    public LibraryAlbumItem? SelectedAlbum
    {
        get => _selectedAlbum;
        set
        {
            if (SetProperty(ref _selectedAlbum, value))
            {
                RefreshAlbumAndTrackSelection();
            }
        }
    }

    public bool IsImporting
    {
        get => _isImporting;
        set
        {
            if (SetProperty(ref _isImporting, value))
            {
                OnPropertyChanged(nameof(CanCancelImport));
            }
        }
    }

    public string ImportCurrentFile
    {
        get => _importCurrentFile;
        set => SetProperty(ref _importCurrentFile, value);
    }

    public string ImportStatusMessage
    {
        get => _importStatusMessage;
        set => SetProperty(ref _importStatusMessage, value);
    }

    public int ImportTotalFiles
    {
        get => _importTotalFiles;
        set => SetProperty(ref _importTotalFiles, value);
    }

    public int ImportRemainingFiles
    {
        get => _importRemainingFiles;
        set => SetProperty(ref _importRemainingFiles, value);
    }

    public int ImportImportedFiles
    {
        get => _importImportedFiles;
        set => SetProperty(ref _importImportedFiles, value);
    }

    public double ImportProgressPercent => ImportTotalFiles == 0
        ? 0
        : Math.Clamp((double)(ImportTotalFiles - ImportRemainingFiles) / ImportTotalFiles * 100.0, 0, 100);

    public bool CanCancelImport => IsImporting;

    public void PlayTrackById(string trackId)
    {
        var track = GetTrackById(trackId);
        if (track == null || string.IsNullOrWhiteSpace(track.FileHash))
        {
            return;
        }

        _applicationViewModel?.PlayTrack(
            track.Title,
            track.Description ?? "Unknown Artist",
            track.Duration,
            track.FileHash);
    }

    public event EventHandler<string>? OpenMetadataEditorRequested;

    public void RequestOpenMetadataEditor(string trackFilePath)
    {
        if (!CanImportMyMusic || string.IsNullOrWhiteSpace(trackFilePath))
        {
            return;
        }

        OpenMetadataEditorRequested?.Invoke(this, trackFilePath);
    }

    public void Search()
    {
        // TODO: Implement library search
    }

    public void RefreshLibrary()
    {
        // TODO: Implement library refresh
    }
}

public sealed class LibraryArtistItem
{
    public required string Name { get; set; }
    public required string CoverPath { get; set; }
    public int AlbumCount { get; set; }
    public int TrackCount { get; set; }
    public override string ToString() => Name;
}

public sealed class LibraryAlbumItem
{
    public required string AlbumId { get; set; }
    public required string Artist { get; set; }
    public required string Name { get; set; }
    public required string CoverPath { get; set; }
    public int TrackCount { get; set; }
    public override string ToString() => Name;
}

public sealed class LibraryTrackItem
{
    public required string TrackId { get; set; }
    public required string Title { get; set; }
    public required string Artist { get; set; }
    public required string AlbumId { get; set; }
    public required string CoverPath { get; set; }
    public required string FilePath { get; set; }
    public override string ToString() => Title;
}
