using System.Collections.Concurrent;
using MeshWave.Common.Core.Models;
using MeshWave.Common.Core.Processors;

namespace MeshWave.Common.Core;

/// <summary>
/// Implementation of ICatalogueService providing a searchable, deduplicated index of shared metadata.
/// </summary>
public class CatalogueService : ICatalogueService
{
    private readonly ConcurrentDictionary<string, CatalogueEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _lastSequenceNumbers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, bool>> _peerAvailability = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ICatalogueEntryProcessor> _processors = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public CatalogueService(IEnumerable<ICatalogueEntryProcessor> processors)
    {
        foreach (var processor in processors)
        {
            _processors[processor.TargetType.ToString()] = processor;
        }
    }

    public Task IngestAsync(Manifest manifest)
    {
        if (manifest == null) return Task.CompletedTask;

        // 1. Process Snapshot if present
        if (manifest.Snapshot != null)
            foreach (var state in manifest.Snapshot.EntityStates)
                UpdateEntry(
                    manifest.UserId,
                    state.TargetId,
                    state.TargetType,
                    state.ContentHash,
                    state.Metadata,
                    manifest.Snapshot.LastSequenceNumber,
                    manifest.Snapshot.Timestamp,
                    isDelete: false);

        // 2. Process Operations
        foreach (var op in manifest.Operations)
            if (op.OperationType == ManifestOperationType.Create ||
                op.OperationType == ManifestOperationType.Update ||
                op.OperationType == ManifestOperationType.Delete)
                UpdateEntry(
                    manifest.UserId,
                    op.TargetId,
                    op.TargetType,
                    op.ContentHash,
                    op.Metadata,
                    op.SequenceNumber,
                    op.Timestamp,
                    op.OperationType == ManifestOperationType.Delete);

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

        if (_peerAvailability.TryGetValue(contentHash, out var peers)) return Task.FromResult<IEnumerable<string>>(peers.Keys.ToList());

        return Task.FromResult(Enumerable.Empty<string>());
    }

    private void UpdateEntry(string userId, string targetId, string targetType, string? contentHash, Dictionary<string, string> metadata, int sequenceNumber, DateTime timestamp, bool isDelete)
    {
        if (string.IsNullOrWhiteSpace(targetId)) return;

        if (!_processors.TryGetValue(targetType, out var processor))
            return;

        lock (_lock)
        {
            _entries.TryGetValue(targetId, out var existingEntry);

            // 1. Authority Rule: Only the original owner can update or delete an entry
            if (existingEntry != null && !string.Equals(existingEntry.OwnerUserId, userId, StringComparison.OrdinalIgnoreCase))
                return;

            // 2. Validate update via processor (e.g., Versioning and Hash Immutability)
            if (!processor.ValidateUpdate(existingEntry, contentHash, metadata))
                return;

            // 3. Staleness Rule: Only apply if incoming SequenceNumber is greater than existing for this TargetId
            if (_lastSequenceNumbers.TryGetValue(targetId, out var existingSeq) && sequenceNumber <= existingSeq)
                return;

            _lastSequenceNumbers[targetId] = sequenceNumber;

            if (isDelete)
            {
                _entries.TryRemove(targetId, out _);
            }
            else
            {
                var entry = processor.MapMetadata(targetId, userId, contentHash, metadata, sequenceNumber, timestamp);
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

            var compressedHash = metadata.GetValueOrDefault("compressedHash");
            if (!string.IsNullOrEmpty(compressedHash))
            {
                var peers = _peerAvailability.GetOrAdd(compressedHash, _ => new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase));
                if (isDelete)
                    peers.TryRemove(userId, out _);
                else
                    peers.TryAdd(userId, true);
            }
        }
    }
}
