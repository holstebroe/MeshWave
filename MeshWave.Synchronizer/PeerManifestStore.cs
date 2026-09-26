using System.Collections.Concurrent;
using MeshWave.Common.Core;
using MeshWave.Common.Core.Models;

namespace MeshWave.Synchronizer;

/// <summary>
/// Persists and manages the manifest streams this peer replicates for other authors.
///
/// Each stream is stored as an append-only log (<see cref="ManifestLog"/>):
///   {storeDirectory}/{userId}.{stream}.mwlog
/// Merging appends the new operations; the file is only rewritten when a newer snapshot is adopted.
///
/// The store is the single source of truth for all received peer data, and the source this peer serves other
/// authors' streams from (store-and-forward). The local user's own manifest is intentionally NOT stored here.
/// </summary>
public class PeerManifestStore : IManifestStore
{
    private const string PeerManifestsFolderName = "PeerManifests";
    private readonly string _storeDirectory;
    private readonly ConcurrentDictionary<(string UserId, ManifestStreamType StreamType), Manifest> _manifests = new();
    private readonly ConcurrentDictionary<string, ManifestOperation> _competitionIndex = new();

    public PeerManifestStore(IMeshWaveEnvironment environment, string? storeDirectory = null)
    {
        _storeDirectory = storeDirectory ?? Path.Combine(environment.GetAppDataRoot(), PeerManifestsFolderName);
        Directory.CreateDirectory(_storeDirectory);
    }

    public static PeerManifestStore CreateAtBase(IMeshWaveEnvironment environment, string baseFolder)
    {
        return new PeerManifestStore(environment, Path.Combine(baseFolder, PeerManifestsFolderName));
    }

    /// <summary>
    /// Loads all persisted peer manifests from disk.  Call once at application start.
    /// Stores written before operations were hash-linked (<c>*.json</c>) cannot be continued and are deleted; the
    /// streams are fetched again from peers.
    /// </summary>
    public void LoadAll()
    {
        if (!Directory.Exists(_storeDirectory))
            return;

        foreach (var legacy in Directory.EnumerateFiles(_storeDirectory, "*.json"))
            try { File.Delete(legacy); }
            catch { /* ignored */ }

        foreach (var file in Directory.EnumerateFiles(_storeDirectory, "*" + ManifestLog.Extension))
            try
            {
                var manifest = ManifestLog.Read(file);
                if (manifest != null && !string.IsNullOrWhiteSpace(manifest.UserId))
                {
                    _manifests[(manifest.UserId, manifest.StreamType)] = manifest;
                    IndexCompetitions(manifest.AllOperations());
                }
            }
            catch
            {
                /* skip corrupted files */
            }
    }

    /// <summary>
    /// Returns the cached manifest for <paramref name="userId"/> and <paramref name="streamType"/>, or null if not yet received.
    /// </summary>
    public Manifest? Get(string userId, ManifestStreamType streamType = ManifestStreamType.Content)
    {
        return _manifests.GetValueOrDefault((userId, streamType));
    }

    /// <summary>
    /// Returns all currently cached peer manifests.
    /// </summary>
    public IReadOnlyCollection<Manifest> GetAll()
    {
        return _manifests.Values.ToList();
    }

    /// <summary>The CreateCompetition operation for a competition, if any replicated stream holds it.</summary>
    public ManifestOperation? GetCompetition(string competitionId)
    {
        return _competitionIndex.GetValueOrDefault(competitionId);
    }

    /// <summary>
    /// Merges <paramref name="incoming"/> into the cached manifest for its owner and stream type.
    /// Creates a new entry if this is the first manifest from that peer for this stream (unless
    /// <see cref="SecurityLimits.MaxReplicatedAuthors"/> authors are already stored).
    /// Persists to disk after merging.  Returns the number of new operations merged.
    /// </summary>
    public int MergeAndSave(Manifest incoming, string peerPublicKey, ManifestManager manager)
    {
        if (string.IsNullOrWhiteSpace(incoming.UserId)) return 0;

        var key = (incoming.UserId, incoming.StreamType);
        if (!_manifests.ContainsKey(key) && !IsKnownAuthor(incoming.UserId) && CountAuthors() >= SecurityLimits.MaxReplicatedAuthors)
        {
            NLog.LogManager.GetCurrentClassLogger().Debug("Not replicating {0}: already storing {1} authors.", incoming.UserId, SecurityLimits.MaxReplicatedAuthors);
            return 0;
        }

        var local = _manifests.GetOrAdd(key, _ =>
        {
            var m = manager.CreateManifest(incoming.UserId);
            m.StreamType = incoming.StreamType;
            return m;
        });

        lock (local)
        {
            var snapshotBefore = local.Snapshot;
            var headBefore = ManifestManager.GetHeadSequenceNumber(local);

            int added;
            try
            {
                added = manager.MergeManifest(local, incoming, peerPublicKey);
            }
            catch (Exception ex)
            {
                NLog.LogManager.GetCurrentClassLogger().Warn("Merge failed for manifest from user {0} stream {1}: {2}", incoming.UserId, incoming.StreamType, ex.Message);
                if (headBefore < 0 && local.Snapshot == null && local.Operations.Count == 0)
                    _manifests.TryRemove(key, out _);
                return 0; // reject tampered / over-limit manifests
            }

            if (headBefore < 0 && ManifestManager.GetHeadSequenceNumber(local) < 0)
            {
                _manifests.TryRemove(key, out _);
                return 0;
            }

            if (!ReferenceEquals(snapshotBefore, local.Snapshot))
            {
                IndexCompetitions(local.AllOperations());
                SaveToDisk(local, rewrite: true);
            }
            else if (added > 0)
            {
                var newOps = local.Operations.Where(o => o.SequenceNumber > headBefore).OrderBy(o => o.SequenceNumber).ToList();
                IndexCompetitions(newOps);
                SaveToDisk(local, rewrite: headBefore < 0, newOps);
            }

            return added;
        }
    }

    /// <summary>
    /// Removes the persisted manifests for a peer (e.g. when they are evicted from the routing table).
    /// </summary>
    public void Remove(string userId)
    {
        foreach (ManifestStreamType streamType in Enum.GetValues(typeof(ManifestStreamType)))
        {
            _manifests.TryRemove((userId, streamType), out _);
            var path = FilePath(userId, streamType);
            if (File.Exists(path))
                try { File.Delete(path); }
                catch
                {
                    // ignored
                }
        }
    }

    /// <summary>
    /// Clears all cached peer manifests from memory and disk.
    /// </summary>
    public void ClearAll()
    {
        _manifests.Clear();

        if (!Directory.Exists(_storeDirectory))
            return;

        foreach (var file in Directory.EnumerateFiles(_storeDirectory, "*" + ManifestLog.Extension, SearchOption.TopDirectoryOnly))
            try { File.Delete(file); }
            catch
            {
                // ignored
            }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────

    private bool IsKnownAuthor(string userId)
    {
        return _manifests.Keys.Any(k => k.UserId == userId);
    }

    private int CountAuthors()
    {
        return _manifests.Keys.Select(k => k.UserId).Distinct().Count();
    }

    private void IndexCompetitions(IEnumerable<ManifestOperation> operations)
    {
        foreach (var op in operations)
            if (op.OperationType == ManifestOperationType.CreateCompetition)
                _competitionIndex[op.TargetId] = op;
    }

    private void SaveToDisk(Manifest manifest, bool rewrite, IReadOnlyList<ManifestOperation>? newOps = null)
    {
        try
        {
            var path = FilePath(manifest.UserId, manifest.StreamType);
            if (rewrite || newOps == null || !File.Exists(path))
                ManifestLog.Rewrite(path, manifest);
            else
                ManifestLog.Append(path, newOps);
        }
        catch { /* best-effort disk write */ }
    }

    private string FilePath(string userId, ManifestStreamType streamType)
    {
        // Sanitise userId to a safe filename  (it is already a GUID-like string per P2PIdentityService)
        var safe = string.Concat(userId.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'));
        var suffix = streamType.ToString().ToLowerInvariant();
        return Path.Combine(_storeDirectory, $"{safe}.{suffix}{ManifestLog.Extension}");
    }
}
