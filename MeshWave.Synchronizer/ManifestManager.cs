using System.Text;
using System.Text.Json;
using MeshWave.Common.Core.Crypto;
using MeshWave.Common.Core.Models;

namespace MeshWave.Synchronizer;

/// <summary>
/// ManifestManager handles creation, signing, and management of user manifests.
/// Manifests are append-only, signed lists of operations on the user's content.
/// </summary>
public class ManifestManager
{
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
        var operation = new ManifestOperation
        {
            OperationId = Guid.NewGuid().ToString(),
            OperationType = type,
            TargetId = targetId,
            TargetType = targetType,
            ContentHash = contentHash,
            SequenceNumber = manifest.Operations.Count,
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

    /// <summary>
    /// Adds a pre-built operation to the manifest (create, update, or delete).
    /// Assigns sequence number and increments manifest version.
    /// </summary>
    public void AppendOperation(Manifest manifest, ManifestOperation operation)
    {
        operation.SequenceNumber = manifest.Operations.Count;
        manifest.Operations.Add(operation);
        manifest.Version++;
        manifest.LastUpdated = DateTime.UtcNow;
    }

    /// <summary>
    /// Verifies the integrity and authenticity of a manifest.
    /// Checks monotonic sequence numbers and each operation's RSA signature.
    /// </summary>
    public bool VerifyManifest(Manifest manifest, string userPublicKey)
    {
        for (int i = 0; i < manifest.Operations.Count; i++)
        {
            var op = manifest.Operations[i];

            var expectedSeq = manifest.Operations.Count > 0
                ? manifest.Operations[0].SequenceNumber + i
                : i;

            if (op.SequenceNumber != expectedSeq)
                return false;

            var signable = BuildSignablePayload(op);
            if (!CryptoService.VerifySignature(signable, op.Signature, userPublicKey))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Merges a remote manifest into a local one, appending any operations the local copy lacks.
    /// Only operations that pass signature verification are accepted.
    /// Rejects manifests that exceed security limits.
    /// Play operations are capped at <see cref="SecurityLimits.MaxPlaysPerUserPerTrackPerDay"/> per track per UTC day.
    /// Returns the number of new operations added.
    /// </summary>
    public int MergeManifest(Manifest local, Manifest remote, string remoteUserPublicKey)
    {
        if (local.UserId != remote.UserId)
            throw new ArgumentException("Cannot merge manifests from different users.");

        if (remote.Operations.Count > SecurityLimits.MaxManifestOperations)
            throw new InvalidDataException($"Remote manifest exceeds operation limit ({remote.Operations.Count}).");

        // Build existing play counts per (trackId, utcDate) from the local manifest so we
        // know how much headroom remains before merging remote play ops.
        var playCounts = BuildPlayCounts(local.Operations);

        int added = 0;
        var localSeq = local.Operations.Count;

        foreach (var op in remote.Operations.OrderBy(o => o.SequenceNumber))
        {
            if (op.SequenceNumber < localSeq)
                continue;

            if (!IsOperationWithinLimits(op))
                continue;

            // Enforce per-user daily play cap.
            if (op.OperationType == ManifestOperationType.Play)
            {
                var key = (TrackId: op.TargetId, Date: op.Timestamp.ToUniversalTime().Date);
                playCounts.TryGetValue(key, out var existing);
                if (existing >= SecurityLimits.MaxPlaysPerUserPerTrackPerDay)
                    continue;
                playCounts[key] = existing + 1;
            }

            var signable = BuildSignablePayload(op);
            if (!CryptoService.VerifySignature(signable, op.Signature, remoteUserPublicKey))
                continue;

            local.Operations.Add(op);
            local.Version = Math.Max(local.Version, remote.Version);
            local.LastUpdated = DateTime.UtcNow;
            localSeq++;
            added++;
        }

        return added;
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
            var key = (TrackId: op.TargetId, Date: op.Timestamp.ToUniversalTime().Date);
            counts.TryGetValue(key, out var c);
            counts[key] = c + 1;
        }
        return counts;
    }

    private static bool IsOperationWithinLimits(ManifestOperation op)
    {
        if (op.OperationId.Length > SecurityLimits.MaxOperationIdLength) return false;
        if (op.TargetId.Length > SecurityLimits.MaxTargetIdLength) return false;
        if (op.TargetType.Length > SecurityLimits.MaxTargetTypeLength) return false;
        if (op.ContentHash?.Length > SecurityLimits.MaxContentHashLength) return false;
        if (op.Metadata.Count > SecurityLimits.MaxMetadataEntries) return false;

        foreach (var kv in op.Metadata)
        {
            if (kv.Key.Length > SecurityLimits.MaxMetadataKeyLength) return false;
            if (kv.Value.Length > SecurityLimits.MaxMetadataValueLength) return false;
        }

        return true;
    }

    private static string BuildSignablePayload(ManifestOperation op)
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
}
