using MeshWave.Common.Core.Models;

namespace MeshWave.Synchronizer;

/// <summary>
/// Minimal synchronization surface used by browse/community view models.
/// Enables view-model unit testing through mocking.
/// </summary>
public interface ISyncBrowseClient
{
    bool IsRunning { get; }
    IReadOnlyCollection<Manifest> PeerManifests { get; }
    Manifest? LocalManifest { get; }
    event EventHandler<ManifestMergedEventArgs>? ManifestMerged;
    Task<byte[]?> RequestContentAsync(string peerUserId, string contentHash);
}
