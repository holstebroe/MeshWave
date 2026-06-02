using System.Collections.Generic;
using System.Threading.Tasks;
using MeshWave.Common.Core.Models;

namespace MeshWave.Common.Core;

/// <summary>
/// Service for ingestion, deduplication, and lookup of shared catalogue metadata.
/// </summary>
public interface ICatalogueService
{
    /// <summary>
    /// Ingests metadata from a peer manifest.
    /// Handles stale metadata by comparing SequenceNumber or Timestamp.
    /// </summary>
    Task IngestAsync(Manifest manifest);

    /// <summary>
    /// Searches the shared catalogue for entries matching the query.
    /// </summary>
    Task<IEnumerable<CatalogueEntry>> SearchAsync(string query);

    /// <summary>
    /// Retrieves a specific entry from the catalogue.
    /// </summary>
    Task<CatalogueEntry?> GetEntryAsync(string entryId);

    /// <summary>
    /// Retrieves the list of peers known to host a specific content hash.
    /// </summary>
    Task<IEnumerable<string>> GetPeersForContentAsync(string contentHash);
}
