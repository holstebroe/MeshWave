using System.Net;
using System.Net.Sockets;
using MeshWave.Common.Core.Crypto;
using MeshWave.Common.Core.Models;
using MeshWave.Common.Core.Storage;
using MeshWave.Synchronizer;
using MeshWave.Bootstrap.Core;

namespace MeshWave.TestUtilities;

public class TestPeer : IAsyncDisposable
{
    public string Name { get; }
    public string BaseDir { get; }
    public SyncOrchestrator Orchestrator { get; }
    public LocalPeerIdentity Identity { get; }
    public int Port { get; }

    public string UserId => Identity.UserId;

    public TestPeer(string name, string baseDir, int port, SyncOrchestrator orchestrator, LocalPeerIdentity identity)
    {
        Name = name;
        BaseDir = baseDir;
        Port = port;
        Orchestrator = orchestrator;
        Identity = identity;
    }

    public async Task StartAsync(IEnumerable<Manifest>? initialManifests = null, IReadOnlyList<string>? bootstrapNodes = null, bool actAsListener = true, Func<string, byte[]?>? contentProvider = null)
    {
        var manifests = initialManifests ?? CreateEmptyManifests();
        await Orchestrator.StartAsync(Identity, manifests, bootstrapNodes, actAsListener, contentProvider);
    }

    private List<Manifest> CreateEmptyManifests()
    {
        return Enum.GetValues<ManifestStreamType>().Select(st => new Manifest
        {
            UserId = UserId,
            StreamType = st,
            Operations = [],
            Version = 1,
            LastUpdated = DateTime.UtcNow
        }).ToList();
    }

    public async ValueTask DisposeAsync()
    {
        await Orchestrator.StopAsync();
        Orchestrator.Dispose();
    }

    public Manifest? GetLocalManifest(ManifestStreamType streamType) => Orchestrator.GetLocalManifest(streamType);
    public Manifest? GetPeerManifest(string userId, ManifestStreamType streamType = ManifestStreamType.Content) => Orchestrator.GetPeerManifest(userId, streamType);

    public void AnnounceTrack(string trackId, string hash, Dictionary<string, string>? metadata = null)
        => Orchestrator.AnnounceTrack(trackId, hash, metadata);

    public void BroadcastProfile(string displayName, bool isArtist, string bio = "", string? website = null, string? iconHash = null)
        => Orchestrator.BroadcastProfile(displayName, isArtist, bio, website, iconHash);

    public async Task SyncAsync() => await Orchestrator.SyncAllPeersAsync();
}
