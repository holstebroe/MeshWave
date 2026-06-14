using MeshWave.Common.Core;
using System.Text;
using MeshWave.Common.Core.Crypto;
using MeshWave.Common.Core.Models;
using NLog;

namespace MeshWave.Synchronizer;

/// <summary>
/// ManifestManager handles creation, signing, and management of user manifests.
/// Manifests are append-only, signed lists of operations on the user's content.
/// </summary>
public class ManifestManager(ILogger logger)
{
    public ManifestManager() : this(LogManager.GetCurrentClassLogger())
    {
    }

    /// <summary>
    /// Creates a new manifest for a user.
    /// </summary>
    public Manifest CreateManifest(string userId)
    {
        return new Manifest
        {
            UserId = userId,
            Operations = [],
            Version = 1,
            LastUpdated = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Builds a signed operation and appends it to the manifest.
    /// </summary>
    public ManifestOperation AppendSignedOperation(
        Manifest manifest,
        ManifestOperationType type,
        string targetId,
        string targetType,
        string? contentHash,
        Dictionary<string, string>? metadata,
        string privateKeyPem)
    {
        lock (manifest)
        {
            var operation = new ManifestOperation
            {
                OperationId = Guid.NewGuid().ToString(),
                OperationType = type,
                TargetId = targetId,
                TargetType = targetType,
                ContentHash = contentHash,
                SequenceNumber = GetNextSequenceNumber(manifest),
                Metadata = metadata ?? [],
                Timestamp = DateTime.UtcNow,
                Signature = string.Empty
            };

            var signable = BuildSignablePayload(operation);
            operation.Signature = CryptoService.SignData(signable, privateKeyPem);

            manifest.Operations.Add(operation);
            manifest.Version++;
            manifest.LastUpdated = DateTime.UtcNow;

            return operation;
        }
    }

    /// <summary>
    /// Adds a pre-built operation to the manifest (create, update, or delete).
    /// Assigns sequence number and increments manifest version.
    /// </summary>
    public void AppendOperation(Manifest manifest, ManifestOperation operation)
    {
        lock (manifest)
        {
            operation.SequenceNumber = GetNextSequenceNumber(manifest);
            manifest.Operations.Add(operation);
            manifest.Version++;
            manifest.LastUpdated = DateTime.UtcNow;
        }
    }

    private static int GetNextSequenceNumber(Manifest manifest)
    {
        if (manifest.Snapshot != null)
            return manifest.Snapshot.LastSequenceNumber + 1 + manifest.Operations.Count;
        return manifest.Operations.Count;
    }

    /// <summary>
    /// Creates a signed snapshot of the manifest state up to a certain sequence number.
    /// Squashes redundant operations (Play, Follow, Like, etc.) and keeps latest entity metadata.
    /// </summary>
    public ManifestSnapshot CreateSnapshot(Manifest manifest, int upToSequenceNumber, string privateKeyPem)
    {
        var playCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var followed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var liked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var friends = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entities = new Dictionary<(string Id, string Type), SnapshotStateEntry>();
        var persistent = new List<ManifestOperation>();

        lock (manifest)
        {
            // Start with existing snapshot if any
            if (manifest.Snapshot != null && manifest.Snapshot.LastSequenceNumber <= upToSequenceNumber)
            {
                foreach (var kv in manifest.Snapshot.PlayCounts) playCounts[kv.Key] = kv.Value;
                foreach (var id in manifest.Snapshot.FollowedUserIds) followed.Add(id);
                foreach (var id in manifest.Snapshot.LikedTrackIds) liked.Add(id);
                foreach (var id in manifest.Snapshot.FriendUserIds) friends.Add(id);
                foreach (var id in manifest.Snapshot.GroupIds) groups.Add(id);
                foreach (var ent in manifest.Snapshot.EntityStates) entities[(ent.TargetId, ent.TargetType)] = ent;
                persistent.AddRange(manifest.Snapshot.PersistentOperations);
            }

            // Process operations in order
            foreach (var op in manifest.Operations.OrderBy(o => o.SequenceNumber))
            {
                if (op.SequenceNumber > upToSequenceNumber) break;

                switch (op.OperationType)
                {
                    case ManifestOperationType.Play:
                        // Total plays
                        playCounts[op.TargetId] = playCounts.GetValueOrDefault(op.TargetId) + 1;
                        // Versioned plays
                        if (!string.IsNullOrEmpty(op.ContentHash))
                        {
                            var versionKey = $"{op.TargetId}:{op.ContentHash}";
                            playCounts[versionKey] = playCounts.GetValueOrDefault(versionKey) + 1;
                        }
                        break;
                    case ManifestOperationType.Follow:
                        followed.Add(op.TargetId);
                        break;
                    case ManifestOperationType.Unfollow:
                        followed.Remove(op.TargetId);
                        break;
                    case ManifestOperationType.Like:
                        liked.Add(op.TargetId);
                        break;
                    case ManifestOperationType.Unlike:
                        liked.Remove(op.TargetId);
                        break;
                    case ManifestOperationType.FriendAdd:
                        friends.Add(op.TargetId);
                        break;
                    case ManifestOperationType.FriendRemove:
                        friends.Remove(op.TargetId);
                        break;
                    case ManifestOperationType.GroupJoin:
                        groups.Add(op.TargetId);
                        break;
                    case ManifestOperationType.GroupLeave:
                        groups.Remove(op.TargetId);
                        break;
                    case ManifestOperationType.Create:
                    case ManifestOperationType.Update:
                    case ManifestOperationType.Profile:
                        entities[(op.TargetId, op.TargetType)] = new SnapshotStateEntry
                        {
                            TargetId = op.TargetId,
                            TargetType = op.TargetType,
                            ContentHash = op.ContentHash,
                            Metadata = new Dictionary<string, string>(op.Metadata)
                        };
                        break;
                    case ManifestOperationType.Delete:
                        entities.Remove((op.TargetId, op.TargetType));
                        break;
                    case ManifestOperationType.Comment:
                    case ManifestOperationType.CreateCompetition:
                    case ManifestOperationType.CompetitionSubmit:
                    case ManifestOperationType.CompetitionCastVote:
                    case ManifestOperationType.CompetitionRevealResults:
                        persistent.Add(op);
                        break;
                    case ManifestOperationType.CommentDelete:
                        var commentIdToDelete = op.Metadata.GetValueOrDefault("commentOperationId");
                        if (!string.IsNullOrEmpty(commentIdToDelete)) persistent.RemoveAll(o => o.OperationId == commentIdToDelete);
                        break;
                }
            }

            var snapshot = new ManifestSnapshot
            {
                LastSequenceNumber = upToSequenceNumber,
                Timestamp = DateTime.UtcNow,
                PlayCounts = playCounts,
                FollowedUserIds = followed.ToList(),
                LikedTrackIds = liked.ToList(),
                FriendUserIds = friends.ToList(),
                GroupIds = groups.ToList(),
                EntityStates = entities.Values.ToList(),
                PersistentOperations = persistent,
                Signature = string.Empty
            };

            snapshot.LibraryStateDigest = ComputeLibraryStateDigest(snapshot);

            var signable = BuildSnapshotSignablePayload(snapshot);
            snapshot.Signature = CryptoService.SignData(signable, privateKeyPem);

            return snapshot;
        }
    }

    /// <summary>
    /// Compacts the manifest if it exceeds the specified threshold.
    /// Squashes old operations into a signed snapshot, keeping only the most recent operations.
    /// </summary>
    public void Compact(Manifest manifest, string privateKeyPem, int threshold = 500, int keepRecent = 100)
    {
        lock (manifest)
        {
            if (manifest.Operations.Count < threshold)
            {
                LogManager.GetCurrentClassLogger().Debug("Compact skipped: ops count {0} < threshold {1}", manifest.Operations.Count, threshold);
                return;
            }

            // Snapshot everything except the last 'keepRecent' operations
            var lastToSnapshot = manifest.Operations.OrderBy(o => o.SequenceNumber)
                .ElementAt(manifest.Operations.Count - keepRecent - 1).SequenceNumber;

            var snapshot = CreateSnapshot(manifest, lastToSnapshot, privateKeyPem);

            manifest.Snapshot = snapshot;
            manifest.Operations = manifest.Operations
                .Where(o => o.SequenceNumber > lastToSnapshot)
                .OrderBy(o => o.SequenceNumber)
                .ToList();

            manifest.Version++;
            manifest.LastUpdated = DateTime.UtcNow;
        }
    }

    private static string ComputeLibraryStateDigest(ManifestSnapshot snapshot)
    {
        var sb = new StringBuilder();

        // Followed, Liked, Friends, Groups
        foreach (var id in snapshot.FollowedUserIds.OrderBy(s => s)) sb.Append("f:").Append(id).Append(';');
        foreach (var id in snapshot.LikedTrackIds.OrderBy(s => s)) sb.Append("l:").Append(id).Append(';');
        foreach (var id in snapshot.FriendUserIds.OrderBy(s => s)) sb.Append("fr:").Append(id).Append(';');
        foreach (var id in snapshot.GroupIds.OrderBy(s => s)) sb.Append("g:").Append(id).Append(';');

        // EntityStates
        foreach (var ent in snapshot.EntityStates.OrderBy(e => e.TargetId).ThenBy(e => e.TargetType))
        {
            sb.Append("e:").Append(ent.TargetId).Append(':').Append(ent.TargetType).Append(':').Append(ent.ContentHash ?? string.Empty).Append('{');
            foreach (var kv in ent.Metadata.OrderBy(k => k.Key)) sb.Append(kv.Key).Append('=').Append(kv.Value).Append(',');
            sb.Append("};");
        }

        // PlayCounts
        foreach (var kv in snapshot.PlayCounts.OrderBy(k => k.Key)) sb.Append("p:").Append(kv.Key).Append('=').Append(kv.Value).Append(';');

        return CryptoService.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    /// <summary>
    /// Verifies the integrity and authenticity of a manifest.
    /// Checks monotonic sequence numbers and each operation's RSA signature.
    /// Supports manifests starting from a snapshot.
    /// </summary>
    public bool VerifyManifest(Manifest manifest, string userPublicKey)
    {
        lock (manifest)
        {
            var expectedSeq = 0;

            if (manifest.Snapshot != null)
            {
                var snapshotSignable = BuildSnapshotSignablePayload(manifest.Snapshot);
                if (!CryptoService.VerifySignature(snapshotSignable, manifest.Snapshot.Signature, userPublicKey))
                {
                    logger.Debug("Manifest verification failed for user {0} stream {1}: Invalid snapshot signature.", manifest.UserId, manifest.StreamType);
                    return false;
                }

                // Set Verification: Verify library state digest
                if (!string.IsNullOrEmpty(manifest.Snapshot.LibraryStateDigest))
                {
                    var computedDigest = ComputeLibraryStateDigest(manifest.Snapshot);
                    if (manifest.Snapshot.LibraryStateDigest != computedDigest)
                    {
                        logger.Debug("Manifest verification failed for user {0} stream {1}: LibraryStateDigest mismatch.", manifest.UserId, manifest.StreamType);
                        return false;
                    }
                }

                // Verify persistent operations in the snapshot
                foreach (var op in manifest.Snapshot.PersistentOperations)
                {
                    var signable = BuildSignablePayload(op);
                    if (!CryptoService.VerifySignature(signable, op.Signature, userPublicKey))
                    {
                        logger.Debug("Manifest verification failed for user {0} stream {1}: Invalid persistent operation signature for sequence {2}.", manifest.UserId, manifest.StreamType, op.SequenceNumber);
                        return false;
                    }
                }

                expectedSeq = manifest.Snapshot.LastSequenceNumber + 1;
            }

            if (manifest.Snapshot == null && manifest.Operations.Count > 0) expectedSeq = manifest.Operations[0].SequenceNumber;

            for (var i = 0; i < manifest.Operations.Count; i++)
            {
                var op = manifest.Operations[i];

                if (op.SequenceNumber != expectedSeq + i)
                {
                    logger.Debug("Manifest verification failed for user {0} stream {1}: Expected sequence {2} but got {3}.", manifest.UserId, manifest.StreamType, expectedSeq + i, op.SequenceNumber);
                    return false;
                }

                var signable = BuildSignablePayload(op);
                if (!CryptoService.VerifySignature(signable, op.Signature, userPublicKey))
                {
                    logger.Debug("Manifest verification failed for user {0} stream {1}: Invalid operation signature for sequence {2}.", manifest.UserId, manifest.StreamType, op.SequenceNumber);
                    return false;
                }
            }
            return true;
        }
    }

    /// <summary>
    /// Merges a remote manifest into a local one, appending any operations the local copy lacks.
    /// Only operations that pass signature verification are accepted.
    /// Rejects manifests that exceed security limits.
    /// Supports merging manifests with snapshots.
    /// Play operations are capped at <see cref="SecurityLimits.MaxPlaysPerUserPerTrackPerDay"/> per track per UTC day.
    /// Returns the number of new operations added.
    /// </summary>
    public int MergeManifest(Manifest local, Manifest remote, string remoteUserPublicKey)
    {
        if (local.UserId != remote.UserId)
            throw new ArgumentException("Cannot merge manifests from different users.");

        if (local.StreamType != remote.StreamType)
            throw new ArgumentException($"Cannot merge manifests with different stream types ({local.StreamType} vs {remote.StreamType}).");

        if (remote.Operations.Count > SecurityLimits.MaxManifestOperations)
        {
            logger.Warn("Merge failed: remote manifest from {0} stream {1} has {2} operations, exceeding limit of {3}", remote.UserId, remote.StreamType, remote.Operations.Count, SecurityLimits.MaxManifestOperations);
            throw new InvalidDataException($"Remote manifest exceeds operation limit ({remote.Operations.Count}).");
        }

        // 1. Verify remote manifest integrity before merging
        if (!VerifyManifest(remote, remoteUserPublicKey))
        {
            logger.Debug("Merge failed: remote manifest from {0} stream {1} failed verification.", remote.UserId, remote.StreamType);
            throw new InvalidDataException("Remote manifest failed signature or continuity verification.");
        }

        lock (local)
        {
            // 2. Handle Snapshot merge
            // If remote has a NEWER snapshot, we adopt it and discard local operations that are now squashed.
            if (remote.Snapshot != null)
            {
                if (remote.Snapshot.LastSequenceNumber > (local.Snapshot?.LastSequenceNumber ?? -1))
                {
                    // Remote snapshot is more recent than ours.
                    // We keep only the remote snapshot and remote operations.
                    local.Snapshot = remote.Snapshot;
                    local.Operations = new List<ManifestOperation>(remote.Operations);
                    local.Version = Math.Max(local.Version, remote.Version);
                    local.LastUpdated = DateTime.UtcNow;

                    // Since we replaced the whole state, we "added" as many ops as the remote currently has
                    return remote.Operations.Count;
                }
            }

            // 3. Merge individual operations
            // Build existing play counts per (trackId, utcDate) from the local manifest so we
            // know how much headroom remains before merging remote play ops.
            var playCounts = BuildPlayCounts(local.Operations);

            var added = 0;
            var localMaxSeqNum = (local.Snapshot?.LastSequenceNumber ?? -1) + local.Operations.Count;

            foreach (var op in remote.Operations.OrderBy(o => o.SequenceNumber))
            {
                if (op.SequenceNumber <= localMaxSeqNum)
                {
                    logger.Trace("Skipping operation {0} for user {1} stream {2}: Sequence number already applied.", op.SequenceNumber, remote.UserId, remote.StreamType);
                    continue;
                }

                if (!IsOperationWithinLimits(remote, op))
                    continue;

                // Enforce per-user daily play cap.
                if (op.OperationType == ManifestOperationType.Play)
                {
                    var key = (TrackId: op.TargetId, op.Timestamp.ToUniversalTime().Date);
                    playCounts.TryGetValue(key, out var existing);
                    if (existing >= SecurityLimits.MaxPlaysPerUserPerTrackPerDay)
                    {
                        logger.Debug("Discarding operation {0} in stream {1} for user {2}: Max plays per user per track per day exceeded.", op.SequenceNumber, remote.StreamType, remote.UserId);
                        continue;
                    }
                    playCounts[key] = existing + 1;
                }

                if (IsCompetitionOperation(op.OperationType))
                    if (!ValidateCompetitionOperation(op))
                    {
                        logger.Debug("Discarding operation {0} in stream {1} for user {2}: Invalid competition operation.", op.SequenceNumber, remote.StreamType, remote.UserId);
                        continue;
                    }

                // We already verified all signatures in VerifyManifest call above,
                // but we can re-verify if we want to be paranoid or if VerifyManifest was skipped.
                // For performance, we trust the previous VerifyManifest(remote) call.

                local.Operations.Add(op);
                local.Version = Math.Max(local.Version, remote.Version);
                local.LastUpdated = DateTime.UtcNow;
                added++;
            }

            return added;
        }
    }

    /// <summary>
    /// Counts existing Play operations in a list grouped by (trackId, utcDate).
    /// Used to enforce <see cref="SecurityLimits.MaxPlaysPerUserPerTrackPerDay"/> during merge.
    /// </summary>
    private static Dictionary<(string TrackId, DateTime Date), int> BuildPlayCounts(
        IEnumerable<ManifestOperation> ops)
    {
        var counts = new Dictionary<(string TrackId, DateTime Date), int>();
        foreach (var op in ops.Where(o => o.OperationType == ManifestOperationType.Play))
        {
            var key = (TrackId: op.TargetId, op.Timestamp.ToUniversalTime().Date);
            counts.TryGetValue(key, out var c);
            counts[key] = c + 1;
        }
        return counts;
    }

    private static bool IsCompetitionOperation(ManifestOperationType type)
    {
        return type is ManifestOperationType.CreateCompetition
                    or ManifestOperationType.CompetitionSubmit
                    or ManifestOperationType.CompetitionCastVote
                    or ManifestOperationType.CompetitionRevealResults;
    }

    /// <summary>
    /// Validates a competition-related operation.
    /// Logic to be fully implemented in #76.
    /// </summary>
    private static bool ValidateCompetitionOperation(ManifestOperation op)
    {
        // TODO: Implement full validation (deadline checks, signature verification for reveal, etc.)
        return true;
    }

    private bool IsOperationWithinLimits(Manifest manifest, ManifestOperation op)
    {
        if (op.OperationId.Length > SecurityLimits.MaxOperationIdLength)
        {
            logger.Debug("Discarding operation {0} in stream {1} for user {2}: OperationId exceeds max length.", op.SequenceNumber, manifest.StreamType, manifest.UserId);
            return false;
        }
        if (op.TargetId.Length > SecurityLimits.MaxTargetIdLength)
        {
            logger.Debug("Discarding operation {0} in stream {1} for user {2}: TargetId exceeds max length.", op.SequenceNumber, manifest.StreamType, manifest.UserId);
            return false;
        }
        if (op.TargetType.Length > SecurityLimits.MaxTargetTypeLength)
        {
            logger.Debug("Discarding operation {0} in stream {1} for user {2}: TargetType exceeds max length.", op.SequenceNumber, manifest.StreamType, manifest.UserId);
            return false;
        }
        if (op.ContentHash?.Length > SecurityLimits.MaxContentHashLength)
        {
            logger.Debug("Discarding operation {0} in stream {1} for user {2}: ContentHash exceeds max length.", op.SequenceNumber, manifest.StreamType, manifest.UserId);
            return false;
        }
        if (op.Metadata.Count > SecurityLimits.MaxMetadataEntries)
        {
            logger.Debug("Discarding operation {0} in stream {1} for user {2}: Metadata entries exceed max limit.", op.SequenceNumber, manifest.StreamType, manifest.UserId);
            return false;
        }

        foreach (var kv in op.Metadata)
        {
            if (kv.Key.Length > SecurityLimits.MaxMetadataKeyLength)
            {
                logger.Warn("Discarding operation {0} in stream {1} for user {2}: Metadata key exceeds max length.", op.SequenceNumber, manifest.StreamType, manifest.UserId);
                return false;
            }
            if (kv.Value.Length > SecurityLimits.MaxMetadataValueLength)
            {
                logger.Warn("Discarding operation {0} in stream {1} for user {2}: Metadata value exceeds max length.", op.SequenceNumber, manifest.StreamType, manifest.UserId);
                return false;
            }
        }

        return true;
    }

    public static string BuildSignablePayload(ManifestOperation op)
    {
        var sb = new StringBuilder();
        sb.Append(op.OperationId);
        sb.Append('|');
        sb.Append(op.OperationType);
        sb.Append('|');
        sb.Append(op.TargetId);
        sb.Append('|');
        sb.Append(op.TargetType);
        sb.Append('|');
        sb.Append(op.ContentHash ?? string.Empty);
        sb.Append('|');
        sb.Append(op.SequenceNumber);
        sb.Append('|');
        sb.Append(op.Timestamp.Ticks);
        return sb.ToString();
    }

    public static string BuildSnapshotSignablePayload(ManifestSnapshot snapshot)
    {
        var sb = new StringBuilder();
        sb.Append(snapshot.LastSequenceNumber);
        sb.Append('|');
        sb.Append(snapshot.Timestamp.Ticks);
        sb.Append('|');

        // Sorted PlayCounts
        foreach (var kv in snapshot.PlayCounts.OrderBy(k => k.Key))
        {
            sb.Append(kv.Key);
            sb.Append(':');
            sb.Append(kv.Value);
            sb.Append(',');
        }
        sb.Append('|');

        // Sorted FollowedUserIds
        foreach (var id in snapshot.FollowedUserIds.OrderBy(s => s))
        {
            sb.Append(id);
            sb.Append(',');
        }
        sb.Append('|');

        // Sorted LikedTrackIds
        foreach (var id in snapshot.LikedTrackIds.OrderBy(s => s))
        {
            sb.Append(id);
            sb.Append(',');
        }
        sb.Append('|');

        // Sorted FriendUserIds
        foreach (var id in snapshot.FriendUserIds.OrderBy(s => s))
        {
            sb.Append(id);
            sb.Append(',');
        }
        sb.Append('|');

        // Sorted GroupIds
        foreach (var id in snapshot.GroupIds.OrderBy(s => s))
        {
            sb.Append(id);
            sb.Append(',');
        }
        sb.Append('|');

        // Sorted EntityStates
        foreach (var entity in snapshot.EntityStates.OrderBy(e => e.TargetId).ThenBy(e => e.TargetType))
        {
            sb.Append(entity.TargetId);
            sb.Append(':');
            sb.Append(entity.TargetType);
            sb.Append(':');
            sb.Append(entity.ContentHash ?? string.Empty);
            sb.Append(':');
            // Sorted Metadata
            foreach (var kv in entity.Metadata.OrderBy(k => k.Key))
            {
                sb.Append(kv.Key);
                sb.Append('=');
                sb.Append(kv.Value);
                sb.Append(';');
            }
            sb.Append(',');
        }
        sb.Append('|');

        // Sorted PersistentOperations
        foreach (var op in snapshot.PersistentOperations.OrderBy(o => o.SequenceNumber))
        {
            sb.Append(op.OperationId);
            sb.Append(',');
        }
        sb.Append('|');

        // LibraryStateDigest
        sb.Append(snapshot.LibraryStateDigest ?? string.Empty);

        return sb.ToString();
    }
}
