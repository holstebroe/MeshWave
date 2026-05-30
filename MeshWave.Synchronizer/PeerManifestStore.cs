using System.Collections.Concurrent;
using System.Text.Json;
using MeshWave.Common.Core.Models;

namespace MeshWave.Synchronizer;

/// <summary>
/// Persists and manages one <see cref="Manifest"/> per remote peer.
///
/// Each peer manifest is stored on disk as:
///   {storeDirectory}/{userId}.json
///
/// The store is the single source of truth for all received peer data.
/// The local user's own manifest is intentionally NOT stored here.
/// </summary>
public class PeerManifestStore
{
    private readonly string _storeDirectory;
    private readonly ConcurrentDictionary<string, Manifest> _manifests = new(StringComparer.OrdinalIgnoreCase);

    public PeerManifestStore(string? storeDirectory = null)
    {
        _storeDirectory = storeDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MeshWave",
            "PeerManifests");
    }

    /// <summary>
    /// Loads all persisted peer manifests from disk.  Call once at application start.
    /// </summary>
    public void LoadAll()
    {
        if (!Directory.Exists(_storeDirectory))
            return;

        foreach (var file in Directory.EnumerateFiles(_storeDirectory, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var manifest = JsonSerializer.Deserialize<Manifest>(json);
                if (manifest != null && !string.IsNullOrWhiteSpace(manifest.UserId))
                    _manifests[manifest.UserId] = manifest;
            }
            catch { /* skip corrupted files */ }
        }
    }

    /// <summary>
    /// Returns the cached manifest for <paramref name="userId"/>, or null if not yet received.
    /// </summary>
    public Manifest? Get(string userId)
        => _manifests.TryGetValue(userId, out var m) ? m : null;

    /// <summary>
    /// Returns all currently cached peer manifests.
    /// </summary>
    public IReadOnlyCollection<Manifest> GetAll()
        => _manifests.Values.ToList();

    /// <summary>
    /// Merges <paramref name="incoming"/> into the cached manifest for its owner.
    /// Creates a new entry if this is the first manifest from that peer.
    /// Persists to disk after merging.  Returns the number of new operations merged.
    /// </summary>
    public int MergeAndSave(Manifest incoming, string peerPublicKeyPem, ManifestManager manager)
    {
        if (string.IsNullOrWhiteSpace(incoming.UserId)) return 0;

        var local = _manifests.GetOrAdd(incoming.UserId, _ => manager.CreateManifest(incoming.UserId));

        int added;
        try
        {
            added = manager.MergeManifest(local, incoming, peerPublicKeyPem);
        }
        catch
        {
            return 0; // reject tampered / over-limit manifests
        }

        if (added > 0)
            SaveToDisk(local);

        return added;
    }

    /// <summary>
    /// Removes the persisted manifest for a peer (e.g. when they are evicted from the routing table).
    /// </summary>
    public void Remove(string userId)
    {
        _manifests.TryRemove(userId, out _);
        var path = FilePath(userId);
        if (File.Exists(path))
        {
            try { File.Delete(path); } catch { }
        }
    }

    /// <summary>
    /// Clears all cached peer manifests from memory and disk.
    /// </summary>
    public void ClearAll()
    {
        _manifests.Clear();

        if (!Directory.Exists(_storeDirectory))
            return;

        foreach (var file in Directory.EnumerateFiles(_storeDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            try { File.Delete(file); } catch { }
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────

    private void SaveToDisk(Manifest manifest)
    {
        try
        {
            Directory.CreateDirectory(_storeDirectory);
            var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = false });
            File.WriteAllText(FilePath(manifest.UserId), json);
        }
        catch { /* best-effort disk write */ }
    }

    private string FilePath(string userId)
    {
        // Sanitise userId to a safe filename  (it is already a GUID-like string per P2PIdentityService)
        var safe = string.Concat(userId.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'));
        return Path.Combine(_storeDirectory, $"{safe}.json");
    }
}
