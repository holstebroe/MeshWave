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
            AudioVersions = new System.Collections.Generic.Dictionary<MeshWave.Common.Core.Models.AudioQuality, MeshWave.Common.Core.Models.AudioVersionInfo> { { MeshWave.Common.Core.Models.AudioQuality.Original, new MeshWave.Common.Core.Models.AudioVersionInfo { FileHash = "abc123", FileSize = 1024000 } } },
            FilePath = @"C:\\Music\\Test Song.mp3",
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
            AudioVersions = new System.Collections.Generic.Dictionary<MeshWave.Common.Core.Models.AudioQuality, MeshWave.Common.Core.Models.AudioVersionInfo> { { MeshWave.Common.Core.Models.AudioQuality.Original, new MeshWave.Common.Core.Models.AudioVersionInfo { FileHash = "abc123", FileSize = 1024000 } } },
            FilePath = @"C:\\Music\\Test Song.mp3",
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
                    AudioVersions = new System.Collections.Generic.Dictionary<MeshWave.Common.Core.Models.AudioQuality, MeshWave.Common.Core.Models.AudioVersionInfo> { { MeshWave.Common.Core.Models.AudioQuality.Original, new MeshWave.Common.Core.Models.AudioVersionInfo { FileHash = "hash123", FileSize = 0 } } },
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

    [Fact]
    public void SerializeGroupManifest_ProducesValidJson()
    {
        // Arrange
        var manifest = new GroupManifest
        {
            GroupId = "group-1",
            Name = "Jazz Collective",
            FounderUserId = "user-1",
            Operations = new List<GroupOperation>
            {
                new()
                {
                    SequenceNumber = 1,
                    UserId = "user-1",
                    OperationType = GroupOperationType.Found,
                    Signature = "sig123",
                    Metadata = new Dictionary<string, string> { { "key", "value" } }
                }
            }
        };

        // Act
        var json = JsonSerializer.SerializeGroupManifest(manifest);

        // Assert
        Assert.NotNull(json);
        Assert.Contains("group-1", json);
        Assert.Contains("Jazz Collective", json);
        Assert.Contains("Found", json);
        Assert.Contains("sig123", json);
    }

    [Fact]
    public void DeserializeGroupManifest_ReconstructsObject()
    {
        // Arrange
        var manifest = new GroupManifest
        {
            GroupId = "group-1",
            Name = "Jazz Collective",
            FounderUserId = "user-1",
            Operations = new List<GroupOperation>
            {
                new()
                {
                    SequenceNumber = 1,
                    UserId = "user-1",
                    OperationType = GroupOperationType.Found,
                    Signature = "sig123"
                }
            }
        };
        var json = JsonSerializer.SerializeGroupManifest(manifest);

        // Act
        var deserialized = JsonSerializer.DeserializeGroupManifest(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(manifest.GroupId, deserialized.GroupId);
        Assert.Single(deserialized.Operations);
        Assert.Equal(GroupOperationType.Found, deserialized.Operations[0].OperationType);
    }

    [Fact]
    public void SerializeChannel_ProducesValidJson()
    {
        // Arrange
        var channel = new Channel
        {
            ChannelId = "channel-1",
            Name = "General",
            CreatedBy = "user-1"
        };

        // Act
        var json = JsonSerializer.SerializeChannel(channel);

        // Assert
        Assert.NotNull(json);
        Assert.Contains("channel-1", json);
        Assert.Contains("General", json);
    }

    [Fact]
    public void DeserializeCompetition_ReconstructsObject()
    {
        // Arrange
        var competition = new Competition
        {
            CompetitionId = Guid.NewGuid(),
            GroupId = "group-1",
            Title = "Summer Remix Contest",
            Description = "Remix the summer hit!",
            SubmissionDeadline = DateTime.UtcNow.AddDays(7),
            VotingDeadline = DateTime.UtcNow.AddDays(14),
            AdministratorUserId = "admin-1",
            IsResultsPublicBeforeReveal = false
        };
        var json = JsonSerializer.SerializeCompetition(competition);

        // Act
        var deserialized = JsonSerializer.DeserializeCompetition(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(competition.CompetitionId, deserialized.CompetitionId);
        Assert.Equal(competition.Title, deserialized.Title);
        Assert.Equal(competition.AdministratorUserId, deserialized.AdministratorUserId);
    }

    [Fact]
    public void DeserializeCompetitionSubmission_ReconstructsObject()
    {
        // Arrange
        var submission = new CompetitionSubmission
        {
            TrackId = "track-123",
            UserId = "user-456",
            SubmissionTimestamp = DateTime.UtcNow
        };
        var json = JsonSerializer.SerializeCompetitionSubmission(submission);

        // Act
        var deserialized = JsonSerializer.DeserializeCompetitionSubmission(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(submission.TrackId, deserialized.TrackId);
        Assert.Equal(submission.UserId, deserialized.UserId);
    }

    [Fact]
    public void DeserializeCompetitionVote_ReconstructsObject()
    {
        // Arrange
        var vote = new CompetitionVote
        {
            VoterUserId = "voter-1",
            EncryptedVotePayload = "BASE64_ENCRYPTED_DATA",
            VoteTimestamp = DateTime.UtcNow
        };
        var json = JsonSerializer.SerializeCompetitionVote(vote);

        // Act
        var deserialized = JsonSerializer.DeserializeCompetitionVote(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(vote.VoterUserId, deserialized.VoterUserId);
        Assert.Equal(vote.EncryptedVotePayload, deserialized.EncryptedVotePayload);
    }

    [Fact]
    public void DeserializeCompetitionResult_ReconstructsObject()
    {
        // Arrange
        var result = new CompetitionResult
        {
            OrderedTrackIds = new List<string> { "track-1", "track-3", "track-2" },
            TallyTimestamp = DateTime.UtcNow,
            AdministratorSignature = "SIG_123"
        };
        var json = JsonSerializer.SerializeCompetitionResult(result);

        // Act
        var deserialized = JsonSerializer.DeserializeCompetitionResult(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(result.OrderedTrackIds.Count, deserialized.OrderedTrackIds.Count);
        Assert.Equal(result.OrderedTrackIds[1], deserialized.OrderedTrackIds[1]);
        Assert.Equal(result.AdministratorSignature, deserialized.AdministratorSignature);
    }
}
