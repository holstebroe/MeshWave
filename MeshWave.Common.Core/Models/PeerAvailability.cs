namespace MeshWave.Common.Core.Models;

/// <summary>
/// Tracks which peers host which content hashes.
/// </summary>
public class PeerAvailability
{
    public required string ContentHash { get; set; }
    public HashSet<string> PeerUserIds { get; set; } = [];
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
}
