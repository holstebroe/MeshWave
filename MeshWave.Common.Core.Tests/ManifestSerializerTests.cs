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

    [Fact]
    public void SerializeAndDeserialize_ChunkRequestFields_RoundTrip()
    {
        var request = new ManifestRequest
        {
            Type = ManifestRequestType.RequestContent,
            ContentHash = "hash",
            ChunkOffset = 524288,
            ChunkLength = 1024
        };

        var deserialized = ManifestSerializer.DeserializeRequest(ManifestSerializer.SerializeRequest(request));

        Assert.Equal(524288, deserialized.ChunkOffset);
        Assert.Equal(1024, deserialized.ChunkLength);
    }

    [Fact]
    public void SerializeAndDeserialize_RequestWithoutChunk_HasNullChunkFields()
    {
        var request = new ManifestRequest { Type = ManifestRequestType.RequestContent, ContentHash = "hash" };

        var deserialized = ManifestSerializer.DeserializeRequest(ManifestSerializer.SerializeRequest(request));

        Assert.Null(deserialized.ChunkOffset);
        Assert.Null(deserialized.ChunkLength);
    }

    [Fact]
    public void SerializeAndDeserialize_TotalContentLength_RoundTrips()
    {
        var response = new ManifestResponse { Acknowledged = true, ContentLength = 0, TotalContentLength = 3_000_000 };

        var deserialized = ManifestSerializer.DeserializeResponse(ManifestSerializer.SerializeResponse(response));

        Assert.Equal(3_000_000, deserialized.TotalContentLength);
        Assert.Null(ManifestSerializer.DeserializeResponse(ManifestSerializer.SerializeResponse(new ManifestResponse())).TotalContentLength);
    }

    [Fact]
    public void SerializeAndDeserialize_AnnounceRequest_RoundTrips()
    {
        var request = new ManifestRequest
        {
            Type = ManifestRequestType.Announce,
            AnnouncingPeer = new PeerInfo { UserId = "user-1", DisplayName = "User", Address = "", Port = 0, PublicKeyPem = "key" }
        };

        var deserialized = ManifestSerializer.DeserializeRequest(ManifestSerializer.SerializeRequest(request));

        Assert.Equal(ManifestRequestType.Announce, deserialized.Type);
        Assert.Equal("user-1", deserialized.AnnouncingPeer!.UserId);
        Assert.Equal(0, deserialized.AnnouncingPeer.Port);
    }

    [Fact]
    public void SerializeAndDeserialize_ReplicationFields_RoundTrip()
    {
        var signature = Convert.ToBase64String(Enumerable.Range(0, 512).Select(i => (byte)i).ToArray());
        var request = new ManifestRequest
        {
            Type = ManifestRequestType.PushManifest,
            StreamType = ManifestStreamType.Interaction,
            TargetUserId = "author-1",
            Manifest = new Manifest
            {
                UserId = "author-1",
                StreamType = ManifestStreamType.Interaction,
                AuthorPublicKey = "-----BEGIN RSA PUBLIC KEY-----",
                HasMore = true,
                Snapshot = new ManifestSnapshot { LastSequenceNumber = 4, HeadHash = "HEAD", Signature = signature },
                Operations =
                [
                    new ManifestOperation
                    {
                        OperationId = "op-5", OperationType = ManifestOperationType.Like, TargetId = "track-1", TargetType = "Track",
                        SequenceNumber = 5, PrevHash = "HEAD", Signature = signature
                    }
                ]
            }
        };

        var bytes = ManifestSerializer.SerializeRequest(request);
        var deserialized = ManifestSerializer.DeserializeRequest(bytes);

        Assert.Equal("author-1", deserialized.TargetUserId);
        var manifest = deserialized.Manifest!;
        Assert.Equal("-----BEGIN RSA PUBLIC KEY-----", manifest.AuthorPublicKey);
        Assert.True(manifest.HasMore);
        Assert.Equal("HEAD", manifest.Snapshot!.HeadHash);
        Assert.Equal(signature, manifest.Snapshot.Signature);
        Assert.Equal("HEAD", manifest.Operations[0].PrevHash);
        Assert.Equal(signature, manifest.Operations[0].Signature);

        // Signatures travel as raw bytes, not base64 text.
        Assert.True(ManifestSerializer.GetEncodedSize(manifest.Operations[0]) < 512 + 100);
    }

    [Fact]
    public void SerializeAndDeserialize_Heads_RoundTrip()
    {
        var response = new ManifestResponse
        {
            Acknowledged = true,
            Heads = [new StreamHead("author-1", ManifestStreamType.Social, 41, "ABC"), new StreamHead("author-2", ManifestStreamType.Content, -1, "")]
        };

        var deserialized = ManifestSerializer.DeserializeResponse(ManifestSerializer.SerializeResponse(response));

        Assert.Equal(response.Heads, deserialized.Heads);
        Assert.Null(ManifestSerializer.DeserializeRequest(ManifestSerializer.SerializeRequest(new ManifestRequest { Type = ManifestRequestType.GetHeads })).TargetUserId);
    }
}
