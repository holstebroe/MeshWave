using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MeshWave.Common.Core.Models;
using TagLib;

namespace MeshWave.LibraryManager;

/// <summary>
/// LocalLibraryManager handles indexing and management of the user's local music library.
/// </summary>
public class LocalLibraryManager
{
    private readonly string _basePath;
    private readonly List<Track> _tracks = new();
    private readonly List<Album> _albums = new();

    public LocalLibraryManager(string basePath)
    {
        _basePath = basePath;
    }

    /// <summary>
    /// Indexes music files in the local library.
    /// </summary>
    public void IndexLibrary()
    {
        _tracks.Clear();
        _albums.Clear();
        var albumDict = new Dictionary<string, Album>(StringComparer.OrdinalIgnoreCase);
        var supported = new[] { ".mp3", ".flac", ".wav", ".ogg", ".m4a" };
        foreach (var file in Directory.EnumerateFiles(_basePath, "*.*", SearchOption.AllDirectories)
            .Where(f => supported.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)))
        {
            try
            {
                var tagFile = TagLib.File.Create(file);
                var title = tagFile.Tag.Title ?? Path.GetFileNameWithoutExtension(file);
                var albumTitle = tagFile.Tag.Album ?? "Unknown Album";
                var artist = tagFile.Tag.FirstPerformer ?? "Unknown Artist";
                var duration = tagFile.Properties.Duration;
                var fileInfo = new FileInfo(file);
                var trackId = fileInfo.FullName.GetHashCode().ToString();
                var albumId = albumTitle.GetHashCode().ToString();
                var track = new Track
                {
                    TrackId = trackId,
                    AlbumId = albumId,
                    OwnerUserId = "local", // TODO: set real user
                    Title = title,
                    Duration = duration,
                    FileHash = fileInfo.FullName, // Temporarily store file path here
                    FileSize = fileInfo.Length,
                    CoverImageHash = null, // TODO: extract cover
                    Description = artist,
                    Signature = "local"
                };
                _tracks.Add(track);
                if (!albumDict.TryGetValue(albumId, out var album))
                {
                    album = new Album
                    {
                        AlbumId = albumId,
                        OwnerUserId = "local",
                        Title = albumTitle,
                        CoverImageHash = null,
                        Description = null,
                        Signature = "local"
                    };
                    albumDict[albumId] = album;
                }
                album.TrackIds.Add(trackId);
            }
            catch { /* skip unreadable files */ }
        }
        _albums.AddRange(albumDict.Values);
    }

    public IEnumerable<Track> GetAllTracks() => _tracks;
    public IEnumerable<Album> GetAllAlbums() => _albums;
}
