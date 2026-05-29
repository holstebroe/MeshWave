namespace MeshWave.Common.Core.Models;

/// <summary>
/// Represents a community group where users can organize and share music.
/// </summary>
public class Community
{
    public required string CommunityId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public List<string> MemberUserIds { get; set; } = [];
    public string? CoverImageHash { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
