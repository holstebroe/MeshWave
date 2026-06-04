using MeshWave.Mvvm;
using System.Collections.ObjectModel;

namespace MeshWave.Services;

public enum DownloadState { Pending, Downloading, Done, Failed }

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
            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(IsDone));
            OnPropertyChanged(nameof(IsFailed));
            OnPropertyChanged(nameof(IsPending));
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
        _ => "Unknown"
    };

    public string StateColor => State switch
    {
        DownloadState.Pending => "#888888",
        DownloadState.Downloading => "#1DB954",
        DownloadState.Done => "#4CAF50",
        DownloadState.Failed => "#E53935",
        _ => "#888888"
    };

    public bool IsActive => State == DownloadState.Downloading;
    public bool IsDone => State == DownloadState.Done;
    public bool IsFailed => State == DownloadState.Failed;
    public bool IsPending => State == DownloadState.Pending;
}

/// <summary>
/// Singleton download queue — holds pending/active/completed download requests.
/// Actual download execution is handled by BrowseViewModel or ApplicationViewModel.
/// </summary>
public class DownloadQueueService
{
    private readonly ObservableCollection<DownloadQueueItem> _items = [];

    public IReadOnlyObservableCollection Items => new ReadOnlyObservableCollectionWrapper(_items);
    public ObservableCollection<DownloadQueueItem> AllItems => _items;

    public DownloadQueueItem Enqueue(string peerUserId, string contentHash, string title, string artist, string album, string targetType = "Track", string? trackId = null)
    {
        // Avoid duplicates by contentHash
        var existing = _items.FirstOrDefault(i =>
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
        _items.Add(item);
        return item;
    }

    public bool IsQueued(string contentHash) =>
        _items.Any(i => string.Equals(i.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase)
                     && (i.State == DownloadState.Pending || i.State == DownloadState.Downloading));

    public void Remove(DownloadQueueItem item) => _items.Remove(item);
    public void ClearCompleted() =>
        _items.Where(i => i.IsDone).ToList().ForEach(i => _items.Remove(i));

    // Simple wrapper interfaces
    public interface IReadOnlyObservableCollection : IEnumerable<DownloadQueueItem> { }

    private class ReadOnlyObservableCollectionWrapper(ObservableCollection<DownloadQueueItem> inner)
        : IReadOnlyObservableCollection
    {
        public IEnumerator<DownloadQueueItem> GetEnumerator() => inner.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => inner.GetEnumerator();
    }
}
