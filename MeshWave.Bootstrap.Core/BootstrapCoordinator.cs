using System.Collections.Concurrent;
using System.Net;
using MeshWave.Synchronizer;

namespace MeshWave.Bootstrap.Core;

/// <summary>
/// Hosts a bootstrap coordinator that registers live peers and serves PEX responses.
/// </summary>
public sealed class BootstrapCoordinator : IDisposable
{
    private readonly ConcurrentDictionary<string, BootstrapPeerEntry> _peers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ManifestExchangeServer _server;

    private int _requestCount;
    private int _peerCount;

    public BootstrapCoordinator(int port)
    {
        Port = port;
        _server = new ManifestExchangeServer(port);
    }

    public int Port { get; }
    public int RequestCount => _requestCount;
    public int RegisteredPeerCount => _peers.Count;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _server.ManifestReceived += OnManifestReceived;

        await _server.StartAsync(
            localManifestProvider: () => null,
            peersProvider: GetLivePeers,
            cancellationToken: cancellationToken);
    }

    public async Task StopAsync()
    {
        _server.ManifestReceived -= OnManifestReceived;
        await _server.StopAsync();
    }

    public IReadOnlyList<PeerInfo> GetLivePeers()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-10);
        return _peers.Values
            .Where(e => e.LastSeen >= cutoff)
            .OrderByDescending(e => e.LastSeen)
            .Take(SecurityLimits.MaxPeersPerExchange)
            .Select(e => e.Peer)
            .ToList();
    }

    public async Task SeedFromNodesAsync(IEnumerable<string> seeds, CancellationToken ct = default)
    {
        var client = new ManifestExchangeClient(timeoutMs: 5_000);
        foreach (var seed in seeds.Take(SecurityLimits.MaxBootstrapNodes))
        {
            try
            {
                var (host, port) = ParseEndpoint(seed, Port);
                var peers = await client.FetchPeersAsync(host, port, ct);
                foreach (var p in peers)
                    RegisterPeer(p);
            }
            catch
            {
                // best-effort seeding
            }
        }
    }

    private void OnManifestReceived(object? sender, ManifestReceivedEventArgs e)
    {
        Interlocked.Increment(ref _requestCount);

        var manifest = e.Manifest;
        if (manifest == null)
            return;

        if (!IPAddress.TryParse(e.PeerAddress, out _))
            return;

        var peer = new PeerInfo
        {
            UserId = manifest.UserId,
            DisplayName = SecurityLimits.Truncate(manifest.UserId, SecurityLimits.MaxDisplayNameLength),
            Address = e.PeerAddress,
            Port = ManifestExchangeServer.DefaultPort,
            LastSeen = DateTime.UtcNow
        };

        RegisterPeer(peer);
    }

    private void RegisterPeer(PeerInfo peer)
    {
        if (!SecurityLimits.IsValidUserId(peer.UserId)) return;
        if (!SecurityLimits.IsValidDisplayName(peer.DisplayName)) return;

        if (_peers.TryGetValue(peer.UserId, out var existing))
        {
            existing.LastSeen = DateTime.UtcNow;
            return;
        }

        if (_peers.Count >= SecurityLimits.MaxRoutingTableSize)
            EvictStalest();

        var entry = new BootstrapPeerEntry { Peer = peer, LastSeen = DateTime.UtcNow };
        if (_peers.TryAdd(peer.UserId, entry))
            Interlocked.Increment(ref _peerCount);
    }

    private void EvictStalest()
    {
        var stalest = _peers.Values.OrderBy(e => e.LastSeen).FirstOrDefault();
        if (stalest != null)
            _peers.TryRemove(stalest.Peer.UserId, out _);
    }

    private static (string host, int port) ParseEndpoint(string endpoint, int defaultPort)
    {
        var lastColon = endpoint.LastIndexOf(':');
        if (lastColon > 0 && int.TryParse(endpoint[(lastColon + 1)..], out var p))
            return (endpoint[..lastColon], p);
        return (endpoint, defaultPort);
    }

    public void Dispose()
    {
        _server.Dispose();
    }
}

internal sealed class BootstrapPeerEntry
{
    public required PeerInfo Peer { get; set; }
    public DateTime LastSeen { get; set; }
}
