using MeshWave.Common.Core.Models;
using MeshWave.Common.Core.P2P;
using MeshWave.Common.Core.Serialization;
using Xunit;

namespace MeshWave.Common.Core.Tests;

public class ManifestSerializerTests
{
    [Fact]
    public void SerializeAndDeserialize_CreateChannel_RoundTripsSuccessfully()
    {
        // Arrange
        var request = new ManifestRequest
        {
            Type = ManifestRequestType.PushManifest,
            StreamType = ManifestStreamType.Social,
            Manifest = new Manifest
            {
                UserId = "user-1",
                Operations = new List<ManifestOperation>
                {
                    new()
                    {
                        OperationId = "op-1",
                        OperationType = ManifestOperationType.CreateChannel,
                        TargetId = "channel-1",
                        TargetType = "GroupChannel",
                        SequenceNumber = 1,
                        Signature = "sig-1"
                    }
                }
            }
        };

        // Act
        var bytes = ManifestSerializer.SerializeRequest(request);
        var deserialized = ManifestSerializer.DeserializeRequest(bytes);

        // Assert
        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Manifest);
        Assert.Single(deserialized.Manifest.Operations);
        var op = deserialized.Manifest.Operations[0];
        Assert.Equal(ManifestOperationType.CreateChannel, op.OperationType);
        Assert.Equal("channel-1", op.TargetId);
    }

    [Fact]
    public void SerializeAndDeserialize_PostMessage_RoundTripsSuccessfully()
    {
        // Arrange
        var request = new ManifestRequest
        {
            Type = ManifestRequestType.PushManifest,
            StreamType = ManifestStreamType.Social,
            Manifest = new Manifest
            {
                UserId = "user-1",
                Operations = new List<ManifestOperation>
                {
                    new()
                    {
                        OperationId = "op-2",
                        OperationType = ManifestOperationType.PostMessage,
                        TargetId = "post-1",
                        TargetType = "PostMessage",
                        SequenceNumber = 2,
                        Signature = "sig-2"
                    }
                }
            }
        };

        // Act
        var bytes = ManifestSerializer.SerializeRequest(request);
        var deserialized = ManifestSerializer.DeserializeRequest(bytes);

        // Assert
        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Manifest);
        Assert.Single(deserialized.Manifest.Operations);
        var op = deserialized.Manifest.Operations[0];
        Assert.Equal(ManifestOperationType.PostMessage, op.OperationType);
        Assert.Equal("post-1", op.TargetId);
    }
}
