using System.Windows.Input;
using MeshWave.Mvvm;

namespace MeshWave.ViewModels;

/// <summary>
/// View model for library browsing and playback.
/// </summary>
public partial class LibraryViewModel : ViewModelBase
{
    private string _searchQuery = string.Empty;
    private List<string> _tracks = [];
    private List<string> _albums = [];
    private List<string> _artists = [];
    private bool _isImporting;
    private string _importCurrentFile = string.Empty;
    private string _importStatusMessage = "Idle";
    private int _importTotalFiles;
    private int _importRemainingFiles;
    private int _importImportedFiles;

    public LibraryViewModel()
    {
        CancelImportCommand = new RelayCommand(_ => CancelImport(), _ => IsImporting);
        LoadFromConfiguredBaseFolder();
    }

    public ICommand CancelImportCommand { get; }

    public string SearchQuery
    {
        get => _searchQuery;
        set => SetProperty(ref _searchQuery, value);
    }

    public List<string> Tracks
    {
        get => _tracks;
        set => SetProperty(ref _tracks, value);
    }

    public List<string> Albums
    {
        get => _albums;
        set => SetProperty(ref _albums, value);
    }

    public List<string> Artists
    {
        get => _artists;
        set => SetProperty(ref _artists, value);
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

    public void Search()
    {
        // TODO: Implement library search
    }

    public void RefreshLibrary()
    {
        // TODO: Implement library refresh
    }
}
