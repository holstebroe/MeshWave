namespace MeshWave.Common.Core.Models;

public sealed class TimelineCommentMarker
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public double TimestampSeconds { get; set; }
    public string Comment { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public int TrackVersion { get; set; } = 1;
    /// <summary>Id of the marker this is a reply to, or null/empty for top-level comments.</summary>
    public string? ReplyToId { get; set; }
}
