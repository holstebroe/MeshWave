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
            MyMusicMetadata? cached = null;
            if (!string.IsNullOrWhiteSpace(metaPath) && File.Exists(metaPath))
            {
                try
                {
                    var json = File.ReadAllText(metaPath);
                    cached = JsonSerializer.Deserialize<MyMusicMetadata>(json);
                }
                catch { }
            }

            var fallback = ExtractFromFileTags(filePath);
            if (cached == null)
            {
                return fallback;
            }

            return MergeWithFallback(cached, fallback);
        }

        public void IncrementPlayCount(string filePath)
        {
            var meta = LoadForTrack(filePath);
            meta.PlayCount++;
            SaveForTrack(filePath, meta);
        }

        public void SaveCoverArt(string filePath, string sourceImagePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath) || !File.Exists(sourceImagePath))
            {
                return;
            }

            var albumFolder = Path.GetDirectoryName(filePath) ?? string.Empty;
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var coverPath = Path.Combine(albumFolder, ".cache", $"{fileName}.cover.jpg");

            try
            {
                var cacheDir = Path.GetDirectoryName(coverPath);
                if (!string.IsNullOrWhiteSpace(cacheDir))
                {
                    Directory.CreateDirectory(cacheDir);
                }
                File.Copy(sourceImagePath, coverPath, overwrite: true);
            }
            catch { }
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
            MyMusicMetadata? cached = null;
            if (!string.IsNullOrWhiteSpace(metaPath) && File.Exists(metaPath))
            {
                try
                {
                    var json = File.ReadAllText(metaPath);
                    cached = JsonSerializer.Deserialize<MyMusicMetadata>(json);
                }
                catch { }
            }

            var fallback = ExtractAlbumFallback(albumFolder);
            if (cached == null)
            {
                return fallback;
            }

            return MergeWithFallback(cached, fallback);
        }

        private MyMusicMetadata ExtractAlbumFallback(string albumFolder)
        {
            if (string.IsNullOrWhiteSpace(albumFolder) || !Directory.Exists(albumFolder))
            {
                return new MyMusicMetadata();
            }

            var supportedExtensions = new HashSet<string>(MeshWave.LibraryManager.LocalLibraryManager.SupportedExtensions, StringComparer.OrdinalIgnoreCase);
            var firstTrack = Directory.EnumerateFiles(albumFolder, "*.*", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(f => supportedExtensions.Contains(Path.GetExtension(f)));

            if (firstTrack == null)
            {
                return new MyMusicMetadata { Title = Path.GetFileName(albumFolder) ?? "Unknown Album" };
            }

            var meta = ExtractFromFileTags(firstTrack);
            return new MyMusicMetadata
            {
                Title = !string.IsNullOrWhiteSpace(meta.Album) ? meta.Album : Path.GetFileName(albumFolder) ?? "Unknown Album",
                Artist = meta.Artist,
                Genre = meta.Genre,
                Year = meta.Year
            };
        }

        public string GetCoverArtPath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return string.Empty;
            }

            var albumFolder = Path.GetDirectoryName(filePath) ?? string.Empty;
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var coverPath = Path.Combine(albumFolder, ".cache", $"{fileName}.cover.jpg");

            if (File.Exists(coverPath))
            {
                return coverPath;
            }

            try
            {
                using var tagFile = TagLib.File.Create(filePath);
                var picture = tagFile.Tag.Pictures?.FirstOrDefault(p => p.Type == TagLib.PictureType.FrontCover)
                           ?? tagFile.Tag.Pictures?.FirstOrDefault();

                if (picture != null && picture.Data != null && picture.Data.Count > 0)
                {
                    var cacheDir = Path.GetDirectoryName(coverPath);
                    if (!string.IsNullOrWhiteSpace(cacheDir))
                    {
                        Directory.CreateDirectory(cacheDir);
                    }
                    File.WriteAllBytes(coverPath, picture.Data.Data);
                    return coverPath;
                }
            }
            catch { }

            return string.Empty;
        }

        public string GetAlbumCoverArtPath(string albumFolder)
        {
            if (string.IsNullOrWhiteSpace(albumFolder) || !Directory.Exists(albumFolder))
            {
                return string.Empty;
            }

            var supportedExtensions = new HashSet<string>(MeshWave.LibraryManager.LocalLibraryManager.SupportedExtensions, StringComparer.OrdinalIgnoreCase);
            var firstTrack = Directory.EnumerateFiles(albumFolder, "*.*", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(f => supportedExtensions.Contains(Path.GetExtension(f)));

            return firstTrack != null ? GetCoverArtPath(firstTrack) : string.Empty;
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
                Version = cached.Version <= 0 ? 1 : cached.Version,
                PlayCount = cached.PlayCount
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
