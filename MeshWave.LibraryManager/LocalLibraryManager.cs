using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MeshWave.Common.Core.Models;
using MeshWave.Common.Core.Storage;
using TagLib;
using File = System.IO.File;

namespace MeshWave.LibraryManager;

/// <summary>
/// LocalLibraryManager handles indexing and management of the user's local music library.
/// </summary>
public class LocalLibraryManager
{
    private static readonly string[] DefaultExtensionList = [".mp3", ".flac", ".wav", ".ogg", ".m4a"];
    private readonly string _basePath;
    private readonly HashSet<string> _supportedExtensions;
    private readonly List<Track> _tracks = new();
    private readonly List<Album> _albums = new();

    public LocalLibraryManager(string basePath, IEnumerable<string>? supportedExtensions = null)
    {
        _basePath = basePath;
        _supportedExtensions = NormalizeExtensions(supportedExtensions);
    }

    public static IReadOnlyList<string> SupportedExtensions => DefaultExtensionList;

    /// <summary>
    /// Indexes music files in the local library. Reads cached metadata when available.
    /// </summary>
    public void IndexLibrary()
    {
        _tracks.Clear();
        _albums.Clear();

        if (!Directory.Exists(_basePath)) return;

        var albumDict = new Dictionary<string, Album>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in EnumerateSupportedFiles(_basePath, _supportedExtensions))
            try
            {
                var metadata = TryReadCachedMetadata(file) ?? ExtractMetadata(file);
                WriteMetadataCache(file, metadata);
                EnsureCoverCached(file);

                var fileInfo = new FileInfo(file);
                var trackId = ComputeStableId(fileInfo.FullName);
                var albumId = ComputeStableId($"{metadata.Artist}|{metadata.Album}");
                var track = new Track
                {
                    TrackId = trackId,
                    AlbumId = albumId,
                    OwnerUserId = "local",
                    Title = metadata.Title,
                    Duration = TimeSpan.FromSeconds(metadata.DurationSeconds),
                    FileHash = ComputeStableId(fileInfo.FullName),
                    FilePath = fileInfo.FullName,
                    FileSize = fileInfo.Length,
                    CoverImageHash = null,
                    Description = metadata.Artist,
                    Signature = "local"
                };
                _tracks.Add(track);

                if (!albumDict.TryGetValue(albumId, out var album))
                {
                    album = new Album
                    {
                        AlbumId = albumId,
                        OwnerUserId = "local",
                        Title = metadata.Album,
                        CoverImageHash = null,
                        Description = null,
                        Signature = "local"
                    };
                    albumDict[albumId] = album;
                }
                album.TrackIds.Add(trackId);
            }
            catch
            {
                // skip unreadable files
            }

        _albums.AddRange(albumDict.Values);
    }

    public static void ImportMusicToOrganizedStructure(
        string sourceFolder,
        string myMusicBaseFolder,
        IEnumerable<string>? supportedExtensions = null,
        Action<ImportProgress>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(sourceFolder))
        {
            progressCallback?.Invoke(new ImportProgress(0, 0, 0, string.Empty, "Source folder does not exist."));
            return;
        }

        Directory.CreateDirectory(myMusicBaseFolder);
        var normalizedExtensions = NormalizeExtensions(supportedExtensions);
        var files = EnumerateSupportedFiles(sourceFolder, normalizedExtensions).ToList();

        var total = files.Count;
        var processed = 0;
        var imported = 0;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remainingBeforeCurrent = total - processed;
            progressCallback?.Invoke(new ImportProgress(total, imported, remainingBeforeCurrent, file, "Copying..."));

            try
            {
                if (TryImportSingleFile(file, myMusicBaseFolder, normalizedExtensions)) imported++;
            }
            catch
            {
                // skip files that cannot be read/imported
            }
            finally
            {
                processed++;
                progressCallback?.Invoke(new ImportProgress(total, imported, total - processed, file, "Processed"));
            }
        }

        progressCallback?.Invoke(new ImportProgress(total, imported, 0, string.Empty, "Import completed."));
    }

    public static bool ImportSingleFileToOrganizedStructure(
        string sourceFile,
        string myMusicBaseFolder,
        IEnumerable<string>? supportedExtensions = null)
    {
        if (string.IsNullOrWhiteSpace(sourceFile) || !File.Exists(sourceFile)) return false;

        Directory.CreateDirectory(myMusicBaseFolder);
        var normalizedExtensions = NormalizeExtensions(supportedExtensions);

        try
        {
            return TryImportSingleFile(sourceFile, myMusicBaseFolder, normalizedExtensions);
        }
        catch
        {
            return false;
        }
    }

    public static bool UpdateImportedTrackFile(
        string sourceFile,
        string existingTrackFilePath,
        bool preserveExistingMetadata = true)
    {
        if (string.IsNullOrWhiteSpace(sourceFile) || string.IsNullOrWhiteSpace(existingTrackFilePath)) return false;

        if (!File.Exists(sourceFile) || !File.Exists(existingTrackFilePath)) return false;

        try
        {
            var backupPath = existingTrackFilePath + ".bak";
            if (File.Exists(backupPath)) File.Delete(backupPath);

            File.Copy(existingTrackFilePath, backupPath, overwrite: true);
            File.Copy(sourceFile, existingTrackFilePath, overwrite: true);

            var metadata = ExtractMetadata(existingTrackFilePath);
            WriteMetadataCache(existingTrackFilePath, metadata);
            EnsureCoverCached(existingTrackFilePath);

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryImportSingleFile(string sourceFile, string myMusicBaseFolder, HashSet<string> normalizedExtensions)
    {
        var extension = Path.GetExtension(sourceFile);
        if (!normalizedExtensions.Contains(extension)) return false;

        var metadata = ExtractMetadata(sourceFile);
        var albumFolder = Path.Combine(myMusicBaseFolder, metadata.Artist, metadata.Album);
        var cacheFolder = Path.Combine(albumFolder, ".cache");
        var commentsFolder = Path.Combine(albumFolder, ".comments");

        Directory.CreateDirectory(albumFolder);
        Directory.CreateDirectory(cacheFolder);
        Directory.CreateDirectory(commentsFolder);

        var destinationFile = Path.Combine(albumFolder, $"{metadata.Title}{extension}");
        var imported = false;
        if (!File.Exists(destinationFile))
        {
            File.Copy(sourceFile, destinationFile, overwrite: false);
            imported = true;
        }

        WriteMetadataCache(destinationFile, metadata);
        EnsureCoverCached(destinationFile);
        UpdateMappingFiles(myMusicBaseFolder, destinationFile, metadata);
        return imported;
    }

    private static void UpdateMappingFiles(string myMusicBaseFolder, string trackFilePath, CachedTrackMetadata metadata)
    {
        try
        {
            var artistFolder = Path.Combine(myMusicBaseFolder, metadata.Artist);
            var albumFolder = Path.Combine(artistFolder, metadata.Album);
            var trackId = ComputeStableId(trackFilePath);
            var albumId = ComputeStableId($"{metadata.Artist}|{metadata.Album}");

            // Artist level: map album IDs to names
            var artistMapPath = Path.Combine(artistFolder, "album_map.json");
            var artistMap = LoadMap(artistMapPath);
            artistMap[albumId] = metadata.Album;
            SaveMap(artistMapPath, artistMap);

            // Album level: map track IDs to names and versions
            var albumMapPath = Path.Combine(albumFolder, "track_map.json");
            var albumMap = LoadMap(albumMapPath);
            albumMap[trackId] = $"{metadata.Title}|1"; // Default version 1
            SaveMap(albumMapPath, albumMap);
        }
        catch { }
    }

    private static Dictionary<string, string> LoadMap(string path)
    {
        if (!File.Exists(path)) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch { return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
    }

    private static void SaveMap(string path, Dictionary<string, string> map)
    {
        var json = JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    public IEnumerable<Track> GetAllTracks()
    {
        return _tracks;
    }

    public IEnumerable<Album> GetAllAlbums()
    {
        return _albums;
    }


    public string GetTrackCoverPath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return string.Empty;

        var cachePath = GetCoverCachePath(filePath);
        if (!File.Exists(cachePath)) EnsureCoverCached(filePath);

        return File.Exists(cachePath) ? cachePath : string.Empty;
    }

    public float[] GetTrackWaveform(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return [];

        var waveformPath = GetWaveformCachePathForTrack(filePath);
        if (!File.Exists(waveformPath)) return [];

        try
        {
            var data = File.ReadAllBytes(waveformPath);
            var waveform = WaveformBinaryFormat.Decode(data);
            return waveform ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static IEnumerable<string> EnumerateSupportedFiles(string folder, HashSet<string> supportedExtensions)
    {
        return Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
            .Where(f => supportedExtensions.Contains(Path.GetExtension(f)));
    }

    private static HashSet<string> NormalizeExtensions(IEnumerable<string>? extensions)
    {
        var source = extensions?.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim()) ?? SupportedExtensions;

        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ext in source)
        {
            var value = ext.StartsWith('.') ? ext.ToLowerInvariant() : $".{ext.ToLowerInvariant()}";
            normalized.Add(value);
        }

        return normalized.Count > 0
            ? normalized
            : new HashSet<string>(SupportedExtensions, StringComparer.OrdinalIgnoreCase);
    }

    private static CachedTrackMetadata ExtractMetadata(string filePath)
    {
        using var tagFile = TagLib.File.Create(filePath);
        var title = NormalizeFolderName(tagFile.Tag.Title ?? Path.GetFileNameWithoutExtension(filePath));
        var artist = NormalizeFolderName(tagFile.Tag.FirstPerformer ?? "Unknown Artist");
        var album = NormalizeFolderName(tagFile.Tag.Album ?? "_singles_");
        var duration = tagFile.Properties.Duration;

        return new CachedTrackMetadata
        {
            Title = title,
            Artist = artist,
            Album = album,
            DurationSeconds = duration.TotalSeconds,
            SourceLastWriteUtc = File.GetLastWriteTimeUtc(filePath)
        };
    }

    private static CachedTrackMetadata? TryReadCachedMetadata(string filePath)
    {
        var cachePath = GetCacheMetadataPath(filePath);
        if (!File.Exists(cachePath)) return null;

        var cacheWrite = File.GetLastWriteTimeUtc(cachePath);
        var sourceWrite = File.GetLastWriteTimeUtc(filePath);
        if (cacheWrite < sourceWrite) return null;

        try
        {
            var json = File.ReadAllText(cachePath);
            var metadata = JsonSerializer.Deserialize<CachedTrackMetadata>(json);
            return metadata;
        }
        catch
        {
            return null;
        }
    }

    private static void WriteMetadataCache(string filePath, CachedTrackMetadata metadata)
    {
        var cachePath = GetCacheMetadataPath(filePath);
        var cacheDir = Path.GetDirectoryName(cachePath);
        if (!string.IsNullOrWhiteSpace(cacheDir)) Directory.CreateDirectory(cacheDir);

        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(cachePath, json);
    }

    private static string GetCacheMetadataPath(string filePath)
    {
        var fileName = NormalizeFolderName(Path.GetFileNameWithoutExtension(filePath));
        var albumFolder = Path.GetDirectoryName(filePath) ?? string.Empty;
        return Path.Combine(albumFolder, ".cache", $"{fileName}.meta.json");
    }

    private static string GetCoverCachePath(string filePath)
    {
        var fileName = NormalizeFolderName(Path.GetFileNameWithoutExtension(filePath));
        var albumFolder = Path.GetDirectoryName(filePath) ?? string.Empty;
        return Path.Combine(albumFolder, ".cache", $"{fileName}.cover.jpg");
    }

    public static string GetWaveformCachePathForTrack(string filePath)
    {
        var fileName = NormalizeFolderName(Path.GetFileNameWithoutExtension(filePath));
        var albumFolder = Path.GetDirectoryName(filePath) ?? string.Empty;
        return Path.Combine(albumFolder, ".cache", $"{fileName}.waveform");
    }

    private static void EnsureCoverCached(string filePath)
    {
        try
        {
            var coverPath = GetCoverCachePath(filePath);
            if (File.Exists(coverPath)) return;

            using var tagFile = TagLib.File.Create(filePath);
            var picture = tagFile.Tag.Pictures?
                .FirstOrDefault(p => p.Type == PictureType.FrontCover)
                ?? tagFile.Tag.Pictures?.FirstOrDefault();

            // TODO: consider resizing large cover images to save space and memory when loading.
            if (picture == null || picture.Data == null || picture.Data.Count == 0) return;

            var coverDir = Path.GetDirectoryName(coverPath);
            if (!string.IsNullOrWhiteSpace(coverDir)) Directory.CreateDirectory(coverDir);

            var cacheFileName = Path.GetFileNameWithoutExtension(coverPath) + ".jpg";
            var normalizedCoverPath = Path.Combine(coverDir ?? string.Empty, cacheFileName);
            File.WriteAllBytes(normalizedCoverPath, picture.Data.Data);
        }
        catch
        {
            // ignore cover extraction errors
        }
    }


    private static string NormalizeFolderName(string value)
    {
        var cleaned = string.Concat(value.Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "Unknown" : cleaned;
    }

    private static string ComputeStableId(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }

    private sealed class CachedTrackMetadata
    {
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Album { get; set; } = "_singles_";
        public double DurationSeconds { get; set; }
        public DateTime SourceLastWriteUtc { get; set; }
    }
}

public sealed record ImportProgress(int TotalFiles, int ImportedFiles, int RemainingFiles, string CurrentFile, string StatusMessage);
