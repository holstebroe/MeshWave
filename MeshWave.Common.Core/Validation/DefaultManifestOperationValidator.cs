using MeshWave.Common.Core.Models;

namespace MeshWave.Common.Core.Validation;

public class DefaultManifestOperationValidator : IManifestOperationValidator
{
    public bool IsValid(ManifestOperation op, string userId, out string? rejectionReason)
    {
        if (op.OperationId.Length > SecurityLimits.MaxOperationIdLength)
        {
            rejectionReason = $"OperationId length {op.OperationId.Length} exceeds limit {SecurityLimits.MaxOperationIdLength}";
            return false;
        }
        if (op.TargetId.Length > SecurityLimits.MaxTargetIdLength)
        {
            rejectionReason = $"TargetId length {op.TargetId.Length} exceeds limit {SecurityLimits.MaxTargetIdLength}";
            return false;
        }
        if (op.TargetType.Length > SecurityLimits.MaxTargetTypeLength)
        {
            rejectionReason = $"TargetType length {op.TargetType.Length} exceeds limit {SecurityLimits.MaxTargetTypeLength}";
            return false;
        }
        if (op.ContentHash?.Length > SecurityLimits.MaxContentHashLength)
        {
            rejectionReason = $"ContentHash length {op.ContentHash.Length} exceeds limit {SecurityLimits.MaxContentHashLength}";
            return false;
        }
        if (op.Metadata.Count > SecurityLimits.MaxMetadataEntries)
        {
            rejectionReason = $"Metadata count {op.Metadata.Count} exceeds limit {SecurityLimits.MaxMetadataEntries}";
            return false;
        }

        foreach (var kv in op.Metadata)
        {
            if (kv.Key.Length > SecurityLimits.MaxMetadataKeyLength)
            {
                rejectionReason = $"Metadata Key length {kv.Key.Length} exceeds limit {SecurityLimits.MaxMetadataKeyLength}";
                return false;
            }
            if (kv.Value.Length > SecurityLimits.MaxMetadataValueLength)
            {
                rejectionReason = $"Metadata Value length {kv.Value.Length} exceeds limit {SecurityLimits.MaxMetadataValueLength}";
                return false;
            }
        }

        rejectionReason = null;
        return true;
    }
}
