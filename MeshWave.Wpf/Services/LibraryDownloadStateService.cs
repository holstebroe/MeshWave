using System.IO;
using System.Text.Json;
using MeshWave.Common.Core;

namespace MeshWave.Wpf.Services;

public sealed class LibraryDownloadStateService
{
    private readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private List<RemovedLibraryTrackEntry>? _removedCache;
    private List<DownloadedTrackEntry>? _downloadedCache;
    private readonly string _appDataRoot;

    public LibraryDownloadStateService(IMeshWaveEnvironment environment, string? appDataRoot = null)
    {
        _appDataRoot = appDataRoot ?? environment.GetAppDataRoot();
    }

    public IReadOnlyList<RemovedLibraryTrackEntry> GetRemovedEntries()
    {
        lock (Sync)
        {
            return LoadInternal();
        }
    }

    public void MarkRemoved(RemovedLibraryTrackEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.ContentHash))
            return;

        lock (Sync)
        {
            var entries = LoadInternal();
            entries.RemoveAll(e => string.Equals(e.ContentHash, entry.ContentHash, StringComparison.OrdinalIgnoreCase));
            entries.Add(entry);
            SaveInternal(entries);
            _removedCache = entries;
        }
    }

    public void ClearRemoved(string? contentHash)
    {
        if (string.IsNullOrWhiteSpace(contentHash))
            return;

        lock (Sync)
        {
            var entries = LoadInternal();
            var removed = entries.RemoveAll(e => string.Equals(e.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase));
            if (removed > 0)
            {
                SaveInternal(entries);
                _removedCache = entries;
            }
        }
    }

    private string GetPath()
    {
        return Path.Combine(_appDataRoot, "removed-library-tracks.json");
    }

    private List<RemovedLibraryTrackEntry> LoadInternal()
    {
        if (_removedCache != null) return [.. _removedCache];

        var path = GetPath();
        if (!File.Exists(path))
            return [];

        try
        {
            var json = File.ReadAllText(path);
            _removedCache = JsonSerializer.Deserialize<List<RemovedLibraryTrackEntry>>(json) ?? [];
            return [.. _removedCache];
        }
        catch
        {
            return [];
        }
    }

    private void SaveInternal(List<RemovedLibraryTrackEntry> entries)
    {
        var path = GetPath();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(entries, JsonOptions);
        File.WriteAllText(path, json);
    }

    public IReadOnlyList<DownloadedTrackEntry> GetDownloadedEntries()
    {
        lock (Sync)
        {
            return LoadDownloadedInternal();
        }
    }

    public void MarkDownloaded(string trackId, string contentHash, int sequenceNumber)
    {
        if (string.IsNullOrWhiteSpace(trackId) || string.IsNullOrWhiteSpace(contentHash))
            return;

        lock (Sync)
        {
            var entries = LoadDownloadedInternal();
            entries.RemoveAll(e => string.Equals(e.TrackId, trackId, StringComparison.OrdinalIgnoreCase));
            entries.Add(new DownloadedTrackEntry
            {
                TrackId = trackId,
                ContentHash = contentHash,
                SequenceNumber = sequenceNumber
            });
            SaveDownloadedInternal(entries);
            _downloadedCache = entries;
        }
    }

    private string GetDownloadedPath()
    {
        return Path.Combine(_appDataRoot, "downloaded-tracks.json");
    }

    private List<DownloadedTrackEntry> LoadDownloadedInternal()
    {
        if (_downloadedCache != null) return [.. _downloadedCache];

        var path = GetDownloadedPath();
        if (!File.Exists(path))
            return [];

        try
        {
            var json = File.ReadAllText(path);
            _downloadedCache = JsonSerializer.Deserialize<List<DownloadedTrackEntry>>(json) ?? [];
            return [.. _downloadedCache];
        }
        catch
        {
            return [];
        }
    }

    private void SaveDownloadedInternal(List<DownloadedTrackEntry> entries)
    {
        var path = GetDownloadedPath();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(entries, JsonOptions);
        File.WriteAllText(path, json);
    }
}

public sealed class DownloadedTrackEntry
{
    public required string TrackId { get; set; }
    public required string ContentHash { get; set; }
    public int SequenceNumber { get; set; }
    public DateTime DownloadedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class RemovedLibraryTrackEntry
{
    public string ContentHash { get; set; } = string.Empty;
    public string TrackId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    public string AlbumId { get; set; } = string.Empty;
    public string PeerUserId { get; set; } = string.Empty;
    public DateTime RemovedAtUtc { get; set; } = DateTime.UtcNow;
}
