using MeshWave.Common.Core.Models;
using MeshWave.Common.Core.Serialization;
using Xunit;

namespace MeshWave.Common.Core.Tests;

public class JsonSerializerTests
{
    [Fact]
    public void SerializeUser_ProducesValidJson()
    {
        // Arrange
        var user = new User
        {
            UserId = "test-user-1",
            DisplayName = "Test User",
            PublicKeyPem = "-----BEGIN PUBLIC KEY-----\ntest\n-----END PUBLIC KEY-----"
        };

        // Act
        var json = JsonSerializer.SerializeUser(user);

        // Assert
        Assert.NotNull(json);
        Assert.Contains("test-user-1", json);
        Assert.Contains("Test User", json);
    }

    [Fact]
    public void DeserializeUser_ReconstructsObject()
    {
        // Arrange
        var user = new User
        {
            UserId = "test-user-1",
            DisplayName = "Test User",
            PublicKeyPem = "-----BEGIN PUBLIC KEY-----\ntest\n-----END PUBLIC KEY-----",
            Description = "Test description"
        };
        var json = JsonSerializer.SerializeUser(user);

        // Act
        var deserialized = JsonSerializer.DeserializeUser(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(user.UserId, deserialized.UserId);
        Assert.Equal(user.DisplayName, deserialized.DisplayName);
        Assert.Equal(user.Description, deserialized.Description);
    }

    [Fact]
    public void SerializeTrack_ProducesValidJson()
    {
        // Arrange
        var track = new Track
        {
            TrackId = "track-1",
            OwnerUserId = "user-1",
            Title = "Test Song",
            Duration = TimeSpan.FromSeconds(180),
            FileHash = "abc123",
            FileSize = 1024000,
            Signature = "sig123"
        };

        // Act
        var json = JsonSerializer.SerializeTrack(track);

        // Assert
        Assert.NotNull(json);
        Assert.Contains("track-1", json);
        Assert.Contains("Test Song", json);
    }

    [Fact]
    public void DeserializeTrack_ReconstructsObject()
    {
        // Arrange
        var track = new Track
        {
            TrackId = "track-1",
            OwnerUserId = "user-1",
            Title = "Test Song",
            Duration = TimeSpan.FromSeconds(180),
            FileHash = "abc123",
            FileSize = 1024000,
            Signature = "sig123"
        };
        var json = JsonSerializer.SerializeTrack(track);

        // Act
        var deserialized = JsonSerializer.DeserializeTrack(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(track.TrackId, deserialized.TrackId);
        Assert.Equal(track.Title, deserialized.Title);
        Assert.Equal(track.Duration, deserialized.Duration);
    }

    [Fact]
    public void SerializeAlbum_ProducesValidJson()
    {
        // Arrange
        var album = new Album
        {
            AlbumId = "album-1",
            OwnerUserId = "user-1",
            Title = "Test Album",
            TrackIds = ["track-1", "track-2"],
            Signature = "sig123"
        };

        // Act
        var json = JsonSerializer.SerializeAlbum(album);

        // Assert
        Assert.NotNull(json);
        Assert.Contains("Test Album", json);
        Assert.Contains("track-1", json);
    }

    [Fact]
    public void SerializeComment_ProducesValidJson()
    {
        // Arrange
        var comment = new Comment
        {
            CommentId = "comment-1",
            AuthorUserId = "user-1",
            TargetType = CommentTargetType.Track,
            TargetId = "track-1",
            Text = "Great song!",
            Signature = "sig123"
        };

        // Act
        var json = JsonSerializer.SerializeComment(comment);

        // Assert
        Assert.NotNull(json);
        Assert.Contains("Great song!", json);
        Assert.Contains("Track", json);
    }

    [Fact]
    public void DeserializeComment_ReconstructsObject()
    {
        // Arrange
        var comment = new Comment
        {
            CommentId = "comment-1",
            AuthorUserId = "user-1",
            TargetType = CommentTargetType.Track,
            TargetId = "track-1",
            TimestampInTrackSeconds = 125.5,
            Text = "Great bridge!",
            Signature = "sig123"
        };
        var json = JsonSerializer.SerializeComment(comment);

        // Act
        var deserialized = JsonSerializer.DeserializeComment(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(comment.CommentId, deserialized.CommentId);
        Assert.Equal(comment.Text, deserialized.Text);
        Assert.Equal(comment.TargetType, deserialized.TargetType);
        Assert.Equal(comment.TimestampInTrackSeconds, deserialized.TimestampInTrackSeconds);
    }

    [Fact]
    public void SerializeManifest_ProducesValidJson()
    {
        // Arrange
        var manifest = new Manifest
        {
            UserId = "user-1",
            Operations =
            [
                new ManifestOperation
                {
                    OperationId = "op-1",
                    OperationType = ManifestOperationType.Create,
                    TargetId = "track-1",
                    TargetType = "Track",
                    ContentHash = "hash123",
                    SequenceNumber = 0,
                    Signature = "sig123"
                }
            ]
        };

        // Act
        var json = JsonSerializer.SerializeManifest(manifest);

        // Assert
        Assert.NotNull(json);
        Assert.Contains("user-1", json);
        Assert.Contains("Create", json);
    }

    [Fact]
    public void SerializeCommunity_ProducesValidJson()
    {
        // Arrange
        var community = new Community
        {
            CommunityId = "community-1",
            Name = "Jazz Musicians",
            MemberUserIds = ["user-1", "user-2"]
        };

        // Act
        var json = JsonSerializer.SerializeCommunity(community);

        // Assert
        Assert.NotNull(json);
        Assert.Contains("Jazz Musicians", json);
        Assert.Contains("user-1", json);
    }
}
