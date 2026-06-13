using System.Text.Json;
using System.Text.Json.Serialization;
using MeshWave.Common.Core.Models;

namespace MeshWave.Common.Core.Serialization;

/// <summary>
/// Provides JSON serialization/deserialization for domain models.
/// </summary>
public class JsonSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string SerializeUser(User user)
    {
        return System.Text.Json.JsonSerializer.Serialize(user, Options);
    }

    public static User? DeserializeUser(string json)
    {
        return System.Text.Json.JsonSerializer.Deserialize<User>(json, Options);
    }

    public static string SerializeTrack(Track track)
    {
        return System.Text.Json.JsonSerializer.Serialize(track, Options);
    }

    public static Track? DeserializeTrack(string json)
    {
        return System.Text.Json.JsonSerializer.Deserialize<Track>(json, Options);
    }

    public static string SerializeAlbum(Album album)
    {
        return System.Text.Json.JsonSerializer.Serialize(album, Options);
    }

    public static Album? DeserializeAlbum(string json)
    {
        return System.Text.Json.JsonSerializer.Deserialize<Album>(json, Options);
    }

    public static string SerializeComment(Comment comment)
    {
        return System.Text.Json.JsonSerializer.Serialize(comment, Options);
    }

    public static Comment? DeserializeComment(string json)
    {
        return System.Text.Json.JsonSerializer.Deserialize<Comment>(json, Options);
    }

    public static string SerializeManifest(Manifest manifest)
    {
        return System.Text.Json.JsonSerializer.Serialize(manifest, Options);
    }

    public static Manifest? DeserializeManifest(string json)
    {
        if (json.Length > SecurityLimits.MaxMessageBytes)
            throw new System.IO.InvalidDataException($"Rejected message: length {json.Length} exceeds limit.");
        return System.Text.Json.JsonSerializer.Deserialize<Manifest>(json, Options);
    }

    public static string SerializeCommunity(Community community)
    {
        return System.Text.Json.JsonSerializer.Serialize(community, Options);
    }

    public static Community? DeserializeCommunity(string json)
    {
        return System.Text.Json.JsonSerializer.Deserialize<Community>(json, Options);
    }

    public static string SerializeGroupManifest(GroupManifest manifest)
    {
        return System.Text.Json.JsonSerializer.Serialize(manifest, Options);
    }

    public static GroupManifest? DeserializeGroupManifest(string json)
    {
        return System.Text.Json.JsonSerializer.Deserialize<GroupManifest>(json, Options);
    }

    public static string SerializeGroupChannel(GroupChannel channel)
    {
        return System.Text.Json.JsonSerializer.Serialize(channel, Options);
    }

    public static GroupChannel? DeserializeGroupChannel(string json)
    {
        return System.Text.Json.JsonSerializer.Deserialize<GroupChannel>(json, Options);
    }

    public static string SerializePostMessage(PostMessage message)
    {
        return System.Text.Json.JsonSerializer.Serialize(message, Options);
    }

    public static PostMessage? DeserializePostMessage(string json)
    {
        return System.Text.Json.JsonSerializer.Deserialize<PostMessage>(json, Options);
    }

    public static string SerializeCompetition(Competition competition)
    {
        return System.Text.Json.JsonSerializer.Serialize(competition, Options);
    }

    public static Competition? DeserializeCompetition(string json)
    {
        return System.Text.Json.JsonSerializer.Deserialize<Competition>(json, Options);
    }

    public static string SerializeCompetitionSubmission(CompetitionSubmission submission)
    {
        return System.Text.Json.JsonSerializer.Serialize(submission, Options);
    }

    public static CompetitionSubmission? DeserializeCompetitionSubmission(string json)
    {
        return System.Text.Json.JsonSerializer.Deserialize<CompetitionSubmission>(json, Options);
    }

    public static string SerializeCompetitionVote(CompetitionVote vote)
    {
        return System.Text.Json.JsonSerializer.Serialize(vote, Options);
    }

    public static CompetitionVote? DeserializeCompetitionVote(string json)
    {
        return System.Text.Json.JsonSerializer.Deserialize<CompetitionVote>(json, Options);
    }

    public static string SerializeCompetitionResult(CompetitionResult result)
    {
        return System.Text.Json.JsonSerializer.Serialize(result, Options);
    }

    public static CompetitionResult? DeserializeCompetitionResult(string json)
    {
        return System.Text.Json.JsonSerializer.Deserialize<CompetitionResult>(json, Options);
    }
}
