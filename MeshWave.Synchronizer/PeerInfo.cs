namespace MeshWave.Synchronizer;

/// <summary>
/// Represents information about a discovered peer.
/// </summary>
public class PeerInfo
{
    public required string UserId { get; set; }
    public required string DisplayName { get; set; }
    public required string Address { get; set; }
    public int Port { get; set; }
    public string PublicKeyPem { get; set; } = string.Empty;
    public DateTime LastSeen { get; set; }
    public List<string> Capabilities { get; set; } = [];
}
