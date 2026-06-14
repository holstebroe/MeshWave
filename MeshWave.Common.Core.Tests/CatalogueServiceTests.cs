using MeshWave.Common.Core.Models;
using Xunit;

namespace MeshWave.Common.Core.Tests;

public class CatalogueServiceTests
{
    [Fact]
    public async Task IngestAsync_AddsNewEntries()
    {
        var service = new CatalogueService(MeshWave.Common.Core.Processors.CatalogueProcessorDefaults.GetDefaultProcessors());
        var manifest = new Manifest
        {
            UserId = "user1",
            Operations = new List<ManifestOperation>
            {
                new()
                {
                    OperationId = "op1",
                    OperationType = ManifestOperationType.Create,
                    TargetId = "track1",
                    TargetType = "Track",
                    ContentHash = "hash1",
                    SequenceNumber = 1,
                    Signature = "sig",
                    Metadata = new Dictionary<string, string> { { "title", "Song A" }, { "artist", "Artist X" }, { "fileSize", "1024" } }
                }
            }
        };

        await service.IngestAsync(manifest);

        var entry = await service.GetEntryAsync("track1");
        Assert.NotNull(entry);
        Assert.Equal("Song A", entry.Title);
        Assert.Equal("Artist X", entry.ArtistName);
        Assert.Equal("user1", entry.OwnerUserId);
        Assert.Equal(1024, entry.FileSize);

        var peers = await service.GetPeersForContentAsync("hash1");
        Assert.Contains("user1", peers);
    }

    [Fact]
    public async Task IngestAsync_AppliesStalenessRule()
    {
        var service = new CatalogueService(MeshWave.Common.Core.Processors.CatalogueProcessorDefaults.GetDefaultProcessors());
        var manifest1 = new Manifest
        {
            UserId = "user1",
            Operations = new List<ManifestOperation>
            {
                new()
                {
                    OperationId = "op1",
                    OperationType = ManifestOperationType.Create,
                    TargetId = "track1",
                    TargetType = "Track",
                    SequenceNumber = 5,
                    Signature = "sig",
                    Metadata = new Dictionary<string, string> { { "title", "New Title" } }
                }
            }
        };

        var manifest2 = new Manifest
        {
            UserId = "user2",
            Operations = new List<ManifestOperation>
            {
                new()
                {
                    OperationId = "op2",
                    OperationType = ManifestOperationType.Update,
                    TargetId = "track1",
                    TargetType = "Track",
                    SequenceNumber = 3,
                    Signature = "sig",
                    Metadata = new Dictionary<string, string> { { "title", "Old Title" } }
                }
            }
        };

        // Ingest newer first
        await service.IngestAsync(manifest1);
        // Ingest older
        await service.IngestAsync(manifest2);

        var entry = await service.GetEntryAsync("track1");
        Assert.NotNull(entry);
        Assert.Equal("New Title", entry.Title);
    }

    [Fact]
    public async Task SearchAsync_ReturnsMatchingEntries()
    {
        var service = new CatalogueService(MeshWave.Common.Core.Processors.CatalogueProcessorDefaults.GetDefaultProcessors());
        var manifest = new Manifest
        {
            UserId = "user1",
            Operations = new List<ManifestOperation>
            {
                new()
                {
                    OperationId = "op1",
                    OperationType = ManifestOperationType.Create,
                    TargetId = "track1",
                    TargetType = "Track",
                    SequenceNumber = 1,
                    Signature = "sig",
                    Metadata = new Dictionary<string, string> { { "title", "Yellow Submarine" }, { "artist", "The Beatles" } }
                },
                new()
                {
                    OperationId = "op2",
                    OperationType = ManifestOperationType.Create,
                    TargetId = "track2",
                    TargetType = "Track",
                    SequenceNumber = 2,
                    Signature = "sig",
                    Metadata = new Dictionary<string, string> { { "title", "Help!" }, { "artist", "The Beatles" }, { "fileSize", "2048" } }
                }
            }
        };

        await service.IngestAsync(manifest);

        var results = await service.SearchAsync("Beatles");
        Assert.Equal(2, results.Count());

        var results2 = await service.SearchAsync("Yellow");
        Assert.Single(results2);
        Assert.Equal("track1", results2.First().EntryId);
    }

    [Fact]
    public async Task IngestAsync_HandlesSnapshots()
    {
        var service = new CatalogueService(MeshWave.Common.Core.Processors.CatalogueProcessorDefaults.GetDefaultProcessors());
        var manifest = new Manifest
        {
            UserId = "user1",
            Snapshot = new ManifestSnapshot
            {
                LastSequenceNumber = 10,
                Signature = "sig",
                EntityStates = new List<SnapshotStateEntry>
                {
                    new()
                    {
                        TargetId = "album1",
                        TargetType = "Album",
                        ContentHash = "ahash",
                        Metadata = new Dictionary<string, string> { { "title", "Snapshot Album" } }
                    }
                }
            }
        };

        await service.IngestAsync(manifest);

        var entry = await service.GetEntryAsync("album1");
        Assert.NotNull(entry);
        Assert.Equal("Snapshot Album", entry.Title);
        Assert.Equal(10, entry.SequenceNumber);
    }
}
