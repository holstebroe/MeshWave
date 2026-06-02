using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MeshWave.Common.Core.Models;
using Xunit;

namespace MeshWave.Common.Core.Tests;

public class CatalogueServiceTests
{
    [Fact]
    public async Task IngestAsync_ShouldIndexNewEntries()
    {
        var service = new CatalogueService();
        var manifest = new Manifest
        {
            UserId = "user1",
            Operations = new List<ManifestOperation>
            {
                new() {
                    OperationId = "op1",
                    OperationType = ManifestOperationType.Create,
                    TargetId = "track1",
                    TargetType = "Track",
                    ContentHash = "hash1",
                    SequenceNumber = 0,
                    Signature = "sig1",
                    Metadata = new Dictionary<string, string> { { "title", "Song A" }, { "artist", "Artist X" } }
                }
            }
        };

        await service.IngestAsync(manifest);

        var entry = await service.GetEntryAsync("track1");
        Assert.NotNull(entry);
        Assert.Equal("Song A", entry.Title);
        Assert.Equal("Artist X", entry.ArtistName);
        Assert.Equal("hash1", entry.ContentHash);

        var peers = await service.GetPeersForContentAsync("hash1");
        Assert.Contains("user1", peers);
    }

    [Fact]
    public async Task IngestAsync_ShouldEnforceStalenessRule()
    {
        var service = new CatalogueService();
        var manifestV1 = new Manifest
        {
            UserId = "user1",
            Operations = new List<ManifestOperation>
            {
                new() {
                    OperationId = "op1",
                    OperationType = ManifestOperationType.Create,
                    TargetId = "track1",
                    TargetType = "Track",
                    SequenceNumber = 1,
                    Signature = "sig1",
                    Metadata = new Dictionary<string, string> { { "title", "Song A v1" } }
                }
            }
        };

        var manifestV2 = new Manifest
        {
            UserId = "user1",
            Operations = new List<ManifestOperation>
            {
                new() {
                    OperationId = "op2",
                    OperationType = ManifestOperationType.Update,
                    TargetId = "track1",
                    TargetType = "Track",
                    SequenceNumber = 2,
                    Signature = "sig2",
                    Metadata = new Dictionary<string, string> { { "title", "Song A v2" } }
                }
            }
        };

        await service.IngestAsync(manifestV2);
        await service.IngestAsync(manifestV1); // Older op should be ignored

        var entry = await service.GetEntryAsync("track1");
        Assert.NotNull(entry);
        Assert.Equal("Song A v2", entry.Title);
    }

    [Fact]
    public async Task IngestAsync_ShouldHandleDelete()
    {
        var service = new CatalogueService();
        var manifest = new Manifest
        {
            UserId = "user1",
            Operations = new List<ManifestOperation>
            {
                new() {
                    OperationId = "op1",
                    OperationType = ManifestOperationType.Create,
                    TargetId = "track1",
                    TargetType = "Track",
                    SequenceNumber = 1,
                    Signature = "sig1",
                    Metadata = new Dictionary<string, string> { { "title", "Song A" } }
                },
                new() {
                    OperationId = "op2",
                    OperationType = ManifestOperationType.Delete,
                    TargetId = "track1",
                    TargetType = "Track",
                    SequenceNumber = 2,
                    Signature = "sig2"
                }
            }
        };

        await service.IngestAsync(manifest);

        var entry = await service.GetEntryAsync("track1");
        Assert.Null(entry);
    }

    [Fact]
    public async Task SearchAsync_ShouldFindMatches()
    {
        var service = new CatalogueService();
        var manifest = new Manifest
        {
            UserId = "user1",
            Operations = new List<ManifestOperation>
            {
                new() {
                    OperationId = "op1",
                    OperationType = ManifestOperationType.Create,
                    TargetId = "track1",
                    TargetType = "Track",
                    SequenceNumber = 1,
                    Signature = "sig1",
                    Metadata = new Dictionary<string, string> { { "title", "Greatest Song" }, { "artist", "Rock Star" } }
                },
                new() {
                    OperationId = "op2",
                    OperationType = ManifestOperationType.Create,
                    TargetId = "track2",
                    TargetType = "Track",
                    SequenceNumber = 2,
                    Signature = "sig2",
                    Metadata = new Dictionary<string, string> { { "title", "A Boring Track" }, { "artist", "Ambient Producer" } }
                }
            }
        };

        await service.IngestAsync(manifest);

        var results = await service.SearchAsync("Greatest");
        Assert.Single(results);
        Assert.Equal("track1", results.First().EntryId);

        results = await service.SearchAsync("Rock");
        Assert.Single(results);
        Assert.Equal("track1", results.First().EntryId);

        results = await service.SearchAsync("Song");
        Assert.Single(results);

        results = await service.SearchAsync("Producer Boring");
        Assert.Single(results);
        Assert.Equal("track2", results.First().EntryId);
    }

    [Fact]
    public async Task IngestAsync_ShouldHandleSnapshots()
    {
        var service = new CatalogueService();
        var manifest = new Manifest
        {
            UserId = "user1",
            Snapshot = new ManifestSnapshot
            {
                LastSequenceNumber = 10,
                Timestamp = DateTime.UtcNow,
                Signature = "snapsig",
                EntityStates = new List<SnapshotStateEntry>
                {
                    new() {
                        TargetId = "album1",
                        TargetType = "Album",
                        Metadata = new Dictionary<string, string> { { "name", "Mega Album" } }
                    }
                }
            },
            Operations = new List<ManifestOperation>
            {
                new() {
                    OperationId = "op11",
                    OperationType = ManifestOperationType.Update,
                    TargetId = "album1",
                    TargetType = "Album",
                    SequenceNumber = 11,
                    Signature = "sig11",
                    Metadata = new Dictionary<string, string> { { "name", "Mega Album Extended" } }
                }
            }
        };

        await service.IngestAsync(manifest);

        var entry = await service.GetEntryAsync("album1");
        Assert.NotNull(entry);
        Assert.Equal("Mega Album Extended", entry.Title);
        Assert.Equal(11, entry.SequenceNumber);
    }
}
