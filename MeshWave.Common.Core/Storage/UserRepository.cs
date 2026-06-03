using System.Collections.Concurrent;
using System.Text.Json;
using MeshWave.Common.Core.Models;

namespace MeshWave.Common.Core.Storage;

/// <summary>
/// Maintains a community-synchronized repository of user profiles.
/// Profiles are updated when broadcast through the mesh.
/// </summary>
public class UserRepository
{
    private readonly string _baseDataFolder;
    private readonly string _cacheDirectory;
    private readonly string _profilesDirectory;
    private readonly ConcurrentDictionary<string, UserProfileData> _profiles = new(StringComparer.OrdinalIgnoreCase);

    public UserRepository(string baseDataFolder)
    {
        _baseDataFolder = baseDataFolder;
        _cacheDirectory = Path.Combine(baseDataFolder, "UserCache", "Images");
        _profilesDirectory = Path.Combine(baseDataFolder, "UserCache", "Profiles");
        Directory.CreateDirectory(_cacheDirectory);
        Directory.CreateDirectory(_profilesDirectory);
        LoadProfiles();
    }

    public void RegisterLocalUser(string userId, string displayName, string? iconPath = null)
    {
        var profile = _profiles.GetOrAdd(userId, id => new UserProfileData { UserId = id });
        profile.DisplayName = displayName;

        if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
        {
            try
            {
                var targetPath = Path.Combine(_cacheDirectory, $"{userId}.png");
                if (iconPath != targetPath)
                {
                    File.Copy(iconPath, targetPath, true);
                }
            }
            catch { }
        }

        SaveProfile(profile);
    }

    public void UpdateProfile(string userId, Dictionary<string, string> metadata)
    {
        var profile = _profiles.GetOrAdd(userId, id => new UserProfileData { UserId = id });

        if (metadata.TryGetValue("displayName", out var displayName))
            profile.DisplayName = displayName;

        if (metadata.TryGetValue("isArtist", out var isArtistStr) && bool.TryParse(isArtistStr, out var isArtist))
            profile.IsArtist = isArtist;

        if (metadata.TryGetValue("bio", out var bio))
            profile.Bio = bio;

        if (metadata.TryGetValue("website", out var website))
            profile.Website = website;

        if (metadata.TryGetValue("publicKeyPem", out var publicKey))
            profile.PublicKeyPem = publicKey;

        SaveProfile(profile);
    }

    public UserProfileData? GetProfile(string userId)
    {
        return _profiles.TryGetValue(userId, out var profile) ? profile : null;
    }

    public string GetDisplayName(string userId)
    {
        return GetProfile(userId)?.DisplayName ?? userId;
    }

    public string BaseDataFolder => _baseDataFolder;

    public string? GetUserIconPath(string userId)
    {
        var path = Path.Combine(_cacheDirectory, $"{userId}.png");
        return File.Exists(path) ? path : null;
    }

    public void SaveUserIcon(string userId, byte[] iconBytes)
    {
        var path = Path.Combine(_cacheDirectory, $"{userId}.png");
        File.WriteAllBytes(path, iconBytes);
    }

    private void LoadProfiles()
    {
        if (!Directory.Exists(_profilesDirectory)) return;

        foreach (var file in Directory.EnumerateFiles(_profilesDirectory, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var profile = JsonSerializer.Deserialize<UserProfileData>(json);
                if (profile != null && !string.IsNullOrWhiteSpace(profile.UserId))
                    _profiles[profile.UserId] = profile;
            }
            catch { }
        }
    }

    private void SaveProfile(UserProfileData profile)
    {
        try
        {
            var json = JsonSerializer.Serialize(profile);
            File.WriteAllText(Path.Combine(_profilesDirectory, $"{profile.UserId}.json"), json);
        }
        catch { }
    }
}

public class UserProfileData
{
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsArtist { get; set; }
    public string Bio { get; set; } = string.Empty;
    public string Website { get; set; } = string.Empty;
    public string PublicKeyPem { get; set; } = string.Empty;
}
