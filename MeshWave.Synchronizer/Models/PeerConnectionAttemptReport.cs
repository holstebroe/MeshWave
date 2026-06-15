using System;
using System.Collections.Generic;
using System.Linq;

namespace MeshWave.Synchronizer;

public sealed class PeerConnectionAttemptReport
{
    public required string PeerUserId { get; init; }
    public required string RequestedContentHash { get; init; }
    public string? TargetAddress { get; set; }
    public int TargetPort { get; set; }
    public int LocalManifestPort { get; init; }
    public string? SuggestedLocalIp { get; init; }
    public DateTime CreatedAtUtc { get; } = DateTime.UtcNow;
    public List<PeerConnectionAttemptResult> Attempts { get; } = [];

    public string BuildUserFacingSummary()
    {
        var attemptSummary = string.Join(" | ", Attempts.Select(a => $"{a.Method}: {(a.Success ? "ok" : "fail")}"));
        var finalGuidance = Attempts.LastOrDefault(a => string.Equals(a.Method, "nat-guidance", StringComparison.OrdinalIgnoreCase))?.Details;
        return string.IsNullOrWhiteSpace(finalGuidance)
            ? attemptSummary
            : $"{attemptSummary}{Environment.NewLine}{finalGuidance}";
    }
}

public sealed record PeerConnectionAttemptResult(string Method, bool Success, string Details);
