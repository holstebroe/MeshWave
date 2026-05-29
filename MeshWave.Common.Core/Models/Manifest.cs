namespace MeshWave.Common.Core.Models;

/// <summary>
/// Represents a signed operation in the append-only manifest.
/// Operations are: Create, Update, Delete (tombstone).
/// </summary>
public class ManifestOperation
{
    public required string OperationId { get; set; }
    public required ManifestOperationType OperationType { get; set; }
    public required string TargetId { get; set; }
    public required string TargetType { get; set; }
    public string? ContentHash { get; set; }
    public int SequenceNumber { get; set; }
    public required string Signature { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Dictionary<string, string> Metadata { get; set; } = [];
}

public enum ManifestOperationType
{
    Create,
    Update,
    Delete
}

/// <summary>
/// Manifest: append-only list of signed operations per user.
/// </summary>
public class Manifest
{
    public required string UserId { get; set; }
    public List<ManifestOperation> Operations { get; set; } = [];
    public int Version { get; set; } = 1;
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
