namespace MeshWave.Synchronizer;

/// <summary>
/// PeerDiscovery handles discovery of peers on the local network.
/// </summary>
public class PeerDiscovery
{
    /// <summary>
    /// Starts listening for peer announcements on the local network.
    /// </summary>
    public async Task StartDiscoveryAsync()
    {
        // TODO: Implement peer discovery
        // - Use mDNS or UDP broadcast for LAN discovery
        // - Optional: Connect to bootstrap peers for internet discovery
        await Task.CompletedTask;
    }

    /// <summary>
    /// Stops listening for peer announcements.
    /// </summary>
    public async Task StopDiscoveryAsync()
    {
        // TODO: Cleanup discovery resources
        await Task.CompletedTask;
    }

    /// <summary>
    /// Gets list of currently discovered peers.
    /// </summary>
    public IEnumerable<PeerInfo> GetDiscoveredPeers()
    {
        // TODO: Return discovered peers
        return [];
    }
}

/// <summary>
/// Represents information about a discovered peer.
/// </summary>
public class PeerInfo
{
    public required string UserId { get; set; }
    public required string DisplayName { get; set; }
    public required string Address { get; set; }
    public int Port { get; set; }
    public DateTime LastSeen { get; set; }
}
