using MeshWave.Common.Core.Models;
using System.Collections.Generic;

namespace MeshWave.Common.Core.Processors;

public interface ICatalogueEntryProcessor
{
    string TargetType { get; }

    bool ValidateUpdate(CatalogueEntry? existingEntry, string? contentHash, Dictionary<string, string> metadata);

    CatalogueEntry MapMetadata(string targetId, string userId, string? contentHash, Dictionary<string, string> metadata, int sequenceNumber, DateTime timestamp);
}
