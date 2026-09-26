namespace MeshWave.Common.Core.Models;

/// <summary>
/// Read-side interpretation of a replicated manifest.
/// Peers replicate every validly signed operation verbatim, so that a stream is identical on every peer (its head can be
/// compared and any peer can serve it). Rules that depend on the reader's view, such as the daily play cap or competition
/// deadlines, are therefore applied here, when the operations are counted, rather than by dropping operations during merge.
/// </summary>
public static class ManifestState
{
    /// <summary>
    /// Number of plays of <paramref name="trackId"/>: the play count squashed into the snapshot plus the live Play operations,
    /// counting at most <see cref="SecurityLimits.MaxPlaysPerUserPerTrackPerDay"/> per UTC day.
    /// </summary>
    public static int CountPlays(Manifest manifest, string trackId)
    {
        var fromSnapshot = manifest.Snapshot?.PlayCounts.GetValueOrDefault(trackId) ?? 0;
        return fromSnapshot + CountCappedPlays(manifest.Operations.Where(op => IsTrackOp(op, trackId)));
    }

    /// <summary>Counts Play operations, at most <see cref="SecurityLimits.MaxPlaysPerUserPerTrackPerDay"/> per track per UTC day.</summary>
    public static int CountCappedPlays(IEnumerable<ManifestOperation> operations)
    {
        return operations
            .Where(op => op.OperationType == ManifestOperationType.Play)
            .GroupBy(op => (Track: op.TargetId.ToLowerInvariant(), Day: op.Timestamp.ToUniversalTime().Date))
            .Sum(g => Math.Min(g.Count(), SecurityLimits.MaxPlaysPerUserPerTrackPerDay));
    }

    /// <summary>
    /// Whether the author currently likes <paramref name="trackId"/>: the latest live Like/Unlike wins, otherwise the snapshot's liked set.
    /// </summary>
    public static bool IsLiked(Manifest manifest, string trackId)
    {
        var latest = manifest.Operations
            .Where(op => IsTrackOp(op, trackId) && op.OperationType is ManifestOperationType.Like or ManifestOperationType.Unlike)
            .MaxBy(op => op.SequenceNumber);
        if (latest != null)
            return latest.OperationType == ManifestOperationType.Like;

        return manifest.Snapshot?.LikedTrackIds.Contains(trackId, StringComparer.OrdinalIgnoreCase) == true;
    }

    /// <summary>
    /// Whether a competition operation by <paramref name="authorUserId"/> obeys the competition's deadlines and roles.
    /// <paramref name="createCompetition"/> is the competition's CreateCompetition operation; without it only
    /// CreateCompetition itself is valid.
    /// </summary>
    public static bool IsValidCompetitionOperation(string authorUserId, ManifestOperation op, ManifestOperation? createCompetition)
    {
        var skewMargin = TimeSpan.FromHours(1);

        if (op.OperationType == ManifestOperationType.CreateCompetition)
            return true;
        if (createCompetition == null)
            return false;

        switch (op.OperationType)
        {
            case ManifestOperationType.CompetitionSubmit:
                if (TryGetDeadline(createCompetition, "SubmissionDeadline", out var submissionDeadline) && op.Timestamp > submissionDeadline + skewMargin)
                    return false;
                break;
            case ManifestOperationType.CompetitionCastVote:
                if (TryGetDeadline(createCompetition, "SubmissionDeadline", out var votingOpens) && op.Timestamp < votingOpens - skewMargin)
                    return false;
                if (TryGetDeadline(createCompetition, "VotingDeadline", out var votingDeadline) && op.Timestamp > votingDeadline + skewMargin)
                    return false;
                break;
            case ManifestOperationType.CompetitionRevealResults:
                if (createCompetition.Metadata.TryGetValue("AdministratorUserId", out var adminId) && authorUserId != adminId)
                    return false;
                break;
        }
        return true;
    }

    private static bool TryGetDeadline(ManifestOperation createCompetition, string key, out DateTime deadline)
    {
        deadline = default;
        return createCompetition.Metadata.TryGetValue(key, out var value)
               && DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out deadline);
    }

    private static bool IsTrackOp(ManifestOperation op, string trackId)
    {
        return string.Equals(op.TargetType, "Track", StringComparison.OrdinalIgnoreCase)
               && string.Equals(op.TargetId, trackId, StringComparison.OrdinalIgnoreCase);
    }
}
