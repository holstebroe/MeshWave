namespace MeshWave.Common.Core.Models;

/// <summary>
/// Represents a user identity in the MeshWave network.
/// UserId is derived from the public key fingerprint.
/// </summary>
public class User
{
    public required string UserId { get; set; }
    public required string DisplayName { get; set; }
    public required string PublicKeyPem { get; set; }
    public string? Description { get; set; }
    public string? CoverImageHash { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
