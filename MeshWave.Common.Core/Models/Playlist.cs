namespace MeshWave.Common.Core.Models;

/// <summary>
/// Represents a user-curated playlist in the MeshWave network.
/// </summary>
public class Playlist
{
    public required string PlaylistId { get; set; }
    public required string OwnerUserId { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public List<string> TrackIds { get; set; } = [];
    public int MetaVersion { get; set; } = 1;
    public required string Signature { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>UTC timestamp when this playlist was first announced to the network. Null = private.</summary>
    public DateTime? ReleasedAt { get; set; }
}
