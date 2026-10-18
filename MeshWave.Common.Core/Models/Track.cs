using MeshWave.Common.Core.Enums;

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
    public string? FilePath { get; set; }
    public long FileSize { get; set; }
    public string? CoverImageHash { get; set; }
    public string? Description { get; set; }
    public int MetaVersion { get; set; } = 1;
    public required string Signature { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>UTC timestamp when this track was first announced to the network. Null = not yet released.</summary>
    public DateTime? ReleasedAt { get; set; }
    public TrackAvailabilityState AvailabilityState { get; set; } = TrackAvailabilityState.Downloaded;
    public string? ContentHash { get; set; }
}
