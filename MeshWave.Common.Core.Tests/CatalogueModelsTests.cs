using MeshWave.Common.Core.Models;
using Xunit;

namespace MeshWave.Common.Core.Tests;

public class CatalogueModelsTests
{
    [Fact]
    public void Playlist_ShouldStoreProperties()
    {
        var playlist = new Playlist
        {
            PlaylistId = "p1",
            OwnerUserId = "u1",
            Title = "My Playlist",
            Description = "A cool playlist",
            TrackIds = ["u1:t1", "u2:t2"],
            Signature = "signature"
        };

        Assert.Equal("p1", playlist.PlaylistId);
        Assert.Equal("u1", playlist.OwnerUserId);
        Assert.Equal("My Playlist", playlist.Title);
        Assert.Equal("A cool playlist", playlist.Description);
        Assert.Equal(2, playlist.TrackIds.Count);
        Assert.Contains("u1:t1", playlist.TrackIds);
    }

    [Fact]
    public void CatalogueEntry_ShouldStoreProperties()
    {
        var entry = new CatalogueEntry
        {
            EntryId = "e1",
            Type = CatalogueEntryType.Track,
            OwnerUserId = "u1",
            Title = "Song A",
            ArtistName = "Artist X",
            AlbumName = "Album Y",
            Duration = TimeSpan.FromMinutes(3),
            AudioVersions = new System.Collections.Generic.Dictionary<MeshWave.Common.Core.Models.AudioQuality, MeshWave.Common.Core.Models.AudioVersionInfo> { { MeshWave.Common.Core.Models.AudioQuality.Original, new MeshWave.Common.Core.Models.AudioVersionInfo { FileHash = "hash123", FileSize = 0 } } },
            ReleaseDate = new DateTime(2023, 1, 1),
            Genre = "Rock",
            SequenceNumber = 5,
            Timestamp = DateTime.UtcNow
        };

        Assert.Equal("e1", entry.EntryId);
        Assert.Equal(CatalogueEntryType.Track, entry.Type);
        Assert.Equal("u1", entry.OwnerUserId);
        Assert.Equal("Song A", entry.Title);
        Assert.Equal("Artist X", entry.ArtistName);
        Assert.Equal("Album Y", entry.AlbumName);
        Assert.Equal(TimeSpan.FromMinutes(3), entry.Duration);
        Assert.Equal("hash123", entry.AudioVersions.Values.FirstOrDefault()?.FileHash);
        Assert.Equal(2023, entry.ReleaseDate.Value.Year);
        Assert.Equal("Rock", entry.Genre);
        Assert.Equal(5, entry.SequenceNumber);
    }

    [Fact]
    public void PeerAvailability_ShouldStoreProperties()
    {
        var availability = new PeerAvailability
        {
            ContentHash = "hash123",
            PeerUserIds = ["u1", "u2"]
        };

        Assert.Equal("hash123", availability.ContentHash);
        Assert.Equal(2, availability.PeerUserIds.Count);
        Assert.Contains("u1", availability.PeerUserIds);
        Assert.Contains("u2", availability.PeerUserIds);
    }
}
