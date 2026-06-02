using System;
using System.Collections.Generic;

namespace MeshWave.Common.Core.Models;

/// <summary>
/// Represents a playlist in the MeshWave network.
/// </summary>
public class Playlist
{
    public required string PlaylistId { get; set; }
    public required string OwnerUserId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    /// <summary>
    /// List of track references in the format "ArtistUserId:TrackId".
    /// </summary>
    public List<string> TrackReferences { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
