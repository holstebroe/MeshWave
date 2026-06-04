namespace MeshWave.Common.Core.Models;

/// <summary>
/// Core entity representing a competition within a group.
/// </summary>
public class Competition
{
    public Guid CompetitionId { get; set; }
    public required string GroupId { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public DateTime SubmissionDeadline { get; set; }
    public DateTime VotingDeadline { get; set; }
    public required string AdministratorUserId { get; set; }
    public bool IsResultsPublicBeforeReveal { get; set; }
}

/// <summary>
/// Represents a track entry in a competition.
/// </summary>
public class CompetitionSubmission
{
    public required string TrackId { get; set; }
    public required string UserId { get; set; }
    public DateTime SubmissionTimestamp { get; set; }
}

/// <summary>
/// Represents a user's sealed vote in a competition.
/// </summary>
public class CompetitionVote
{
    public required string VoterUserId { get; set; }
    public required string EncryptedVotePayload { get; set; }
    public DateTime VoteTimestamp { get; set; }
}

/// <summary>
/// Represents the final rankings of a competition.
/// </summary>
public class CompetitionResult
{
    public List<string> OrderedTrackIds { get; set; } = [];
    public DateTime TallyTimestamp { get; set; }
    public required string AdministratorSignature { get; set; }
}
