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
using MeshWave.Common.Core.Enums;

namespace MeshWave.Wpf.ViewModels.Items;

public class BrowseTrackItem : ViewModelBase
{
    private TrackAvailabilityState _availabilityState = TrackAvailabilityState.Remote;
    private bool _needsUpdate;
    private string _iconPath = string.Empty;

    public string TrackId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ArtistUserId { get; set; } = string.Empty;
    public string ArtistDisplayName { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    public string? ContentHash { get; set; }
    public string? CompressedContentHash { get; set; }
    public long FileSize { get; set; }
    public string FileSizeDisplay { get; set; } = string.Empty;
    public DateTime? ReleasedAt { get; set; }
    public string ReleasedAtDisplay => ReleasedAt.HasValue ? ReleasedAt.Value.ToLocalTime().ToString("MMM d, yyyy") : string.Empty;

    public TrackAvailabilityState AvailabilityState
    {
        get => _availabilityState;
        set
        {
            SetProperty(ref _availabilityState, value);
            OnPropertyChanged(nameof(DownloadButtonLabel));
            OnPropertyChanged(nameof(CanDownload));
        }
    }

    public bool NeedsUpdate
    {
        get => _needsUpdate;
        set
        {
            SetProperty(ref _needsUpdate, value);
            OnPropertyChanged(nameof(DownloadButtonLabel));
            OnPropertyChanged(nameof(CanDownload));
        }
    }

    public string IconPath
    {
        get => _iconPath;
        set => SetProperty(ref _iconPath, value);
    }

    public string DownloadButtonLabel =>
        AvailabilityState == TrackAvailabilityState.Local ? "Local" :
        AvailabilityState == TrackAvailabilityState.Pending ? "⏳ Pending" :
        NeedsUpdate ? "Update Available" :
        AvailabilityState == TrackAvailabilityState.Downloaded ? "✅ Downloaded" :
        "⬇ Remote";

    public bool CanDownload => (AvailabilityState == TrackAvailabilityState.Remote || NeedsUpdate) && (!string.IsNullOrWhiteSpace(ContentHash) || !string.IsNullOrWhiteSpace(CompressedContentHash));
}
