using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Input;
using MeshWave.Common.Core.Models;
using MeshWave.Synchronizer;
using MeshWave.Wpf.Mvvm;
using MeshWave.Wpf.Services;
using MeshWave.Wpf.Models;

namespace MeshWave.Wpf.ViewModels.Items;

public class BrowsePlaylistItem : ViewModelBase
{
    private string _iconPath = string.Empty;
    private bool _isLocal;

    public string PlaylistId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ArtistUserId { get; set; } = string.Empty;
    public string ArtistDisplayName { get; set; } = string.Empty;
    public int TrackCount { get; set; }
    public List<string> TrackIds { get; set; } = [];
    public DateTime? ReleasedAt { get; set; }
    public string ReleasedAtDisplay => ReleasedAt.HasValue ? ReleasedAt.Value.ToLocalTime().ToString("MMM d, yyyy") : string.Empty;
    public string IconPath
    {
        get => _iconPath;
        set => SetProperty(ref _iconPath, value);
    }
    public bool IsLocal
    {
        get => _isLocal;
        set => SetProperty(ref _isLocal, value);
    }
}
