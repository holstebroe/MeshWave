using System.IO;
using System.Text.Json;
using MeshWave.Common.Core;

namespace MeshWave.Services;

public sealed class LibraryDownloadStateService
{
    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

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
                SaveInternal(entries);
        }
    }

    private static string GetPath() => MeshWaveEnvironment.CombineInAppData("removed-library-tracks.json");

    private static List<RemovedLibraryTrackEntry> LoadInternal()
    {
        var path = GetPath();
        if (!File.Exists(path))
            return [];

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<RemovedLibraryTrackEntry>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static void SaveInternal(List<RemovedLibraryTrackEntry> entries)
    {
        var path = GetPath();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(entries, JsonOptions);
        File.WriteAllText(path, json);
    }
}

public sealed class RemovedLibraryTrackEntry
{
    public string ContentHash { get; set; } = string.Empty;
    public string TrackId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    public string AlbumId { get; set; } = string.Empty;
    public DateTime RemovedAtUtc { get; set; } = DateTime.UtcNow;
}
