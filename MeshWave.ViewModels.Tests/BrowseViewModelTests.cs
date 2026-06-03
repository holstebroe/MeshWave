using MeshWave.Common.Core.Models;
using MeshWave.Services;
using MeshWave.Synchronizer;
using MeshWave.ViewModels;
using Moq;
using Xunit;

namespace MeshWave.ViewModels.Tests;

public class BrowseViewModelTests
{
    [Fact]
    public void Refresh_CountsPublicTracks_FromLatestTrackState()
    {
        var artistId = "artist-1";
        var manifest = new Manifest
        {
            UserId = artistId,
            Operations =
            [
                new ManifestOperation
                {
                    OperationId = "1", OperationType = ManifestOperationType.Profile,
                    TargetId = artistId, TargetType = "User", Signature = "sig", SequenceNumber = 0,
                    Metadata = new Dictionary<string, string>
                    {
                        ["displayName"] = "John",
                        ["isArtist"] = "True"
                    }
                },
                new ManifestOperation
                {
                    OperationId = "2", OperationType = ManifestOperationType.Create,
                    TargetId = "track-a", TargetType = "Track", ContentHash = "hash-a",
                    Signature = "sig", SequenceNumber = 1,
                    Metadata = new Dictionary<string, string> { ["title"] = "A", ["album"] = "DeskPlastic" }
                },
                new ManifestOperation
                {
                    OperationId = "3", OperationType = ManifestOperationType.Update,
                    TargetId = "track-a", TargetType = "Track", ContentHash = "hash-a2",
                    Signature = "sig", SequenceNumber = 2,
                    Metadata = new Dictionary<string, string> { ["title"] = "A (Remaster)", ["album"] = "DeskPlastic" }
                },
                new ManifestOperation
                {
                    OperationId = "4", OperationType = ManifestOperationType.Create,
                    TargetId = "track-b", TargetType = "Track", ContentHash = "hash-b",
                    Signature = "sig", SequenceNumber = 3,
                    Metadata = new Dictionary<string, string> { ["title"] = "B", ["album"] = "DeskPlastic" }
                },
                new ManifestOperation
                {
                    OperationId = "5", OperationType = ManifestOperationType.Delete,
                    TargetId = "track-b", TargetType = "Track", Signature = "sig", SequenceNumber = 4,
                    Metadata = []
                }
            ],
            Version = 1,
            LastUpdated = DateTime.UtcNow
        };

        var sync = new Mock<ISyncBrowseClient>();
        sync.SetupGet(s => s.IsRunning).Returns(true);
        sync.SetupGet(s => s.PeerManifests).Returns(new List<Manifest> { manifest });
        sync.SetupGet(s => s.LocalManifest).Returns((Manifest?)null);
        sync.Setup(s => s.GetPeers()).Returns(Array.Empty<PeerInfo>());

        var vm = new BrowseViewModel(sync.Object, new DownloadQueueService());

        Assert.Single(vm.Artists);
        Assert.Equal(1, vm.Artists[0].TrackCount);
        Assert.Single(vm.Tracks);
        Assert.Equal("track-a", vm.Tracks[0].TrackId);
    }

    [Fact]
    public void NavigateToArtist_FiltersTracksToThatArtist()
    {
        var john = BuildArtistManifest("john", "John", "track-j1", "hash-j1");
        var jane = BuildArtistManifest("jane", "Jane", "track-n1", "hash-n1");

        var sync = new Mock<ISyncBrowseClient>();
        sync.SetupGet(s => s.IsRunning).Returns(true);
        sync.SetupGet(s => s.PeerManifests).Returns(new List<Manifest> { john, jane });
        sync.SetupGet(s => s.LocalManifest).Returns((Manifest?)null);
        sync.Setup(s => s.GetPeers()).Returns(Array.Empty<PeerInfo>());

        var vm = new BrowseViewModel(sync.Object, new DownloadQueueService());

        Assert.Equal(2, vm.Tracks.Count);

        vm.NavigateToArtist("jane");

        Assert.Single(vm.Tracks);
        Assert.Equal("jane", vm.Tracks[0].ArtistUserId);
    }

    [Fact]
    public void Refresh_DiscoversPlaylists_FromManifest()
    {
        var artistId = "artist-1";
        var trackIds = new List<string> { "track-1", "track-2" };
        var trackIdsJson = System.Text.Json.JsonSerializer.Serialize(trackIds);

        var manifest = new Manifest
        {
            UserId = artistId,
            Operations =
            [
                new ManifestOperation
                {
                    OperationId = "p1", OperationType = ManifestOperationType.Create,
                    TargetId = "playlist-1", TargetType = "Playlist", Signature = "sig", SequenceNumber = 0,
                    Metadata = new Dictionary<string, string>
                    {
                        ["name"] = "My Favorites",
                        ["description"] = "Best tracks",
                        ["trackIds"] = trackIdsJson,
                        ["releasedAt"] = DateTime.UtcNow.ToString("O")
                    }
                }
            ]
        };

        var sync = new Mock<ISyncBrowseClient>();
        sync.SetupGet(s => s.IsRunning).Returns(true);
        sync.SetupGet(s => s.PeerManifests).Returns(new List<Manifest> { manifest });
        sync.Setup(s => s.GetPeers()).Returns(Array.Empty<PeerInfo>());

        var vm = new BrowseViewModel(sync.Object, new DownloadQueueService());

        Assert.Single(vm.Playlists);
        var pl = vm.Playlists[0];
        Assert.Equal("My Favorites", pl.Name);
        Assert.Equal("Best tracks", pl.Description);
        Assert.Equal(2, pl.TrackCount);
        Assert.Equal(trackIds, pl.TrackIds);
    }

    [Fact]
    public void DownloadPlaylistCommand_EnqueuesTracksInPlaylist()
    {
        var artistId = "artist-1";
        var trackIds = new List<string> { "track-1" };
        var trackIdsJson = System.Text.Json.JsonSerializer.Serialize(trackIds);

        var manifest = new Manifest
        {
            UserId = artistId,
            Operations =
            [
                new ManifestOperation
                {
                    OperationId = "t1", OperationType = ManifestOperationType.Create,
                    TargetId = "track-1", TargetType = "Track", ContentHash = "hash-1", Signature = "sig",
                    Metadata = new Dictionary<string, string> { ["title"] = "Track 1", ["album"] = "Album A" }
                },
                new ManifestOperation
                {
                    OperationId = "p1", OperationType = ManifestOperationType.Create,
                    TargetId = "playlist-1", TargetType = "Playlist", Signature = "sig",
                    Metadata = new Dictionary<string, string>
                    {
                        ["name"] = "Playlist",
                        ["trackIds"] = trackIdsJson
                    }
                }
            ]
        };

        var sync = new Mock<ISyncBrowseClient>();
        sync.SetupGet(s => s.IsRunning).Returns(true);
        sync.SetupGet(s => s.PeerManifests).Returns(new List<Manifest> { manifest });
        sync.Setup(s => s.GetPeers()).Returns(Array.Empty<PeerInfo>());

        var downloadQueue = new DownloadQueueService();
        var vm = new BrowseViewModel(sync.Object, downloadQueue);

        Assert.Empty(downloadQueue.AllItems);

        vm.DownloadPlaylistCommand.Execute(vm.Playlists[0]);

        Assert.Single(downloadQueue.AllItems);
        Assert.Equal("hash-1", downloadQueue.AllItems[0].ContentHash);
    }

    [Fact]
    public void Refresh_DeduplicatesAlbums_FromManifest()
    {
        var artistId = "artist-1";
        var manifest = new Manifest
        {
            UserId = artistId,
            Operations =
            [
                new ManifestOperation
                {
                    OperationId = "1", OperationType = ManifestOperationType.Create,
                    TargetId = "album-1", TargetType = "Album", Signature = "sig", SequenceNumber = 1,
                    Metadata = new Dictionary<string, string> { ["name"] = "Original Album" }
                },
                new ManifestOperation
                {
                    OperationId = "2", OperationType = ManifestOperationType.Update,
                    TargetId = "album-1", TargetType = "Album", Signature = "sig", SequenceNumber = 2,
                    Metadata = new Dictionary<string, string> { ["name"] = "Updated Album" }
                }
            ]
        };

        var sync = new Mock<ISyncBrowseClient>();
        sync.SetupGet(s => s.IsRunning).Returns(true);
        sync.SetupGet(s => s.PeerManifests).Returns(new List<Manifest> { manifest });
        sync.Setup(s => s.GetPeers()).Returns(Array.Empty<PeerInfo>());

        var vm = new BrowseViewModel(sync.Object, new DownloadQueueService());

        Assert.Single(vm.Albums);
        Assert.Equal("Updated Album", vm.Albums[0].Name);
    }

    [Fact]
    public void Refresh_DeduplicatesPlaylists_FromManifest()
    {
        var artistId = "artist-1";
        var manifest = new Manifest
        {
            UserId = artistId,
            Operations =
            [
                new ManifestOperation
                {
                    OperationId = "p1", OperationType = ManifestOperationType.Create,
                    TargetId = "playlist-1", TargetType = "Playlist", Signature = "sig", SequenceNumber = 1,
                    Metadata = new Dictionary<string, string> { ["name"] = "Playlist V1" }
                },
                new ManifestOperation
                {
                    OperationId = "p2", OperationType = ManifestOperationType.Update,
                    TargetId = "playlist-1", TargetType = "Playlist", Signature = "sig", SequenceNumber = 2,
                    Metadata = new Dictionary<string, string> { ["name"] = "Playlist V2" }
                }
            ]
        };

        var sync = new Mock<ISyncBrowseClient>();
        sync.SetupGet(s => s.IsRunning).Returns(true);
        sync.SetupGet(s => s.PeerManifests).Returns(new List<Manifest> { manifest });
        sync.Setup(s => s.GetPeers()).Returns(Array.Empty<PeerInfo>());

        var vm = new BrowseViewModel(sync.Object, new DownloadQueueService());

        Assert.Single(vm.Playlists);
        Assert.Equal("Playlist V2", vm.Playlists[0].Name);
    }

    [Fact]
    public void Refresh_HandlesSnapshots()
    {
        var artistId = "artist-1";
        var manifest = new Manifest
        {
            UserId = artistId,
            Snapshot = new ManifestSnapshot
            {
                LastSequenceNumber = 10,
                Signature = "sig",
                EntityStates =
                [
                    new SnapshotStateEntry
                    {
                        TargetId = "album-snap", TargetType = "Album",
                        Metadata = new Dictionary<string, string> { ["name"] = "Snapshot Album" }
                    }
                ]
            },
            Operations =
            [
                new ManifestOperation
                {
                    OperationId = "11", OperationType = ManifestOperationType.Update,
                    TargetId = "album-snap", TargetType = "Album", Signature = "sig", SequenceNumber = 11,
                    Metadata = new Dictionary<string, string> { ["name"] = "Snapshot Album Updated" }
                }
            ]
        };

        var sync = new Mock<ISyncBrowseClient>();
        sync.SetupGet(s => s.IsRunning).Returns(true);
        sync.SetupGet(s => s.PeerManifests).Returns(new List<Manifest> { manifest });
        sync.Setup(s => s.GetPeers()).Returns(Array.Empty<PeerInfo>());

        var vm = new BrowseViewModel(sync.Object, new DownloadQueueService());

        Assert.Single(vm.Albums);
        Assert.Equal("Snapshot Album Updated", vm.Albums[0].Name);
    }

    private static Manifest BuildArtistManifest(string userId, string displayName, string trackId, string hash)
    {
        return new Manifest
        {
            UserId = userId,
            Operations =
            [
                new ManifestOperation
                {
                    OperationId = $"p-{userId}", OperationType = ManifestOperationType.Profile,
                    TargetId = userId, TargetType = "User", Signature = "sig", SequenceNumber = 0,
                    Metadata = new Dictionary<string, string>
                    {
                        ["displayName"] = displayName,
                        ["isArtist"] = "True"
                    }
                },
                new ManifestOperation
                {
                    OperationId = $"t-{userId}", OperationType = ManifestOperationType.Create,
                    TargetId = trackId, TargetType = "Track", ContentHash = hash,
                    Signature = "sig", SequenceNumber = 1,
                    Metadata = new Dictionary<string, string> { ["title"] = trackId, ["album"] = "AlbumX" }
                }
            ],
            Version = 1,
            LastUpdated = DateTime.UtcNow
        };
    }
}
