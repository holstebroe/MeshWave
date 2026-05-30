using System;
using System.IO;
using System.Text.Json;
using MeshWave.Models;

namespace MeshWave.Services
{
    public class UserProfileService
    {
        private static readonly string AppDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MeshWave");

        private static readonly string ProfileFilePath = Path.Combine(AppDataFolder, "profile.json");

        public UserProfile LoadProfile()
        {
            try
            {
                if (File.Exists(ProfileFilePath))
                {
                    var json = File.ReadAllText(ProfileFilePath);
                    var profile = JsonSerializer.Deserialize<UserProfile>(json);
                    if (profile != null)
                    {
                        return profile;
                    }
                }
            }
            catch
            {
                // fall through to defaults
            }

            return new UserProfile();
        }

        public void SaveProfile(UserProfile profile)
        {
            if (!Directory.Exists(AppDataFolder))
            {
                Directory.CreateDirectory(AppDataFolder);
            }

            var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ProfileFilePath, json);
        }
    }
}
