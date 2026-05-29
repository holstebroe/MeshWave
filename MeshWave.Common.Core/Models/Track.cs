namespace MeshWave.Common.Core.Models;

/// <summary>
/// Represents a track (song/audio file) in the MeshWave network.
/// </summary>
public class Track
{
    public required string TrackId { get; set; }
    public string? AlbumId { get; set; }
    public required string OwnerUserId { get; set; }
    public required string Title { get; set; }
    public TimeSpan Duration { get; set; }
    public required string FileHash { get; set; }
    public long FileSize { get; set; }
    public string? CoverImageHash { get; set; }
    public string? Description { get; set; }
    public int MetaVersion { get; set; } = 1;
    public required string Signature { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
