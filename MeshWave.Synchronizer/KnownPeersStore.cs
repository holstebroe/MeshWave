using System.Collections.Concurrent;
using System.Text.Json;
using MeshWave.Common.Core;
using MeshWave.Common.Core.P2P;

namespace MeshWave.Synchronizer;

/// <summary>
/// Persists successful peer connection details locally to allow PEX bootstrap resilience.
/// </summary>
public class KnownPeersStore
{
    private const string KnownPeersFileName = "known_peers.json";
    private readonly string _storeFile;
    private readonly ConcurrentDictionary<string, PeerInfo> _knownPeers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _diskLock = new();

    public void Flush()
    {
        lock (_diskLock)
        {
            try
            {
                var list = _knownPeers.Values.ToList();
                var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = false });
                File.WriteAllText(_storeFile, json);
            }
            catch { /* best effort */ }
        }
    }

    public KnownPeersStore(string? baseFolder = null)
    {
        var folder = baseFolder ?? MeshWaveEnvironment.GetAppDataRoot();
        Directory.CreateDirectory(folder);
        _storeFile = Path.Combine(folder, KnownPeersFileName);
    }

    public void LoadAll()
    {
        if (!File.Exists(_storeFile)) return;

        try
        {
            var json = File.ReadAllText(_storeFile);
            var peers = JsonSerializer.Deserialize<List<PeerInfo>>(json);
            if (peers != null)
            {
                foreach (var peer in peers)
                {
                    if (!string.IsNullOrWhiteSpace(peer.UserId) && !peer.UserId.StartsWith("bootstrap:"))
                    {
                        _knownPeers[peer.UserId] = peer;
                    }
                }
            }
        }
        catch
        {
            // skip corrupted files
        }
    }

    public void AddOrUpdate(PeerInfo peer)
    {
        if (string.IsNullOrWhiteSpace(peer.UserId) || peer.UserId.StartsWith("bootstrap:")) return;

        _knownPeers[peer.UserId] = peer;
        SaveToDisk();
    }

    public void Remove(string userId)
    {
        if (_knownPeers.TryRemove(userId, out _))
        {
            SaveToDisk();
        }
    }

    public IReadOnlyCollection<PeerInfo> GetAll()
    {
        return _knownPeers.Values.ToList();
    }

    public void PruneStale(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        bool changed = false;
        foreach (var kvp in _knownPeers.ToArray())
        {
            if (kvp.Value.LastSeen < cutoff)
            {
                if (_knownPeers.TryRemove(kvp.Key, out _))
                {
                    changed = true;
                }
            }
        }

        if (changed)
        {
            SaveToDisk();
        }
    }

    private void SaveToDisk()
    {
        Task.Run(() =>
        {
            Flush();
        });
    }
}
