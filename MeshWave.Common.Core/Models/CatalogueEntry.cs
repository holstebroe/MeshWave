namespace MeshWave.Common.Core.Models;

/// <summary>
/// Represents a flattened entry in the shared catalogue for global browsing and search.
/// </summary>
public class CatalogueEntry
{
    public required string EntryId { get; set; }
    public required CatalogueEntryType Type { get; set; }
    public required string OwnerUserId { get; set; }
    public required string Title { get; set; }
    public string? ArtistName { get; set; }
    public string? AlbumName { get; set; }
    public TimeSpan? Duration { get; set; }
    public string? ContentHash { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public string? Genre { get; set; }
    public long FileSize { get; set; }
    public int Version { get; set; }
    public string? ShaderScript { get; set; }
    public int SequenceNumber { get; set; }
    public DateTime Timestamp { get; set; }
}
