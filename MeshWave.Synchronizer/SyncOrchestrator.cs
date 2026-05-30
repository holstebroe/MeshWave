using MeshWave.Common.Core.Models;

namespace MeshWave.Synchronizer;

/// <summary>
/// SyncOrchestrator is the top-level P2P coordinator.
/// It uses PeerRouter (LAN + bootstrap + PEX) to find peers,
/// exchanges manifests over TCP, and merges verified operations.
/// </summary>
public class SyncOrchestrator : IDisposable
{
    private readonly PeerRouter _router;
    private readonly ManifestExchangeServer _server;
    private readonly ManifestExchangeClient _client;
    private readonly ManifestManager _manifestManager;

    private LocalPeerIdentity? _identity;
    private Manifest? _localManifest;
    private CancellationTokenSource? _cts;

    public event EventHandler<ManifestMergedEventArgs>? ManifestMerged;

    public SyncOrchestrator(
        PeerRouter? router = null,
        ManifestExchangeServer? server = null,
        ManifestExchangeClient? client = null,
        ManifestManager? manifestManager = null)
    {
        _router = router ?? new PeerRouter();
        _server = server ?? new ManifestExchangeServer();
        _client = client ?? new ManifestExchangeClient(timeoutMs: SecurityLimits.ConnectTimeoutMs);
        _manifestManager = manifestManager ?? new ManifestManager();
    }

    /// <summary>
    /// Starts P2P sync: LAN discovery, bootstrap node connections, PEX, and manifest exchange server.
    /// </summary>
    /// <param name="identity">Local peer identity with keypair.</param>
    /// <param name="localManifest">This user's own manifest to serve to peers.</param>
    /// <param name="bootstrapNodes">Internet bootstrap node addresses (format: "host:port").</param>
    /// <param name="cancellationToken">Cancellation token for graceful shutdown.</param>
    public async Task StartAsync(
        LocalPeerIdentity identity,
        Manifest localManifest,
        IReadOnlyList<string>? bootstrapNodes = null,
        CancellationToken cancellationToken = default)
    {
        _identity = identity;
        _localManifest = localManifest;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _router.PeerAdded += OnPeerAdded;
        _server.ManifestReceived += OnManifestReceived;

        await _server.StartAsync(
            () => _localManifest,
            () => _router.GetPeersForExchange(),
            _cts.Token);

        await _router.StartAsync(identity, bootstrapNodes ?? [], _cts.Token);
    }

    /// <summary>
    /// Stops all P2P activity.
    /// </summary>
    public async Task StopAsync()
    {
        _router.PeerAdded -= OnPeerAdded;
        _server.ManifestReceived -= OnManifestReceived;

        await _router.StopAsync();
        await _server.StopAsync();
        _cts?.Cancel();
    }

    /// <summary>
    /// Returns currently visible peers from the routing table.
    /// </summary>
    public IEnumerable<PeerInfo> GetPeers() => _router.GetPeers();

    /// <summary>
    /// Manually triggers a manifest fetch from all known peers.
    /// </summary>
    public async Task SyncAllPeersAsync(CancellationToken cancellationToken = default)
    {
        foreach (var peer in _router.GetPeers())
        {
            await TryFetchAndMergeAsync(peer, cancellationToken);
        }
    }

    private void OnPeerAdded(object? sender, PeerInfo peer)
    {
        _ = Task.Run(() => TryFetchAndMergeAsync(peer, _cts?.Token ?? CancellationToken.None));
    }

    private void OnManifestReceived(object? sender, ManifestReceivedEventArgs e)
    {
        if (_localManifest == null) return;

        var peer = _router.GetPeers().FirstOrDefault(p => p.UserId == e.Manifest.UserId);
        if (peer == null || string.IsNullOrWhiteSpace(peer.PublicKeyPem)) return;

        TryMerge(e.Manifest, peer.PublicKeyPem);
    }

    private async Task TryFetchAndMergeAsync(PeerInfo peer, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(peer.PublicKeyPem)) return;

        try
        {
            var remoteManifest = await _client.FetchManifestAsync(peer.Address, peer.Port, ct);
            if (remoteManifest == null) return;
            TryMerge(remoteManifest, peer.PublicKeyPem);
        }
        catch { /* peer unreachable – will retry on next cycle */ }
    }

    private void TryMerge(Manifest remote, string publicKeyPem)
    {
        if (_localManifest == null || remote.UserId == _identity?.UserId) return;

        try
        {
            var added = _manifestManager.MergeManifest(_localManifest, remote, publicKeyPem);
            if (added > 0)
                ManifestMerged?.Invoke(this, new ManifestMergedEventArgs(remote.UserId, added));
        }
        catch { /* reject invalid/oversized manifests */ }
    }

    public void Dispose()
    {
        _router.Dispose();
        _server.Dispose();
        _cts?.Dispose();
    }
}

public class ManifestMergedEventArgs(string userId, int operationsAdded) : EventArgs
{
    public string UserId { get; } = userId;
    public int OperationsAdded { get; } = operationsAdded;
}
