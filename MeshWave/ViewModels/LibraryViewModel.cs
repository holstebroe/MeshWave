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

    public void Search()
    {
        // TODO: Implement library search
    }

    public void RefreshLibrary()
    {
        // TODO: Implement library refresh
    }
}
