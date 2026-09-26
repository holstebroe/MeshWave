using MeshWave.Common.Core;
using System.Runtime.CompilerServices;
using System.Text;
using MeshWave.Common.Core.Crypto;
using MeshWave.Common.Core.Models;
using MeshWave.Common.Core.Serialization;
using NLog;

namespace MeshWave.Synchronizer;

/// <summary>
/// ManifestManager handles creation, signing, verification and merging of manifests.
/// A manifest stream is an append-only, hash-linked log of operations signed by its author: every operation signs its
/// sequence number, its metadata and the hash of the previous operation (<see cref="ManifestOperation.PrevHash"/>).
/// Each operation therefore verifies on its own, which lets any peer store and forward it, and two different operations
/// with the same sequence number (a fork) are detected.
/// </summary>
public class ManifestManager(ILogger logger)
{
    private const string SignablePayloadVersion = "mw2";

    private static readonly ConditionalWeakTable<ManifestOperation, Tuple<string, string>> HashCache = new();

    public ManifestManager() : this(LogManager.GetCurrentClassLogger())
    {
    }

    /// <summary>Raised when a peer offers an operation that conflicts with one already held for the same author, stream and sequence number.</summary>
    public event Action<string, ManifestStreamType, int>? ForkDetected;

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
    /// Builds a signed operation and appends it to the manifest, chained to the current head.
    /// </summary>
    public ManifestOperation AppendSignedOperation(
        Manifest manifest,
        ManifestOperationType type,
        string targetId,
        string targetType,
        string? contentHash,
        Dictionary<string, string>? metadata,
        string privateKey)
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
                SequenceNumber = GetHeadSequenceNumber(manifest) + 1,
                PrevHash = GetHeadHash(manifest),
                Metadata = metadata ?? [],
                Timestamp = DateTime.UtcNow,
                Signature = string.Empty
            };

            operation.Signature = CryptoService.SignData(BuildSignablePayload(operation), privateKey);

            manifest.Operations.Add(operation);
            manifest.Version++;
            manifest.LastUpdated = DateTime.UtcNow;

            return operation;
        }
    }

    /// <summary>
    /// Adds a pre-built operation to the manifest (create, update, or delete).
    /// Assigns sequence number, chains it to the head and increments manifest version. The caller signs it.
    /// </summary>
    public void AppendOperation(Manifest manifest, ManifestOperation operation)
    {
        lock (manifest)
        {
            operation.SequenceNumber = GetHeadSequenceNumber(manifest) + 1;
            operation.PrevHash = GetHeadHash(manifest);
            manifest.Operations.Add(operation);
            manifest.Version++;
            manifest.LastUpdated = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Returns the highest sequence number already covered by <paramref name="manifest"/> (its snapshot or its operations),
    /// or -1 if it is empty.
    /// </summary>
    public static int GetHeadSequenceNumber(Manifest? manifest)
    {
        if (manifest == null) return -1;
        lock (manifest)
        {
            var head = manifest.Snapshot?.LastSequenceNumber ?? -1;
            foreach (var op in manifest.Operations)
                if (op.SequenceNumber > head) head = op.SequenceNumber;
            return head;
        }
    }

    /// <summary>
    /// Hash of the head operation (the snapshot's head hash if there are no live operations); for an empty stream, the
    /// stream's <see cref="GetGenesisHash">genesis hash</see>.
    /// </summary>
    public static string GetHeadHash(Manifest? manifest)
    {
        if (manifest == null) return string.Empty;
        lock (manifest)
        {
            var head = manifest.Operations.Count > 0 ? manifest.Operations.MaxBy(o => o.SequenceNumber) : null;
            if (head != null && head.SequenceNumber > (manifest.Snapshot?.LastSequenceNumber ?? -1))
                return ComputeOperationHash(head);
            return manifest.Snapshot?.HeadHash ?? GetGenesisHash(manifest.UserId, manifest.StreamType);
        }
    }

    /// <summary>
    /// The <see cref="ManifestOperation.PrevHash"/> of a stream's first operation. It names the author and the stream, so
    /// the signature of every operation binds it to one stream of one author: an operation cannot be replayed into
    /// another stream.
    /// </summary>
    public static string GetGenesisHash(string userId, ManifestStreamType streamType)
    {
        return CryptoService.ComputeHash(Encoding.UTF8.GetBytes($"{SignablePayloadVersion}-genesis|{userId}|{streamType}"));
    }

    /// <summary>The stream's head as exchanged in anti-entropy.</summary>
    public static StreamHead GetHead(Manifest manifest)
    {
        lock (manifest)
        {
            return new StreamHead(manifest.UserId, manifest.StreamType, GetHeadSequenceNumber(manifest), GetHeadHash(manifest));
        }
    }

    /// <summary>
    /// Identifies an operation: the SHA-256 of its signable payload, which covers every field except the signature.
    /// The next operation of the author signs this value as its <see cref="ManifestOperation.PrevHash"/>.
    /// </summary>
    public static string ComputeOperationHash(ManifestOperation op)
    {
        if (HashCache.TryGetValue(op, out var cached) && ReferenceEquals(cached.Item1, op.Signature))
            return cached.Item2;

        var hash = CryptoService.ComputeHash(Encoding.UTF8.GetBytes(BuildSignablePayload(op)));
        HashCache.AddOrUpdate(op, Tuple.Create(op.Signature, hash));
        return hash;
    }

    /// <summary>
    /// Returns one page of <paramref name="source"/> for a peer that already has everything up to
    /// <paramref name="fromSequenceNumber"/> - 1: the snapshot if the peer is behind it, then operations until the page
    /// reaches <paramref name="maxPageBytes"/>. <see cref="Manifest.HasMore"/> is set when operations were left out.
    /// The author's public key is included when the page starts from the beginning, so that a peer that has never seen
    /// the author can verify it.
    /// </summary>
    public static Manifest BuildPage(Manifest source, int fromSequenceNumber, int? toSequenceNumber = null, int maxPageBytes = SecurityLimits.MaxManifestPageBytes)
    {
        lock (source)
        {
            var from = Math.Max(0, fromSequenceNumber);
            var snapshot = source.Snapshot != null && from <= source.Snapshot.LastSequenceNumber ? source.Snapshot : null;
            var size = snapshot != null ? ManifestSerializer.GetEncodedSize(snapshot) : 0;

            var ops = new List<ManifestOperation>();
            var hasMore = false;
            foreach (var op in source.Operations.OrderBy(o => o.SequenceNumber))
            {
                if (op.SequenceNumber < from) continue;
                if (toSequenceNumber.HasValue && op.SequenceNumber > toSequenceNumber.Value) break;

                var opSize = ManifestSerializer.GetEncodedSize(op);
                if ((ops.Count > 0 || snapshot != null) && size + opSize > maxPageBytes)
                {
                    hasMore = true;
                    break;
                }
                ops.Add(op);
                size += opSize;
            }

            return new Manifest
            {
                UserId = source.UserId,
                StreamType = source.StreamType,
                Version = source.Version,
                LastUpdated = source.LastUpdated,
                Snapshot = snapshot,
                Operations = ops,
                HasMore = hasMore,
                AuthorPublicKey = from == 0 || snapshot != null ? source.AuthorPublicKey : null
            };
        }
    }

    /// <summary>
    /// Creates a signed snapshot of the manifest state up to a certain sequence number.
    /// Squashes redundant operations (Play, Follow, Like, etc.) and keeps latest entity metadata.
    /// Comments and group posts are preserved up to <see cref="SecurityLimits.MaxSnapshotRetainedOperations"/>; older ones are dropped.
    /// </summary>
    public ManifestSnapshot CreateSnapshot(Manifest manifest, int upToSequenceNumber, string privateKey)
    {
        var playCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var followed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var liked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var friends = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entities = new Dictionary<(string Id, string Type), SnapshotStateEntry>();
        var persistent = new List<ManifestOperation>();
        var headHash = string.Empty;

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
                headHash = manifest.Snapshot.HeadHash;
            }

            var squashed = manifest.Operations.Where(o => o.SequenceNumber <= upToSequenceNumber).OrderBy(o => o.SequenceNumber).ToList();

            // Plays are counted with the same daily cap that readers apply to live operations.
            foreach (var group in squashed.Where(o => o.OperationType == ManifestOperationType.Play)
                         .GroupBy(o => (o.TargetId, Day: o.Timestamp.ToUniversalTime().Date)))
                foreach (var op in group.Take(SecurityLimits.MaxPlaysPerUserPerTrackPerDay))
                {
                    playCounts[op.TargetId] = playCounts.GetValueOrDefault(op.TargetId) + 1;
                    if (!string.IsNullOrEmpty(op.ContentHash))
                    {
                        var versionKey = $"{op.TargetId}:{op.ContentHash}";
                        playCounts[versionKey] = playCounts.GetValueOrDefault(versionKey) + 1;
                    }
                }

            foreach (var op in squashed)
            {
                headHash = ComputeOperationHash(op);
                switch (op.OperationType)
                {
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
                    case ManifestOperationType.FoundGroup:
                    case ManifestOperationType.ModerateGroup:
                    case ManifestOperationType.CreateChannel:
                    case ManifestOperationType.PostMessage:
                        persistent.Add(op);
                        break;
                    case ManifestOperationType.CommentDelete:
                        var commentIdToDelete = op.Metadata.GetValueOrDefault("commentOperationId");
                        if (!string.IsNullOrEmpty(commentIdToDelete)) persistent.RemoveAll(o => o.OperationId == commentIdToDelete);
                        break;
                }
            }

            ApplyRetention(persistent);

            var snapshot = new ManifestSnapshot
            {
                LastSequenceNumber = upToSequenceNumber,
                Timestamp = DateTime.UtcNow,
                HeadHash = headHash,
                PlayCounts = playCounts,
                FollowedUserIds = followed.ToList(),
                LikedTrackIds = liked.ToList(),
                FriendUserIds = friends.ToList(),
                GroupIds = groups.ToList(),
                EntityStates = entities.Values.ToList(),
                PersistentOperations = persistent.OrderBy(o => o.SequenceNumber).ToList(),
                Signature = string.Empty
            };

            snapshot.LibraryStateDigest = ComputeLibraryStateDigest(snapshot);
            snapshot.Signature = CryptoService.SignData(BuildSnapshotSignablePayload(snapshot), privateKey);

            return snapshot;
        }
    }

    /// <summary>
    /// Drops the oldest comments and group posts beyond <see cref="SecurityLimits.MaxSnapshotRetainedOperations"/>.
    /// Group and competition structure (founding, channels, moderation, competitions) is always kept.
    /// </summary>
    private static void ApplyRetention(List<ManifestOperation> persistent)
    {
        var chatty = persistent
            .Where(o => o.OperationType is ManifestOperationType.Comment or ManifestOperationType.PostMessage)
            .OrderBy(o => o.SequenceNumber)
            .ToList();
        var excess = chatty.Count - SecurityLimits.MaxSnapshotRetainedOperations;
        if (excess <= 0) return;

        var dropped = chatty.Take(excess).ToHashSet();
        persistent.RemoveAll(dropped.Contains);
    }

    /// <summary>
    /// Compacts the manifest if it exceeds the specified threshold.
    /// Squashes old operations into a signed snapshot, keeping only the most recent operations.
    /// </summary>
    public void Compact(Manifest manifest, string privateKey, int threshold = 500, int keepRecent = 100)
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

            var snapshot = CreateSnapshot(manifest, lastToSnapshot, privateKey);

            manifest.Snapshot = snapshot;
            manifest.Operations = manifest.Operations
                .Where(o => o.SequenceNumber > lastToSnapshot)
                .OrderBy(o => o.SequenceNumber)
                .ToList();

            manifest.Version++;
            manifest.LastUpdated = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Makes sure the author's own manifest is a valid signed hash chain, re-signing it if it is not
    /// (e.g. a manifest written before operations were chained, or before their metadata was signed).
    /// Keeps operation IDs, timestamps and content; renumbers sequence numbers if they have holes.
    /// Returns true if the manifest was re-signed.
    /// </summary>
    public bool EnsureSignedChain(Manifest manifest, string privateKey, string publicKey)
    {
        lock (manifest)
        {
            manifest.AuthorPublicKey = publicKey;
            if (VerifyManifest(manifest, publicKey) && (manifest.Snapshot != null || manifest.Operations.Count == 0 || manifest.Operations[0].SequenceNumber == 0))
                return false;

            logger.Info("Re-signing local {0} manifest of {1} as a hash-linked log ({2} operations).", manifest.StreamType, manifest.UserId, manifest.Operations.Count);

            var prevHash = GetGenesisHash(manifest.UserId, manifest.StreamType);
            var nextSeq = 0;
            if (manifest.Snapshot != null)
            {
                var snapshot = manifest.Snapshot;
                foreach (var op in snapshot.PersistentOperations)
                    op.Signature = CryptoService.SignData(BuildSignablePayload(op), privateKey);
                if (string.IsNullOrEmpty(snapshot.HeadHash))
                    snapshot.HeadHash = CryptoService.ComputeHash(Encoding.UTF8.GetBytes($"migrated-snapshot|{manifest.UserId}|{manifest.StreamType}|{snapshot.LastSequenceNumber}"));
                snapshot.LibraryStateDigest = ComputeLibraryStateDigest(snapshot);
                snapshot.Signature = CryptoService.SignData(BuildSnapshotSignablePayload(snapshot), privateKey);
                prevHash = snapshot.HeadHash;
                nextSeq = snapshot.LastSequenceNumber + 1;
            }

            foreach (var op in manifest.Operations.OrderBy(o => o.SequenceNumber).ToList())
            {
                op.SequenceNumber = nextSeq++;
                op.PrevHash = prevHash;
                op.Signature = CryptoService.SignData(BuildSignablePayload(op), privateKey);
                prevHash = ComputeOperationHash(op);
            }
            manifest.Operations = manifest.Operations.OrderBy(o => o.SequenceNumber).ToList();
            manifest.Version++;
            return true;
        }
    }

    private static string ComputeLibraryStateDigest(ManifestSnapshot snapshot)
    {
        var sb = new StringBuilder();

        // Followed, Liked, Friends, Groups
        foreach (var id in snapshot.FollowedUserIds.OrderBy(s => s, StringComparer.Ordinal)) sb.Append("f:").Append(id).Append(';');
        foreach (var id in snapshot.LikedTrackIds.OrderBy(s => s, StringComparer.Ordinal)) sb.Append("l:").Append(id).Append(';');
        foreach (var id in snapshot.FriendUserIds.OrderBy(s => s, StringComparer.Ordinal)) sb.Append("fr:").Append(id).Append(';');
        foreach (var id in snapshot.GroupIds.OrderBy(s => s, StringComparer.Ordinal)) sb.Append("g:").Append(id).Append(';');

        // EntityStates
        foreach (var ent in snapshot.EntityStates.OrderBy(e => e.TargetId, StringComparer.Ordinal).ThenBy(e => e.TargetType, StringComparer.Ordinal))
        {
            sb.Append("e:").Append(ent.TargetId).Append(':').Append(ent.TargetType).Append(':').Append(ent.ContentHash ?? string.Empty).Append('{');
            AppendCanonicalMetadata(sb, ent.Metadata);
            sb.Append("};");
        }

        // PlayCounts
        foreach (var kv in snapshot.PlayCounts.OrderBy(k => k.Key, StringComparer.Ordinal)) sb.Append("p:").Append(kv.Key).Append('=').Append(kv.Value).Append(';');

        return CryptoService.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    /// <summary>
    /// Verifies the integrity and authenticity of a manifest (a whole stream or a page of it).
    /// Checks the snapshot signature, contiguous sequence numbers, the hash chain between consecutive operations
    /// (from the stream's genesis or the snapshot's head) and each operation's signature.
    /// A page without a snapshot may start anywhere; whether its first operation connects to what the receiver holds is
    /// checked when merging.
    /// </summary>
    public bool VerifyManifest(Manifest manifest, string userPublicKey)
    {
        lock (manifest)
        {
            var expectedSeq = 0;
            string? prevHash = null;

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
                    if (op.SequenceNumber > manifest.Snapshot.LastSequenceNumber
                        || !CryptoService.VerifySignature(BuildSignablePayload(op), op.Signature, userPublicKey))
                    {
                        logger.Debug("Manifest verification failed for user {0} stream {1}: Invalid persistent operation {2}.", manifest.UserId, manifest.StreamType, op.SequenceNumber);
                        return false;
                    }
                }

                expectedSeq = manifest.Snapshot.LastSequenceNumber + 1;
                prevHash = manifest.Snapshot.HeadHash;
            }
            else if (manifest.Operations.Count > 0)
            {
                expectedSeq = manifest.Operations[0].SequenceNumber;
                if (expectedSeq == 0) prevHash = GetGenesisHash(manifest.UserId, manifest.StreamType);
            }

            for (var i = 0; i < manifest.Operations.Count; i++)
            {
                var op = manifest.Operations[i];

                if (op.SequenceNumber != expectedSeq + i)
                {
                    logger.Debug("Manifest verification failed for user {0} stream {1}: Expected sequence {2} but got {3}.", manifest.UserId, manifest.StreamType, expectedSeq + i, op.SequenceNumber);
                    return false;
                }

                if (prevHash != null && op.PrevHash != prevHash)
                {
                    logger.Debug("Manifest verification failed for user {0} stream {1}: Operation {2} does not chain to its predecessor.", manifest.UserId, manifest.StreamType, op.SequenceNumber);
                    return false;
                }

                if (!CryptoService.VerifySignature(BuildSignablePayload(op), op.Signature, userPublicKey))
                {
                    logger.Debug("Manifest verification failed for user {0} stream {1}: Invalid operation signature for sequence {2}.", manifest.UserId, manifest.StreamType, op.SequenceNumber);
                    return false;
                }

                prevHash = ComputeOperationHash(op);
            }
            return true;
        }
    }

    /// <summary>
    /// Merges a remote manifest (a whole stream or a page of it) into the local copy, appending the operations the local
    /// copy lacks. Every operation that verifies is stored as it is, so that the copy is identical to the author's log;
    /// read-side rules (the daily play cap, competition deadlines) are applied by <see cref="ManifestState"/>.
    /// Merging stops at a gap, at an operation that does not chain to the local head (a fork, raising <see cref="ForkDetected"/>),
    /// or at an operation exceeding the field limits.
    /// Returns the number of live operations appended (a newly adopted snapshot is not counted; compare heads to see it).
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
            local.AuthorPublicKey ??= remoteUserPublicKey;
            var added = 0;

            // 2. Adopt a newer snapshot. Local operations after it are kept if they continue from it.
            if (remote.Snapshot != null && remote.Snapshot.LastSequenceNumber > (local.Snapshot?.LastSequenceNumber ?? -1))
            {
                var snapshot = remote.Snapshot;
                var covered = local.Operations.FirstOrDefault(o => o.SequenceNumber == snapshot.LastSequenceNumber);
                if (covered != null && !string.IsNullOrEmpty(snapshot.HeadHash) && ComputeOperationHash(covered) != snapshot.HeadHash)
                {
                    ReportFork(local, snapshot.LastSequenceNumber);
                    return 0;
                }

                var kept = local.Operations.Where(o => o.SequenceNumber > snapshot.LastSequenceNumber).OrderBy(o => o.SequenceNumber).ToList();
                if (kept.Count > 0 && (kept[0].SequenceNumber != snapshot.LastSequenceNumber + 1 || kept[0].PrevHash != snapshot.HeadHash))
                    kept.Clear();

                local.Snapshot = snapshot;
                local.Operations = kept;
                local.Version = Math.Max(local.Version, remote.Version);
                local.LastUpdated = DateTime.UtcNow;
            }

            // 3. Append operations that continue the local head.
            var head = GetHeadSequenceNumber(local);
            var headHash = GetHeadHash(local);

            foreach (var op in remote.Operations.OrderBy(o => o.SequenceNumber))
            {
                if (op.SequenceNumber <= head)
                {
                    var existing = local.Operations.FirstOrDefault(o => o.SequenceNumber == op.SequenceNumber);
                    if (existing != null && ComputeOperationHash(existing) != ComputeOperationHash(op))
                    {
                        ReportFork(local, op.SequenceNumber);
                        break;
                    }
                    continue;
                }

                // Never create a gap in the chain: an op can only follow the local head.
                if (op.SequenceNumber != head + 1)
                {
                    logger.Debug("Stopping merge for user {0} stream {1}: expected sequence {2} but got {3} (gap).", remote.UserId, remote.StreamType, head + 1, op.SequenceNumber);
                    break;
                }

                if (op.PrevHash != headHash)
                {
                    ReportFork(local, op.SequenceNumber);
                    break;
                }

                if (!IsOperationWithinLimits(remote, op))
                    break;

                if (local.Operations.Count >= SecurityLimits.MaxManifestOperations)
                {
                    logger.Warn("Stopping merge for user {0} stream {1}: the author has {2} uncompacted operations.", remote.UserId, remote.StreamType, local.Operations.Count);
                    break;
                }

                local.Operations.Add(op);
                head = op.SequenceNumber;
                headHash = ComputeOperationHash(op);
                added++;
            }

            if (added > 0)
            {
                local.Version = Math.Max(local.Version, remote.Version);
                local.LastUpdated = DateTime.UtcNow;
            }
            return added;
        }
    }

    private void ReportFork(Manifest local, int sequenceNumber)
    {
        logger.Warn("Fork detected for user {0} stream {1} at sequence {2}: a peer offered an operation that conflicts with the one already held. Keeping the first.",
            local.UserId, local.StreamType, sequenceNumber);
        ForkDetected?.Invoke(local.UserId, local.StreamType, sequenceNumber);
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

    /// <summary>
    /// The exact data an operation's signature covers: every field (including all metadata, so that track titles, cover
    /// hashes or shader scripts cannot be altered by the peers that forward it) and the hash of the author's previous operation.
    /// </summary>
    public static string BuildSignablePayload(ManifestOperation op)
    {
        var sb = new StringBuilder();
        sb.Append(SignablePayloadVersion).Append('|');
        sb.Append(op.OperationId).Append('|');
        sb.Append(op.OperationType).Append('|');
        sb.Append(op.TargetId).Append('|');
        sb.Append(op.TargetType).Append('|');
        sb.Append(op.ContentHash ?? string.Empty).Append('|');
        sb.Append(op.SequenceNumber).Append('|');
        sb.Append(op.Timestamp.ToUniversalTime().Ticks).Append('|');
        sb.Append(op.PrevHash ?? string.Empty).Append('|');
        AppendCanonicalMetadata(sb, op.Metadata);
        return sb.ToString();
    }

    /// <summary>Length-prefixed and sorted, so that no two different dictionaries produce the same text.</summary>
    private static void AppendCanonicalMetadata(StringBuilder sb, Dictionary<string, string>? metadata)
    {
        if (metadata == null) return;
        foreach (var kv in metadata.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            var value = kv.Value ?? string.Empty;
            sb.Append(kv.Key.Length).Append(':').Append(kv.Key).Append('=').Append(value.Length).Append(':').Append(value).Append(';');
        }
    }

    public static string BuildSnapshotSignablePayload(ManifestSnapshot snapshot)
    {
        var sb = new StringBuilder();
        sb.Append(SignablePayloadVersion).Append('|');
        sb.Append(snapshot.LastSequenceNumber).Append('|');
        sb.Append(snapshot.Timestamp.ToUniversalTime().Ticks).Append('|');
        sb.Append(snapshot.HeadHash ?? string.Empty).Append('|');

        foreach (var kv in snapshot.PlayCounts.OrderBy(k => k.Key, StringComparer.Ordinal))
            sb.Append(kv.Key).Append(':').Append(kv.Value).Append(',');
        sb.Append('|');

        foreach (var id in snapshot.FollowedUserIds.OrderBy(s => s, StringComparer.Ordinal)) sb.Append(id).Append(',');
        sb.Append('|');
        foreach (var id in snapshot.LikedTrackIds.OrderBy(s => s, StringComparer.Ordinal)) sb.Append(id).Append(',');
        sb.Append('|');
        foreach (var id in snapshot.FriendUserIds.OrderBy(s => s, StringComparer.Ordinal)) sb.Append(id).Append(',');
        sb.Append('|');
        foreach (var id in snapshot.GroupIds.OrderBy(s => s, StringComparer.Ordinal)) sb.Append(id).Append(',');
        sb.Append('|');

        foreach (var entity in snapshot.EntityStates.OrderBy(e => e.TargetId, StringComparer.Ordinal).ThenBy(e => e.TargetType, StringComparer.Ordinal))
        {
            sb.Append(entity.TargetId).Append(':').Append(entity.TargetType).Append(':').Append(entity.ContentHash ?? string.Empty).Append(':');
            AppendCanonicalMetadata(sb, entity.Metadata);
            sb.Append(',');
        }
        sb.Append('|');

        // Persistent operations are signed individually; the snapshot binds which ones it preserves.
        foreach (var op in snapshot.PersistentOperations.OrderBy(o => o.SequenceNumber))
            sb.Append(ComputeOperationHash(op)).Append(',');
        sb.Append('|');

        sb.Append(snapshot.LibraryStateDigest ?? string.Empty);

        return sb.ToString();
    }
}
