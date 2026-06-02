using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MeshWave.Common.Core.Models;

namespace MeshWave.Common.Core;

/// <summary>
/// Thread-safe implementation of ICatalogueService for indexing and searching shared mesh content.
/// </summary>
public class CatalogueService : ICatalogueService
{
    private readonly ConcurrentDictionary<string, CatalogueEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, HashSet<string>> _availability = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public Task IngestAsync(Manifest manifest)
    {
        if (manifest == null) return Task.CompletedTask;

        // 1. Process Snapshot if present
        if (manifest.Snapshot != null)
        {
            foreach (var entity in manifest.Snapshot.EntityStates)
            {
                ProcessEntry(
                    manifest.UserId,
                    entity.TargetId,
                    entity.TargetType,
                    entity.ContentHash,
                    entity.Metadata,
                    manifest.Snapshot.LastSequenceNumber,
                    manifest.Snapshot.Timestamp,
                    isDelete: false);
            }
        }

        // 2. Process Operations
        foreach (var op in (manifest.Operations ?? Enumerable.Empty<ManifestOperation>()).OrderBy(o => o.SequenceNumber))
        {
            if (op.OperationType == ManifestOperationType.Create ||
                op.OperationType == ManifestOperationType.Update ||
                op.OperationType == ManifestOperationType.Delete ||
                op.OperationType == ManifestOperationType.Profile)
            {
                ProcessEntry(
                    manifest.UserId,
                    op.TargetId,
                    op.TargetType,
                    op.ContentHash,
                    op.Metadata,
                    op.SequenceNumber,
                    op.Timestamp,
                    isDelete: op.OperationType == ManifestOperationType.Delete);
            }
        }

        return Task.CompletedTask;
    }

    private void ProcessEntry(
        string userId,
        string targetId,
        string targetType,
        string? contentHash,
        Dictionary<string, string> metadata,
        int sequenceNumber,
        DateTime timestamp,
        bool isDelete)
    {
        // Map targetType string to CatalogueEntryType enum
        CatalogueEntryType? entryType = targetType.ToLowerInvariant() switch
        {
            "track" => CatalogueEntryType.Track,
            "album" => CatalogueEntryType.Album,
            "artist" => CatalogueEntryType.Artist,
            "playlist" => CatalogueEntryType.Playlist,
            "user" => metadata.GetValueOrDefault("isArtist") == "True" ? CatalogueEntryType.Artist : null,
            _ => null
        };

        if (entryType == null) return;

        string entryId = targetId;

        if (isDelete)
        {
            // If it's a delete operation, we only remove if it's the latest operation.
            if (_entries.TryGetValue(entryId, out var existing) && sequenceNumber > existing.SequenceNumber)
            {
                _entries.TryRemove(entryId, out _);
            }
            return;
        }

        // Update Availability
        if (!string.IsNullOrWhiteSpace(contentHash))
        {
            var peers = _availability.GetOrAdd(contentHash, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            lock (peers)
            {
                peers.Add(userId);
            }
        }

        // Staleness Rule: Only apply changes if incoming SequenceNumber is strictly greater than existing.
        if (_entries.TryGetValue(entryId, out var existingEntry))
        {
            if (sequenceNumber <= existingEntry.SequenceNumber)
                return;
        }

        var entry = new CatalogueEntry
        {
            EntryId = entryId,
            Type = entryType.Value,
            OwnerUserId = userId,
            Title = GetTitle(entryType.Value, metadata, targetId),
            ArtistName = metadata.GetValueOrDefault("artist") ?? metadata.GetValueOrDefault("displayName"),
            AlbumName = metadata.GetValueOrDefault("album") ?? metadata.GetValueOrDefault("name"),
            ContentHash = contentHash,
            SequenceNumber = sequenceNumber,
            Timestamp = timestamp,
            Genre = metadata.GetValueOrDefault("genre")
        };

        if (metadata.TryGetValue("duration", out var dStr) && TimeSpan.TryParse(dStr, out var duration))
            entry.Duration = duration;

        if (metadata.TryGetValue("releasedAt", out var rStr) && DateTime.TryParse(rStr, out var releaseDate))
            entry.ReleaseDate = releaseDate;

        _entries[entryId] = entry;
    }

    private static string GetTitle(CatalogueEntryType type, Dictionary<string, string> metadata, string targetId)
    {
        return type switch
        {
            CatalogueEntryType.Track => metadata.GetValueOrDefault("title") ?? targetId,
            CatalogueEntryType.Album => metadata.GetValueOrDefault("name") ?? targetId,
            CatalogueEntryType.Artist => metadata.GetValueOrDefault("displayName") ?? targetId,
            CatalogueEntryType.Playlist => metadata.GetValueOrDefault("title") ?? targetId,
            _ => targetId
        };
    }

    /// <inheritdoc />
    public Task<IEnumerable<CatalogueEntry>> SearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Task.FromResult(_entries.Values.AsEnumerable());

        var keywords = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var results = _entries.Values.Where(e =>
            keywords.All(k =>
                (e.Title?.Contains(k, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (e.ArtistName?.Contains(k, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (e.AlbumName?.Contains(k, StringComparison.OrdinalIgnoreCase) ?? false)
            )
        );

        return Task.FromResult(results);
    }

    /// <inheritdoc />
    public Task<CatalogueEntry?> GetEntryAsync(string entryId)
    {
        _entries.TryGetValue(entryId, out var entry);
        return Task.FromResult(entry);
    }

    /// <inheritdoc />
    public Task<IEnumerable<string>> GetPeersForContentAsync(string contentHash)
    {
        if (string.IsNullOrWhiteSpace(contentHash))
            return Task.FromResult(Enumerable.Empty<string>());

        if (_availability.TryGetValue(contentHash, out var peers))
        {
            lock (peers)
            {
                return Task.FromResult(peers.ToList().AsEnumerable());
            }
        }

        return Task.FromResult(Enumerable.Empty<string>());
    }
}
