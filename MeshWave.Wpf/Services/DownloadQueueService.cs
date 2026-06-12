using System.Collections;
using System.Collections.ObjectModel;
using MeshWave.Wpf.Mvvm;

namespace MeshWave.Wpf.Services;

public enum DownloadState { Pending, Downloading, Done, Failed, Paused }

/// <summary>
/// Represents a single queued download request from a peer.
/// </summary>
public class DownloadQueueItem : ViewModelBase
{
    private DownloadState _state = DownloadState.Pending;
    private int _percentComplete;
    private string _statusMessage = string.Empty;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? TrackId { get; set; }
    public string PeerUserId { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    public string TargetType { get; set; } = "Track"; // "Track" or "Album"
    public DateTime EnqueuedAt { get; set; } = DateTime.UtcNow;

    public DownloadState State
    {
        get => _state;
        set
        {
            SetProperty(ref _state, value);
            OnPropertyChanged(nameof(StateLabel));
            OnPropertyChanged(nameof(StateColor));
            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(IsDone));
            OnPropertyChanged(nameof(IsFailed));
            OnPropertyChanged(nameof(IsPending));
            OnPropertyChanged(nameof(IsPaused));
        }
    }

    public int PercentComplete
    {
        get => _percentComplete;
        set => SetProperty(ref _percentComplete, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string StateLabel => State switch
    {
        DownloadState.Pending => "Pending",
        DownloadState.Downloading => "Downloading…",
        DownloadState.Done => "Done",
        DownloadState.Failed => "Failed",
        DownloadState.Paused => "Paused",
        _ => "Unknown"
    };

    public string StateColor => State switch
    {
        DownloadState.Pending => "#888888",
        DownloadState.Downloading => "#1DB954",
        DownloadState.Done => "#4CAF50",
        DownloadState.Failed => "#E53935",
        DownloadState.Paused => "#FFA000",
        _ => "#888888"
    };

    public bool IsActive => State == DownloadState.Downloading;
    public bool IsDone => State == DownloadState.Done;
    public bool IsFailed => State == DownloadState.Failed;
    public bool IsPending => State == DownloadState.Pending;
    public bool IsPaused => State == DownloadState.Paused;
}

/// <summary>
/// Singleton download queue — holds pending/active/completed download requests.
/// Actual download execution is handled by BrowseViewModel or ApplicationViewModel.
/// </summary>
public class DownloadQueueService
{
    public IReadOnlyObservableCollection Items => new ReadOnlyObservableCollectionWrapper(AllItems);
    public ObservableCollection<DownloadQueueItem> AllItems { get; } = [];

    public DownloadQueueItem Enqueue(string peerUserId, string contentHash, string title, string artist, string album, string targetType = "Track", string? trackId = null)
    {
        // Avoid duplicates by contentHash
        var existing = AllItems.FirstOrDefault(i =>
            string.Equals(i.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase)
            && i.State != DownloadState.Failed);
        if (existing != null)
            return existing;

        var item = new DownloadQueueItem
        {
            PeerUserId = peerUserId,
            ContentHash = contentHash,
            Title = title,
            Artist = artist,
            Album = album,
            TargetType = targetType,
            TrackId = trackId
        };
        AllItems.Add(item);
        return item;
    }

    public bool IsQueued(string contentHash)
    {
        return AllItems.Any(i => string.Equals(i.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase)
                               && (i.State == DownloadState.Pending || i.State == DownloadState.Downloading));
    }

    public void Remove(DownloadQueueItem item)
    {
        AllItems.Remove(item);
    }

    public void ClearCompleted()
    {
        AllItems.Where(i => i.IsDone).ToList().ForEach(i => AllItems.Remove(i));
    }

    // Simple wrapper interfaces
    public interface IReadOnlyObservableCollection : IEnumerable<DownloadQueueItem> { }

    private class ReadOnlyObservableCollectionWrapper(ObservableCollection<DownloadQueueItem> inner)
        : IReadOnlyObservableCollection
    {
        public IEnumerator<DownloadQueueItem> GetEnumerator()
        {
            return inner.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return inner.GetEnumerator();
        }
    }
}
