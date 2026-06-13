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

public class BrowseTrackItem : ViewModelBase
{
    private bool _isQueued;
    private bool _isDownloaded;
    private bool _isLocal;
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

    public bool IsQueued
    {
        get => _isQueued;
        set
        {
            SetProperty(ref _isQueued, value);
            OnPropertyChanged(nameof(DownloadButtonLabel));
            OnPropertyChanged(nameof(CanDownload));
        }
    }

    public bool IsDownloaded
    {
        get => _isDownloaded;
        set
        {
            SetProperty(ref _isDownloaded, value);
            OnPropertyChanged(nameof(DownloadButtonLabel));
            OnPropertyChanged(nameof(CanDownload));
        }
    }

    public bool IsLocal
    {
        get => _isLocal;
        set
        {
            SetProperty(ref _isLocal, value);
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
        IsLocal ? "Local" :
        IsQueued ? "⏳ Queued" :
        NeedsUpdate ? "Update Available" :
        IsDownloaded ? "✅ Downloaded" :
        "⬇ Download";

    public bool CanDownload => !IsLocal && !IsQueued && (!IsDownloaded || NeedsUpdate) && (!string.IsNullOrWhiteSpace(ContentHash) || !string.IsNullOrWhiteSpace(CompressedContentHash));
}
