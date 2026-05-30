using System.IO;
using System.Text.Json;
using MeshWave.Models;

namespace MeshWave.Services
{
    public class MyMusicMetadataService
    {
        public MyMusicMetadata LoadForTrack(string filePath)
        {
            var fallback = ExtractFromFileTags(filePath);
            var metaPath = GetTrackMetaPath(filePath);
            if (string.IsNullOrWhiteSpace(metaPath) || !File.Exists(metaPath))
            {
                return fallback;
            }

            try
            {
                var json = File.ReadAllText(metaPath);
                var cached = JsonSerializer.Deserialize<MyMusicMetadata>(json) ?? new MyMusicMetadata();
                return MergeWithFallback(cached, fallback);
            }
            catch
            {
                return fallback;
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

        private static MyMusicMetadata ExtractFromFileTags(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return new MyMusicMetadata();
            }

            try
            {
                using var tagFile = TagLib.File.Create(filePath);
                return new MyMusicMetadata
                {
                    Title = tagFile.Tag.Title ?? Path.GetFileNameWithoutExtension(filePath),
                    Artist = tagFile.Tag.FirstPerformer ?? string.Empty,
                    Album = tagFile.Tag.Album ?? string.Empty,
                    Genre = tagFile.Tag.FirstGenre ?? string.Empty,
                    Year = (int)tagFile.Tag.Year,
                    TrackNumber = (int)tagFile.Tag.Track
                };
            }
            catch
            {
                return new MyMusicMetadata
                {
                    Title = Path.GetFileNameWithoutExtension(filePath)
                };
            }
        }

        private static MyMusicMetadata MergeWithFallback(MyMusicMetadata cached, MyMusicMetadata fallback)
        {
            return new MyMusicMetadata
            {
                Title = string.IsNullOrWhiteSpace(cached.Title) ? fallback.Title : cached.Title,
                Artist = string.IsNullOrWhiteSpace(cached.Artist) ? fallback.Artist : cached.Artist,
                Album = string.IsNullOrWhiteSpace(cached.Album) ? fallback.Album : cached.Album,
                Description = cached.Description,
                Genre = string.IsNullOrWhiteSpace(cached.Genre) ? fallback.Genre : cached.Genre,
                Year = cached.Year > 0 ? cached.Year : fallback.Year,
                TrackNumber = cached.TrackNumber > 0 ? cached.TrackNumber : fallback.TrackNumber,
                IsReleased = cached.IsReleased,
                Version = cached.Version <= 0 ? 1 : cached.Version
            };
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
