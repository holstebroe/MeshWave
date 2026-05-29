namespace MeshWave.Common.Core.Models;

/// <summary>
/// Represents a comment on a track or album.
/// Comments are authored/signed by the commenter.
/// Can optionally link to a specific time position in a track (in seconds).
/// </summary>
public class Comment
{
    public required string CommentId { get; set; }
    public required string AuthorUserId { get; set; }
    public required CommentTargetType TargetType { get; set; }
    public required string TargetId { get; set; }
    public double? TimestampInTrackSeconds { get; set; }
    public required string Text { get; set; }
    public required string Signature { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsHidden { get; set; } = false;
}

public enum CommentTargetType
{
    Album,
    Track
}
