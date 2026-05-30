namespace MeshWave.Common.Core.Models;

/// <summary>
/// Represents an album (collection of tracks) in the MeshWave network.
/// </summary>
public class Album
{
    public required string AlbumId { get; set; }
    public required string OwnerUserId { get; set; }
    public required string Title { get; set; }
    public string? CoverImageHash { get; set; }
    public string? Description { get; set; }
    public List<string> TrackIds { get; set; } = [];
    public int MetaVersion { get; set; } = 1;
    public required string Signature { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>UTC timestamp when this album was first announced to the network. Null = not yet released.</summary>
    public DateTime? ReleasedAt { get; set; }
}
