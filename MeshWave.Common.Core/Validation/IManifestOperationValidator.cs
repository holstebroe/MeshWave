using MeshWave.Common.Core.Models;

namespace MeshWave.Common.Core.Validation;

public interface IManifestOperationValidator
{
    bool IsValid(ManifestOperation op, string userId, out string? rejectionReason);
}
