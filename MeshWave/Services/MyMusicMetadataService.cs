using System.IO;
using System.Text.Json;
using MeshWave.Models;

namespace MeshWave.Services
{
    public class MyMusicMetadataService
    {
        public MyMusicMetadata LoadForTrack(string filePath)
        {
            var metaPath = GetTrackMetaPath(filePath);
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
            var metaPath = GetTrackMetaPath(filePath);
            if (string.IsNullOrWhiteSpace(metaPath))
            {
                return;
            }

            var folder = Path.GetDirectoryName(metaPath);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                Directory.CreateDirectory(folder);
            }

            metadata.Version = metadata.Version <= 0 ? 1 : metadata.Version;

            var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(metaPath, json);
        }

        public MyMusicMetadata LoadForAlbum(string albumFolder)
        {
            var metaPath = GetAlbumMetaPath(albumFolder);
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

        public void SaveForAlbum(string albumFolder, MyMusicMetadata metadata)
        {
            var metaPath = GetAlbumMetaPath(albumFolder);
            if (string.IsNullOrWhiteSpace(metaPath))
            {
                return;
            }

            var folder = Path.GetDirectoryName(metaPath);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                Directory.CreateDirectory(folder);
            }

            metadata.Version = metadata.Version <= 0 ? 1 : metadata.Version;

            var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(metaPath, json);
        }

        private static string GetTrackMetaPath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return string.Empty;
            }

            var albumFolder = Path.GetDirectoryName(filePath) ?? string.Empty;
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            return Path.Combine(albumFolder, ".cache", $"{fileName}.mymusic.json");
        }

        private static string GetAlbumMetaPath(string albumFolder)
        {
            if (string.IsNullOrWhiteSpace(albumFolder))
            {
                return string.Empty;
            }

            return Path.Combine(albumFolder, ".cache", "album.mymusic.json");
        }
    }
}
