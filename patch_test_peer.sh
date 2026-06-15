cat << 'INNER_EOF' > MeshWave.TestUtilities/TestPeer.cs
using MeshWave.Common.Core.Models;
using MeshWave.Synchronizer;
using MeshWave.Common.Core;
using NLog;
using NLog.Targets;

namespace MeshWave.TestUtilities;

public class TestPeer : IAsyncDisposable
{
    public string Name { get; }
    public string BaseFolder { get; }
    public string AppDataRoot { get; }
    public int Port { get; }
    public SyncOrchestrator Orchestrator { get; }
    public LocalPeerIdentity Identity { get; }

    private readonly MemoryTarget _memoryTarget;
    private readonly Logger _logger;

    public TestPeer(string name, string baseFolder, int port, SyncOrchestrator orchestrator, LocalPeerIdentity identity, MemoryTarget memoryTarget, Logger logger)
    {
        Name = name;
        BaseFolder = baseFolder;
        AppDataRoot = Path.Combine(baseFolder, "AppData");
        Port = port;
        Orchestrator = orchestrator;
        Identity = identity;
        _memoryTarget = memoryTarget;
        _logger = logger;

        Directory.CreateDirectory(AppDataRoot);
    }

    public async Task StartAsync()
    {
        await Orchestrator.StartAsync(Port);
    }

    public async Task AwaitConditionAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var timeoutSpan = timeout ?? TimeSpan.FromSeconds(5);
        var cts = new CancellationTokenSource(timeoutSpan);

        while (!condition() && !cts.IsCancellationRequested)
        {
            await Task.Delay(100, cts.Token);
        }

        if (cts.IsCancellationRequested)
        {
            throw new TimeoutException($"Condition was not met within {timeoutSpan.TotalSeconds} seconds for peer {Name}.");
        }
    }

    public List<ManifestOperation> ExtractAllOperations(ManifestStreamType streamType)
    {
        var manifest = Orchestrator.GetLocalManifest(streamType);
        if (manifest == null) return new List<ManifestOperation>();

        return manifest.Operations.Select(op => new ManifestOperation
        {
            OperationId = op.OperationId,
            OperationType = op.OperationType,
            TargetId = op.TargetId,
            TargetType = op.TargetType,
            ContentHash = op.ContentHash,
            SequenceNumber = op.SequenceNumber,
            Signature = op.Signature,
            Timestamp = op.Timestamp,
            Metadata = new Dictionary<string, string>(op.Metadata)
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

    public GroupManifest? GetGroupManifest(string groupId)
    {
        return Orchestrator.GetGroupManifest(groupId);
    }

    public async Task InjectOperationAsync(ManifestOperation op, ManifestStreamType streamType = ManifestStreamType.Content)
    {
        await Orchestrator.InjectLocalOperationAsync(op, streamType);
    }

    public IReadOnlyList<string> GetLogs()
    {
        return _memoryTarget.Logs.AsReadOnly();
    }

    public string GetLogsAsString()
    {
        return string.Join(Environment.NewLine, _memoryTarget.Logs);
    }

    public IMeshWaveEnvironment GetEnvironment()
    {
        return new TestMeshWaveEnvironment(this.AppDataRoot, this.BaseFolder);
    }
}

#pragma warning disable CS9113
public class TestMeshWaveEnvironment(string appDataRoot, string baseFolder) : IMeshWaveEnvironment
#pragma warning restore CS9113
{
    private string _appDataRoot = appDataRoot;

    public string GetAppDataRoot()
    {
        return _appDataRoot;
    }

    public void SetAppDataRootOverride(string? overridePath)
    {
        if (!string.IsNullOrEmpty(overridePath))
        {
            _appDataRoot = overridePath;
        }
    }

    public string CombineInAppData(params string[] paths)
    {
        var combined = new List<string> { _appDataRoot };
        combined.AddRange(paths);
        return Path.Combine(combined.ToArray());
    }

    public string CombineInEnvironment(params string[] paths)
    {
        return Path.Combine(paths);
    }
}
INNER_EOF
