using System;
using System.Collections.Generic;
using MeshWave.Common.Core.Models;

namespace MeshWave.Common.Core.Processors;

public class TrackProcessor : BaseCatalogueEntryProcessor
{
    public override string TargetType => CatalogueEntryType.Track;

    public override bool ValidateUpdate(CatalogueEntry? existingEntry, string? contentHash, Dictionary<string, string> metadata)
    {
        var incomingVersion = int.TryParse(metadata.GetValueOrDefault("version") ?? metadata.GetValueOrDefault("trackVersion"), out var v) ? v : 1;

        if (existingEntry != null)
        {
            if (incomingVersion < existingEntry.Version)
                return false; // Reject older versions

            if (incomingVersion == existingEntry.Version && !string.Equals(existingEntry.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase))
                return false; // Hash immutability: same version must have same hash
        }

        return true;
    }
}
