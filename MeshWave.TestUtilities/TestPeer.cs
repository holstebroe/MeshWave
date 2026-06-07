using MeshWave.Common.Core.Models;
using MeshWave.Synchronizer;
using NLog;
using NLog.Targets;

namespace MeshWave.TestUtilities;

public class TestPeer : IAsyncDisposable
{
    private readonly MemoryTarget _memoryTarget;

    public string Name { get; }
    public string BaseFolder { get; }
    public string AppDataRoot { get; }
    public SyncOrchestrator Orchestrator { get; }
    public LocalPeerIdentity Identity { get; }
    public int Port { get; }

    public string UserId => Identity.UserId;
    public Logger Logger { get; }

    public TestPeer(string name, string baseFolder, int port, SyncOrchestrator orchestrator, LocalPeerIdentity identity, MemoryTarget memoryTarget, Logger logger)
    {
        Name = name;
        BaseFolder = baseFolder;
        AppDataRoot = Path.Combine(baseFolder, "AppData");
        Directory.CreateDirectory(AppDataRoot);
        Port = port;
        Orchestrator = orchestrator;
        Identity = identity;
        Logger = logger;
        _memoryTarget = memoryTarget;
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

        if (BaseFolder.StartsWith(Path.GetTempPath()))
            if (Directory.Exists(BaseFolder))
                Directory.Delete(BaseFolder, true);
    }

    public Manifest? GetLocalManifest(ManifestStreamType streamType)
    {
        return Orchestrator.GetLocalManifest(streamType);
    }

    public Manifest? GetPeerManifest(string userId, ManifestStreamType streamType = ManifestStreamType.Content)
    {
        return Orchestrator.GetPeerManifest(userId, streamType);
    }

    public void AnnounceTrack(string trackId, string hash, Dictionary<string, string>? metadata = null)
    {
        Orchestrator.AnnounceTrack(trackId, hash, metadata);
    }

    public void BroadcastProfile(string displayName, bool isArtist, string bio = "", string? website = null, string? iconHash = null)
    {
        Orchestrator.BroadcastProfile(displayName, isArtist, bio, website, iconHash);
    }

    public async Task SyncAsync()
    {
        await Orchestrator.SyncAllPeersAsync();
    }

    public IReadOnlyList<string> GetLogs()
    {
        return _memoryTarget.Logs.AsReadOnly();
    }

    public string GetLogsAsString()
    {
        return string.Join(Environment.NewLine, _memoryTarget.Logs);
    }
}
