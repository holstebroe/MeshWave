using MeshWave.Common.Core;
using System.Collections.Concurrent;
using System.Net;
using MeshWave.Common.Core.Crypto;
using MeshWave.Common.Core.P2P;
using MeshWave.Synchronizer;
using NLog;

namespace MeshWave.Bootstrap.Core;

/// <summary>
/// Hosts a bootstrap coordinator: it registers live peers (Announce, with a dial-back check of the announced port),
/// serves PEX responses, keeps control sessions with peers, and introduces peers to each other for UDP hole
/// punching. It never stores, relays or serves manifests or content.
/// </summary>
public sealed class BootstrapCoordinator : IDisposable
{
    private readonly Logger _logger;
    private readonly ConcurrentDictionary<string, BootstrapPeerEntry> _peers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ManifestExchangeServer _server;
    private readonly PeerSessionManager _sessions;

    private int _requestCount;
    private int _peerCount;

    public event EventHandler<BootstrapPeerEventArgs>? PeerRegistered;
    public event EventHandler<BootstrapPeerEventArgs>? PeerRefreshed;
    public event EventHandler<BootstrapPeerEventArgs>? PeerDisconnected;

    public BootstrapCoordinator(int port, Logger logger)
    {
        _logger = logger;
        Port = port;
        _server = new ManifestExchangeServer(port, logger: logger);
        // One control session per registered peer.
        _sessions = new PeerSessionManager(logger) { MaxSessions = SecurityLimits.MaxRoutingTableSize };
    }

    public int Port { get; }
    public int RequestCount => _requestCount;
    public int RegisteredPeerCount => _peers.Count;

    /// <summary>Number of peers holding a control session with this node.</summary>
    public int SessionCount => _sessions.Sessions.Count(s => s.IsAuthenticated);

    /// <summary>The UDP port used for NAT introductions (0 if it could not be bound).</summary>
    public int UdpPort => _sessions.UdpPort;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _server.PeerAnnounced += OnPeerAnnounced;

        _sessions.Configure(
            identity: null,
            selfRecordProvider: () => null,
            dataHandler: (request, remoteAddress, ct) => _server.HandleRequestAsync(request, remoteAddress, inlineContent: true, ct),
            isIntroducer: true);
        _sessions.SessionActivity += OnSessionActivity;
        _sessions.SessionAuthenticated += OnSessionActivity;
        _sessions.Start(Port);
        _server.SessionUpgradeHandler = _sessions.AcceptTcpSession;

        await _server.StartAsync(
            localManifestProvider: _ => null,
            peersProvider: GetLivePeers,
            cancellationToken: cancellationToken);
    }

    public async Task StopAsync()
    {
        _server.PeerAnnounced -= OnPeerAnnounced;
        _sessions.SessionActivity -= OnSessionActivity;
        _sessions.SessionAuthenticated -= OnSessionActivity;
        await _server.StopAsync();
        _sessions.Stop();
    }

    public IReadOnlyList<PeerInfo> GetLivePeers()
    {
        PruneStalePeers();

        return _peers.Values
            .OrderByDescending(e => e.LastSeen)
            .Take(SecurityLimits.MaxPeersPerExchange)
            .Select(e =>
            {
                var copy = PeerRecords.Clone(e.Peer);
                copy.LastSeen = e.LastSeen;
                return copy;
            })
            .ToList();
    }

    public async Task SeedFromNodesAsync(IEnumerable<string> seeds, CancellationToken ct = default)
    {
        var client = new ManifestExchangeClient(timeoutMs: 5_000, logger: _logger);
        foreach (var seed in seeds.Take(SecurityLimits.MaxBootstrapNodes))
            try
            {
                var (host, port) = ParseEndpoint(seed, Port);
                var peers = await client.FetchPeersAsync(host, port, cancellationToken: ct);
                if (peers != null)
                    foreach (var p in peers)
                        RegisterPeer(p);
            }
            catch
            {
                // best-effort seeding
            }
    }

    private void OnPeerAnnounced(object? sender, PeerAnnouncedEventArgs e)
    {
        Interlocked.Increment(ref _requestCount);

        if (!IPAddress.TryParse(e.Peer.Address, out _))
            return;

        // Announcements must prove key ownership of the UserId.
        if (!CryptoService.IsPublicKeyForUser(e.Peer.UserId, e.Peer.PublicKey))
            return;

        RegisterPeer(e.Peer);
    }

    /// <summary>A live control session is first-hand evidence that the peer is online.</summary>
    private void OnSessionActivity(PeerSession session)
    {
        if (session.RemoteUserId != null && _peers.TryGetValue(session.RemoteUserId, out var entry))
            entry.LastSeen = DateTime.UtcNow;
    }

    private void RegisterPeer(PeerInfo peer)
    {
        if (!SecurityLimits.IsValidUserId(peer.UserId)) return;
        if (!SecurityLimits.IsValidDisplayName(peer.DisplayName)) return;

        // UserIds are derived from public keys; never let a forged key claim someone else's UserId.
        if (!string.IsNullOrWhiteSpace(peer.PublicKey) && !CryptoService.IsPublicKeyForUser(peer.UserId, peer.PublicKey))
        {
            _logger.Warn("Rejected registration for user {0} from {1}: public key does not match the UserId.", peer.UserId, peer.Address);
            return;
        }

        if (_peers.TryGetValue(peer.UserId, out var existing))
        {
            existing.LastSeen = DateTime.UtcNow;
            var publicKey = string.IsNullOrWhiteSpace(peer.PublicKey) ? existing.Peer.PublicKey : peer.PublicKey;
            existing.Peer = PeerRecords.Clone(peer);
            existing.Peer.PublicKey = publicKey;
            PeerRefreshed?.Invoke(this, new BootstrapPeerEventArgs(existing.Peer, "refreshed"));
            return;
        }

        if (_peers.Count >= SecurityLimits.MaxRoutingTableSize)
            EvictStalest();

        var entry = new BootstrapPeerEntry { Peer = PeerRecords.Clone(peer), LastSeen = DateTime.UtcNow };
        if (_peers.TryAdd(peer.UserId, entry))
        {
            Interlocked.Increment(ref _peerCount);
            PeerRegistered?.Invoke(this, new BootstrapPeerEventArgs(entry.Peer, "registered"));
        }
    }

    private void EvictStalest()
    {
        var stalest = _peers.Values.OrderBy(e => e.LastSeen).FirstOrDefault();
        if (stalest != null && _peers.TryRemove(stalest.Peer.UserId, out var removed)) PeerDisconnected?.Invoke(this, new BootstrapPeerEventArgs(removed.Peer, "evicted"));
    }

    private void PruneStalePeers()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-SecurityLimits.PeerLivenessTimeoutMinutes);
        foreach (var stale in _peers.Where(kv => kv.Value.LastSeen < cutoff).ToList())
            if (_peers.TryRemove(stale.Key, out var removed))
                PeerDisconnected?.Invoke(this, new BootstrapPeerEventArgs(removed.Peer, "stale-timeout"));
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
        _sessions.Dispose();
    }
}

public sealed class BootstrapPeerEventArgs(PeerInfo peer, string reason) : EventArgs
{
    public PeerInfo Peer { get; } = peer;
    public string Reason { get; } = reason;
}

internal sealed class BootstrapPeerEntry
{
    public required PeerInfo Peer { get; set; }
    public DateTime LastSeen { get; set; }
}
