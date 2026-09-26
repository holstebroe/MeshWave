using System;
using System.Collections.Generic;
using MeshWave.Common.Core.Crypto;
using MeshWave.Common.Core.Models;
using Xunit;

namespace MeshWave.Synchronizer.Tests;

/// <summary>
/// Competition rules (deadlines, administrator) are applied when operations are read (<see cref="ManifestState"/>),
/// not by dropping them during merge: every peer replicates the author's log verbatim so that streams stay identical.
/// </summary>
public class CompetitionValidationTests
{
    private readonly ManifestManager _manager = new();

    private static ManifestOperation Competition(string compId, DateTime submissionDeadline, DateTime votingDeadline, string adminId = "admin-1")
    {
        return new ManifestOperation
        {
            OperationId = Guid.NewGuid().ToString(),
            OperationType = ManifestOperationType.CreateCompetition,
            TargetId = compId,
            TargetType = "Competition",
            Signature = "sig",
            Metadata = new Dictionary<string, string>
            {
                { "SubmissionDeadline", submissionDeadline.ToString("O") },
                { "VotingDeadline", votingDeadline.ToString("O") },
                { "AdministratorUserId", adminId }
            }
        };
    }

    private static ManifestOperation Op(ManifestOperationType type, string compId, DateTime timestamp)
    {
        return new ManifestOperation
        {
            OperationId = Guid.NewGuid().ToString(),
            OperationType = type,
            TargetId = compId,
            TargetType = "Competition",
            Signature = "sig",
            Timestamp = timestamp
        };
    }

    [Fact]
    public void CompetitionSubmit_Valid_WithinDeadline()
    {
        var comp = Competition("c1", DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(2));
        Assert.True(ManifestState.IsValidCompetitionOperation("user-1", Op(ManifestOperationType.CompetitionSubmit, "c1", DateTime.UtcNow), comp));
    }

    [Fact]
    public void CompetitionSubmit_Invalid_PastDeadline()
    {
        var comp = Competition("c1", DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddDays(1));
        Assert.False(ManifestState.IsValidCompetitionOperation("user-1", Op(ManifestOperationType.CompetitionSubmit, "c1", DateTime.UtcNow), comp));
    }

    [Fact]
    public void CompetitionSubmit_Invalid_WithoutKnownCompetition()
    {
        Assert.False(ManifestState.IsValidCompetitionOperation("user-1", Op(ManifestOperationType.CompetitionSubmit, "c1", DateTime.UtcNow), null));
    }

    [Fact]
    public void CompetitionCastVote_Invalid_BeforeSubmissionDeadline()
    {
        var comp = Competition("c1", DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(2));
        Assert.False(ManifestState.IsValidCompetitionOperation("user-1", Op(ManifestOperationType.CompetitionCastVote, "c1", DateTime.UtcNow), comp));
    }

    [Fact]
    public void CompetitionCastVote_Valid_InVotingWindow()
    {
        var comp = Competition("c1", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));
        Assert.True(ManifestState.IsValidCompetitionOperation("user-1", Op(ManifestOperationType.CompetitionCastVote, "c1", DateTime.UtcNow), comp));
    }

    [Fact]
    public void CompetitionRevealResults_Invalid_NotAdmin()
    {
        var comp = Competition("c1", DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddDays(-1), adminId: "admin-1");
        Assert.False(ManifestState.IsValidCompetitionOperation("admin-2", Op(ManifestOperationType.CompetitionRevealResults, "c1", DateTime.UtcNow), comp));
        Assert.True(ManifestState.IsValidCompetitionOperation("admin-1", Op(ManifestOperationType.CompetitionRevealResults, "c1", DateTime.UtcNow), comp));
    }

    [Fact]
    public void Merge_KeepsCompetitionOperationsVerbatim_EvenWhenInvalid()
    {
        var (privateKey, publicKey) = CryptoService.GenerateKeyPair();
        var userId = CryptoService.DeriveUserIdFromPublicKey(publicKey);
        var remote = _manager.CreateManifest(userId);
        remote.StreamType = ManifestStreamType.Social;
        _manager.AppendSignedOperation(remote, ManifestOperationType.CompetitionCastVote, "unknown-competition", "Competition", "vote", null, privateKey);

        var local = _manager.CreateManifest(userId);
        local.StreamType = ManifestStreamType.Social;
        var added = _manager.MergeManifest(local, remote, publicKey);

        Assert.Equal(1, added);
        Assert.Equal(ManifestManager.GetHead(remote), ManifestManager.GetHead(local));
    }
}
