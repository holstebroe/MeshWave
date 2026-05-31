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

        var vm = new BrowseViewModel(sync.Object, new DownloadQueueService());

        Assert.Equal(2, vm.Tracks.Count);

        vm.NavigateToArtist("jane");

        Assert.Single(vm.Tracks);
        Assert.Equal("jane", vm.Tracks[0].ArtistUserId);
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
