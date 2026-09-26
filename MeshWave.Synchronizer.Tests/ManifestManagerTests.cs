using MeshWave.Common.Core;
using MeshWave.Common.Core.Models;
using Xunit;

namespace MeshWave.Synchronizer.Tests;

public class ManifestManagerTests
{
    private readonly ManifestManager _manifestManager = new();

    [Fact]
    public void CreateManifest_ReturnsValidManifest()
    {
        // Act
        var manifest = _manifestManager.CreateManifest("user-1");

        // Assert
        Assert.NotNull(manifest);
        Assert.Equal("user-1", manifest.UserId);
        Assert.Empty(manifest.Operations);
        Assert.Equal(1, manifest.Version);
    }

    [Fact]
    public void AppendOperation_AddsOperationToManifest()
    {
        // Arrange
        var manifest = _manifestManager.CreateManifest("user-1");
        var operation = new ManifestOperation
        {
            OperationId = "op-1",
            OperationType = ManifestOperationType.Create,
            TargetId = "track-1",
            TargetType = "Track",
            ContentHash = "hash123",
            Signature = "sig123"
        };

        // Act
        _manifestManager.AppendOperation(manifest, operation);

        // Assert
        Assert.Single(manifest.Operations);
        Assert.Equal(0, manifest.Operations[0].SequenceNumber);
        Assert.Equal(2, manifest.Version);
    }

    [Fact]
    public void AppendOperation_IncrementSequenceNumber()
    {
        // Arrange
        var manifest = _manifestManager.CreateManifest("user-1");
        var op1 = new ManifestOperation
        {
            OperationId = "op-1",
            OperationType = ManifestOperationType.Create,
            TargetId = "track-1",
            TargetType = "Track",
            ContentHash = "hash123",
            Signature = "sig123"
        };
        var op2 = new ManifestOperation
        {
            OperationId = "op-2",
            OperationType = ManifestOperationType.Create,
            TargetId = "track-2",
            TargetType = "Track",
            ContentHash = "hash456",
            Signature = "sig456"
        };

        // Act
        _manifestManager.AppendOperation(manifest, op1);
        _manifestManager.AppendOperation(manifest, op2);

        // Assert
        Assert.Equal(2, manifest.Operations.Count);
        Assert.Equal(0, manifest.Operations[0].SequenceNumber);
        Assert.Equal(1, manifest.Operations[1].SequenceNumber);
    }

    [Fact]
    public void VerifyManifest_ReturnsTrue()
    {
        // Arrange
        var manifest = _manifestManager.CreateManifest("user-1");
        var publicKey = "-----BEGIN PUBLIC KEY-----\ntest\n-----END PUBLIC KEY-----";

        // Act
        var isValid = _manifestManager.VerifyManifest(manifest, publicKey);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void MergeManifest_ThrowsInvalidDataException_WhenOperationsExceedLimit()
    {
        var local = _manifestManager.CreateManifest("user-4");
        var remote = _manifestManager.CreateManifest("user-4");

        for (int i = 0; i <= SecurityLimits.MaxManifestOperations; i++)
        {
            remote.Operations.Add(new ManifestOperation
            {
                OperationId = $"op-{i}",
                OperationType = ManifestOperationType.Create,
                TargetId = "target",
                TargetType = "Track",
                Signature = "sig",
                SequenceNumber = i
            });
        }

        Assert.Throws<System.IO.InvalidDataException>(() => _manifestManager.MergeManifest(local, remote, "mocked-key"));
    }

    [Fact]
    public void MergeManifest_LogsDetailedRejection_WhenSignatureFails()
    {
        // Arrange
        var keys1 = MeshWave.Common.Core.Crypto.CryptoService.GenerateSigningKeyPair();

        // Uses its own LogFactory (rather than the global NLog.LogManager) so the captured logs cannot be
        // clobbered by other tests reconfiguring the shared static logging config in parallel.
        var memoryTarget = new NLog.Targets.MemoryTarget { Name = "mem" };
        var config = new NLog.Config.LoggingConfiguration();
        config.AddRuleForAllLevels(memoryTarget);
        var logFactory = new NLog.LogFactory { Configuration = config };
        var isolatedManager = new ManifestManager(logFactory.GetCurrentClassLogger());

        var local = isolatedManager.CreateManifest("user-1");
        var remote = isolatedManager.CreateManifest("user-1");

        isolatedManager.AppendSignedOperation(remote, ManifestOperationType.Create, "t1", "Track", null, null, keys1.privateKey);

        // Tamper with the signature
        remote.Operations[0].Signature = "invalid-signature";

        // Act & Assert
        Assert.Throws<System.IO.InvalidDataException>(() =>
            isolatedManager.MergeManifest(local, remote, keys1.publicKey));

        // Verify logs
        var logs = memoryTarget.Logs;
        Assert.Contains(logs, l => l.Contains("failed verification") && l.Contains("user-1"));
        Assert.Contains(logs, l => l.Contains("Invalid operation signature") && l.Contains("sequence 0") && l.Contains("stream Content") && l.Contains("user-1"));
    }

}
