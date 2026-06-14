using System;
using System.Collections.Generic;
using MeshWave.Common.Core.Crypto;
using MeshWave.Common.Core.Models;
using Xunit;

namespace MeshWave.Synchronizer.Tests;

public class CompetitionValidationTests
{
    private readonly ManifestManager _manager = new();

    [Fact]
    public void CompetitionSubmit_Valid_WithinDeadline()
    {
        var (privateKey, publicKey) = CryptoService.GenerateKeyPair();
        var local = _manager.CreateManifest("user-1");
        var remote = _manager.CreateManifest("user-1");

        var compId = Guid.NewGuid().ToString();

        var submitOp = _manager.AppendSignedOperation(
            remote,
            ManifestOperationType.CompetitionSubmit,
            compId,
            "Track",
            "hash123",
            null,
            privateKey);

        submitOp.Timestamp = DateTime.UtcNow;
        submitOp.Signature = CryptoService.SignData(ManifestManager.BuildSignablePayload(submitOp), privateKey);

        var createOp = new ManifestOperation
        {
            OperationId = Guid.NewGuid().ToString(),
            OperationType = ManifestOperationType.CreateCompetition,
            TargetId = compId,
            TargetType = "Competition",
            Signature = "sig",
            Metadata = new Dictionary<string, string>
            {
                { "SubmissionDeadline", DateTime.UtcNow.AddDays(1).ToString("O") },
                { "VotingDeadline", DateTime.UtcNow.AddDays(2).ToString("O") },
                { "AdministratorUserId", "admin-1" }
            }
        };

        var added = _manager.MergeManifest(local, remote, publicKey, id => id == compId ? createOp : null);
        Assert.Equal(1, added);
        Assert.Single(local.Operations);
    }

    [Fact]
    public void CompetitionSubmit_Invalid_PastDeadline()
    {
        var (privateKey, publicKey) = CryptoService.GenerateKeyPair();
        var local = _manager.CreateManifest("user-1");
        var remote = _manager.CreateManifest("user-1");

        var compId = Guid.NewGuid().ToString();

        var submitOp = _manager.AppendSignedOperation(
            remote,
            ManifestOperationType.CompetitionSubmit,
            compId,
            "Track",
            "hash123",
            null,
            privateKey);

        submitOp.Timestamp = DateTime.UtcNow.AddDays(2); // Past the deadline of +1 day

        var createOp = new ManifestOperation
        {
            OperationId = Guid.NewGuid().ToString(),
            OperationType = ManifestOperationType.CreateCompetition,
            TargetId = compId,
            TargetType = "Competition",
            Signature = "sig",
            Metadata = new Dictionary<string, string>
            {
                { "SubmissionDeadline", DateTime.UtcNow.AddDays(1).ToString("O") },
                { "VotingDeadline", DateTime.UtcNow.AddDays(2).ToString("O") },
                { "AdministratorUserId", "admin-1" }
            }
        };

        // Needs to re-sign because we altered the timestamp
        submitOp.Signature = CryptoService.SignData(ManifestManager.BuildSignablePayload(submitOp), privateKey);

        var added = _manager.MergeManifest(local, remote, publicKey, id => id == compId ? createOp : null);
        Assert.Equal(0, added);
        Assert.Empty(local.Operations);
    }

    [Fact]
    public void CompetitionCastVote_Invalid_BeforeSubmissionDeadline()
    {
        var (privateKey, publicKey) = CryptoService.GenerateKeyPair();
        var local = _manager.CreateManifest("user-1");
        var remote = _manager.CreateManifest("user-1");

        var compId = Guid.NewGuid().ToString();

        var voteOp = _manager.AppendSignedOperation(
            remote,
            ManifestOperationType.CompetitionCastVote,
            compId,
            "Competition",
            "hash123",
            null,
            privateKey);

        voteOp.Timestamp = DateTime.UtcNow; // Before submission deadline (AddDays(1))

        var createOp = new ManifestOperation
        {
            OperationId = Guid.NewGuid().ToString(),
            OperationType = ManifestOperationType.CreateCompetition,
            TargetId = compId,
            TargetType = "Competition",
            Signature = "sig",
            Metadata = new Dictionary<string, string>
            {
                { "SubmissionDeadline", DateTime.UtcNow.AddDays(1).ToString("O") },
                { "VotingDeadline", DateTime.UtcNow.AddDays(2).ToString("O") },
                { "AdministratorUserId", "admin-1" }
            }
        };

        voteOp.Signature = CryptoService.SignData(ManifestManager.BuildSignablePayload(voteOp), privateKey);

        var added = _manager.MergeManifest(local, remote, publicKey, id => id == compId ? createOp : null);
        Assert.Equal(0, added);
    }

    [Fact]
    public void CompetitionRevealResults_Invalid_NotAdmin()
    {
        var (privateKey, publicKey) = CryptoService.GenerateKeyPair();
        var local = _manager.CreateManifest("admin-2"); // Not admin-1
        var remote = _manager.CreateManifest("admin-2");

        var compId = Guid.NewGuid().ToString();

        var revealOp = _manager.AppendSignedOperation(
            remote,
            ManifestOperationType.CompetitionRevealResults,
            compId,
            "Competition",
            "hash123",
            null,
            privateKey);

        revealOp.Signature = CryptoService.SignData(ManifestManager.BuildSignablePayload(revealOp), privateKey);

        var createOp = new ManifestOperation
        {
            OperationId = Guid.NewGuid().ToString(),
            OperationType = ManifestOperationType.CreateCompetition,
            TargetId = compId,
            TargetType = "Competition",
            Signature = "sig",
            Metadata = new Dictionary<string, string>
            {
                { "SubmissionDeadline", DateTime.UtcNow.AddDays(-2).ToString("O") },
                { "VotingDeadline", DateTime.UtcNow.AddDays(-1).ToString("O") },
                { "AdministratorUserId", "admin-1" }
            }
        };

        var added = _manager.MergeManifest(local, remote, publicKey, id => id == compId ? createOp : null);
        Assert.Equal(0, added);
    }
}
