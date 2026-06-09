using System.Collections.Generic;
using MeshWave.Common.Core.Models;

namespace MeshWave.Synchronizer;

/// <summary>
/// Defines a contract for persisting and managing remote peer manifests.
/// Allows swapping out the underlying storage mechanism (e.g., File System, Database).
/// </summary>
public interface IManifestStore
{
    /// <summary>
    /// Loads all persisted peer manifests into memory. Call once at application start.
    /// </summary>
    void LoadAll();

    /// <summary>
    /// Returns the cached manifest for the specified user and stream type, or null if not found.
    /// </summary>
    Manifest? Get(string userId, ManifestStreamType streamType = ManifestStreamType.Content);

    /// <summary>
    /// Returns all currently cached peer manifests.
    /// </summary>
    IReadOnlyCollection<Manifest> GetAll();

    /// <summary>
    /// Merges an incoming manifest into the store and persists the changes.
    /// Returns the number of new operations merged.
    /// </summary>
    int MergeAndSave(Manifest incoming, string peerPublicKeyPem, ManifestManager manager);

    /// <summary>
    /// Removes the persisted manifests for a peer from the store.
    /// </summary>
    void Remove(string userId);

    /// <summary>
    /// Clears all cached peer manifests from memory and underlying storage.
    /// </summary>
    void ClearAll();
}
