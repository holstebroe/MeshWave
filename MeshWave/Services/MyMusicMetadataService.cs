using System.IO;
using System.Text.Json;
using MeshWave.Models;

namespace MeshWave.Services
{
    public class MyMusicMetadataService
    {
        public MyMusicMetadata LoadForTrack(string filePath)
        {
            var metaPath = GetMetaPath(filePath);
            if (string.IsNullOrWhiteSpace(metaPath) || !File.Exists(metaPath))
            {
                return new MyMusicMetadata();
            }

            try
            {
                var json = File.ReadAllText(metaPath);
                return JsonSerializer.Deserialize<MyMusicMetadata>(json) ?? new MyMusicMetadata();
            }
            catch
            {
                return new MyMusicMetadata();
            }
        }

        public void SaveForTrack(string filePath, MyMusicMetadata metadata)
        {
            var metaPath = GetMetaPath(filePath);
            if (string.IsNullOrWhiteSpace(metaPath))
            {
                return;
            }

            var folder = Path.GetDirectoryName(metaPath);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                Directory.CreateDirectory(folder);
            }

            var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(metaPath, json);
        }

        private static string GetMetaPath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return string.Empty;
            }

            var albumFolder = Path.GetDirectoryName(filePath) ?? string.Empty;
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            return Path.Combine(albumFolder, ".cache", $"{fileName}.mymusic.json");
        }
    }
}
