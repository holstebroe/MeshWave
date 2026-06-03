using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MeshWave.Common.Core.Models;

namespace MeshWave.Common.Core;

/// <summary>
/// Implementation of ICatalogueService providing a searchable, deduplicated index of shared metadata.
/// </summary>
public class CatalogueService : ICatalogueService
{
    private readonly ConcurrentDictionary<string, CatalogueEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _lastSequenceNumbers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, bool>> _peerAvailability = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public Task IngestAsync(Manifest manifest)
    {
        if (manifest == null) return Task.CompletedTask;

        // 1. Process Snapshot if present
        if (manifest.Snapshot != null)
        {
            foreach (var state in manifest.Snapshot.EntityStates)
            {
                UpdateEntry(
                    manifest.UserId,
                    state.TargetId,
                    state.TargetType,
                    state.ContentHash,
                    state.Metadata,
                    manifest.Snapshot.LastSequenceNumber,
                    manifest.Snapshot.Timestamp,
                    isDelete: false);
            }
        }

        // 2. Process Operations
        foreach (var op in manifest.Operations)
        {
            if (op.OperationType == ManifestOperationType.Create ||
                op.OperationType == ManifestOperationType.Update ||
                op.OperationType == ManifestOperationType.Delete)
            {
                UpdateEntry(
                    manifest.UserId,
                    op.TargetId,
                    op.TargetType,
                    op.ContentHash,
                    op.Metadata,
                    op.SequenceNumber,
                    op.Timestamp,
                    op.OperationType == ManifestOperationType.Delete);
            }
        }

        return Task.CompletedTask;
    }

    public Task<IEnumerable<CatalogueEntry>> SearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Task.FromResult(Enumerable.Empty<CatalogueEntry>());

        var results = _entries.Values.Where(e =>
            (e.Title?.Contains(query, StringComparison.OrdinalIgnoreCase) == true) ||
            (e.ArtistName?.Contains(query, StringComparison.OrdinalIgnoreCase) == true) ||
            (e.AlbumName?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
        ).ToList();

        return Task.FromResult<IEnumerable<CatalogueEntry>>(results);
    }

    public Task<CatalogueEntry?> GetEntryAsync(string entryId)
    {
        if (string.IsNullOrWhiteSpace(entryId)) return Task.FromResult<CatalogueEntry?>(null);
        _entries.TryGetValue(entryId, out var entry);
        return Task.FromResult(entry);
    }

    public Task<IEnumerable<string>> GetPeersForContentAsync(string contentHash)
    {
        if (string.IsNullOrWhiteSpace(contentHash))
            return Task.FromResult(Enumerable.Empty<string>());

        if (_peerAvailability.TryGetValue(contentHash, out var peers))
        {
            return Task.FromResult<IEnumerable<string>>(peers.Keys.ToList());
        }

        return Task.FromResult(Enumerable.Empty<string>());
    }

    private void UpdateEntry(string userId, string targetId, string targetType, string? contentHash, Dictionary<string, string> metadata, int sequenceNumber, DateTime timestamp, bool isDelete)
    {
        if (string.IsNullOrWhiteSpace(targetId)) return;
        if (targetType != "Artist" && targetType != "Album" && targetType != "Track" && targetType != "Playlist") return;

        lock (_lock)
        {
            // Staleness Rule: Only apply if incoming SequenceNumber is greater than existing for this TargetId
            if (_lastSequenceNumbers.TryGetValue(targetId, out var existingSeq) && sequenceNumber <= existingSeq)
                return;

            _lastSequenceNumbers[targetId] = sequenceNumber;

            if (isDelete)
            {
                _entries.TryRemove(targetId, out _);
            }
            else
            {
                var entry = new CatalogueEntry
                {
                    EntryId = targetId,
                    Type = MapType(targetType),
                    OwnerUserId = userId,
                    Title = GetTitle(targetId, metadata),
                    ArtistName = metadata.GetValueOrDefault("artist") ?? metadata.GetValueOrDefault("artistName"),
                    AlbumName = metadata.GetValueOrDefault("album") ?? metadata.GetValueOrDefault("albumName"),
                    Duration = ParseDuration(metadata.GetValueOrDefault("duration")),
                    ContentHash = contentHash,
                    ReleaseDate = ParseDate(metadata.GetValueOrDefault("releasedAt") ?? metadata.GetValueOrDefault("releaseDate")),
                    Genre = metadata.GetValueOrDefault("genre"),
                    FileSize = long.TryParse(metadata.GetValueOrDefault("fileSize"), out var fs) ? fs : 0,
                    SequenceNumber = sequenceNumber,
                    Timestamp = timestamp
                };
                _entries[targetId] = entry;
            }

            // Update Peer Availability
            if (!string.IsNullOrEmpty(contentHash))
            {
                var peers = _peerAvailability.GetOrAdd(contentHash, _ => new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase));
                if (isDelete)
                    peers.TryRemove(userId, out _);
                else
                    peers.TryAdd(userId, true);
            }
        }
    }

    private static CatalogueEntryType MapType(string targetType)
    {
        return targetType switch
        {
            "Artist" => CatalogueEntryType.Artist,
            "Album" => CatalogueEntryType.Album,
            "Playlist" => CatalogueEntryType.Playlist,
            _ => CatalogueEntryType.Track
        };
    }

    private static string GetTitle(string targetId, Dictionary<string, string> metadata)
    {
        if (metadata.TryGetValue("title", out var title) && !string.IsNullOrWhiteSpace(title)) return title;
        if (metadata.TryGetValue("displayName", out var dn) && !string.IsNullOrWhiteSpace(dn)) return dn;
        if (metadata.TryGetValue("name", out var name) && !string.IsNullOrWhiteSpace(name)) return name;
        return targetId;
    }

    private static TimeSpan? ParseDuration(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (TimeSpan.TryParse(value, out var ts)) return ts;
        if (double.TryParse(value, out var seconds)) return TimeSpan.FromSeconds(seconds);
        return null;
    }

    private static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (DateTime.TryParse(value, out var dt)) return dt;
        return null;
    }
}
