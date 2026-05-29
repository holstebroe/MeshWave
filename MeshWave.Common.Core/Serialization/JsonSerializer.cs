using System.Text.Json;
using System.Text.Json.Serialization;
using MeshWave.Common.Core.Models;

namespace MeshWave.Common.Core.Serialization;

/// <summary>
/// Provides JSON serialization/deserialization for domain models.
/// </summary>
public class JsonSerializer
{
    private static readonly System.Text.Json.JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
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
}
