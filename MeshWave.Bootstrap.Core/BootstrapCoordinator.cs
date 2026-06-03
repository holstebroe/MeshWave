using System.Collections.Concurrent;
using System.Net;
using MeshWave.Common.Core.Models;
using MeshWave.Synchronizer;

namespace MeshWave.Bootstrap.Core;

/// <summary>
/// Hosts a bootstrap coordinator that registers live peers and serves PEX responses.
/// </summary>
public sealed class BootstrapCoordinator : IDisposable
{
    private readonly ConcurrentDictionary<string, BootstrapPeerEntry> _peers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, BootstrapRendezvousSession> _rendezvousSessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Manifest> _relayedManifests = new(StringComparer.OrdinalIgnoreCase);
    private readonly ManifestExchangeServer _server;

    private int _requestCount;
    private int _peerCount;

    public event EventHandler<BootstrapPeerEventArgs>? PeerRegistered;
    public event EventHandler<BootstrapPeerEventArgs>? PeerRefreshed;
    public event EventHandler<BootstrapPeerEventArgs>? PeerDisconnected;

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
            rendezvousProvider: OnRendezvousRequested,
            relayedManifestProvider: (userId) => _relayedManifests.GetValueOrDefault(userId),
            cancellationToken: cancellationToken);
    }

    public async Task StopAsync()
    {
        _server.ManifestReceived -= OnManifestReceived;
        await _server.StopAsync();
    }

    public IReadOnlyList<PeerInfo> GetLivePeers()
    {
        PruneStalePeers();

        return _peers.Values
            .OrderByDescending(e => e.LastSeen)
            .Take(SecurityLimits.MaxPeersPerExchange)
            .Select(e =>
            {
                var p = e.Peer;
                if (_relayedManifests.ContainsKey(p.UserId))
                {
                    return new PeerInfo
                    {
                        UserId = p.UserId,
                        DisplayName = p.DisplayName,
                        Address = p.Address,
                        Port = p.Port,
                        PublicKeyPem = p.PublicKeyPem,
                        LastSeen = p.LastSeen,
                        Capabilities = p.Capabilities.Contains("relay") ? p.Capabilities : [.. p.Capabilities, "relay"]
                    };
                }
                return p;
            })
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

        var latestProfile = manifest.Operations
            .Where(op => op.OperationType == ManifestOperationType.Profile)
            .OrderByDescending(op => op.SequenceNumber)
            .FirstOrDefault();

        var displayName = latestProfile?.Metadata.GetValueOrDefault("displayName");
        var publicKeyPem = e.AnnouncingPeer?.PublicKeyPem ?? latestProfile?.Metadata.GetValueOrDefault("publicKeyPem") ?? string.Empty;
        var announcedPort = e.AnnouncingPeer?.Port ?? 0;

        var peer = new PeerInfo
        {
            UserId = manifest.UserId,
            DisplayName = SecurityLimits.Truncate(
                string.IsNullOrWhiteSpace(displayName)
                    ? (string.IsNullOrWhiteSpace(e.AnnouncingPeer?.DisplayName) ? manifest.UserId : e.AnnouncingPeer.DisplayName)
                    : displayName,
                SecurityLimits.MaxDisplayNameLength),
            Address = e.PeerAddress,
            Port = announcedPort > 0 ? announcedPort : ManifestExchangeServer.DefaultPort,
            PublicKeyPem = publicKeyPem,
            LastSeen = DateTime.UtcNow
        };

        RegisterPeer(peer);

        if (e.IsRelay)
        {
            _relayedManifests[manifest.UserId] = manifest;
        }
    }

    private RendezvousResponse OnRendezvousRequested(RendezvousRequest request)
    {
        if (request == null || !SecurityLimits.IsValidUserId(request.InitiatorUserId) || !SecurityLimits.IsValidUserId(request.TargetUserId))
        {
            return new RendezvousResponse
            {
                Success = false,
                Message = "Invalid rendezvous request."
            };
        }

        var now = DateTime.UtcNow;
        var probeWindow = Math.Clamp(request.RequestedProbeWindowMs, 1_500, 10_000);
        var probeStart = now.AddMilliseconds(1_200);
        var expiry = probeStart.AddMilliseconds(probeWindow + 2_000);
        var session = new BootstrapRendezvousSession
        {
            SessionId = Guid.NewGuid().ToString("N"),
            InitiatorUserId = request.InitiatorUserId,
            TargetUserId = request.TargetUserId,
            InitiatorPort = request.InitiatorPort,
            ProbeStartUtc = probeStart,
            ProbeWindowMs = probeWindow,
            CreatedAtUtc = now,
            ExpiresAtUtc = expiry
        };

        _rendezvousSessions[session.SessionId] = session;

        foreach (var stale in _rendezvousSessions.Where(kv => kv.Value.ExpiresAtUtc <= now).Select(kv => kv.Key).ToList())
            _rendezvousSessions.TryRemove(stale, out _);

        var targetKnown = _peers.ContainsKey(request.TargetUserId);
        return new RendezvousResponse
        {
            Success = true,
            SessionId = session.SessionId,
            ExpiresAtUtc = expiry,
            ProbeStartUtc = session.ProbeStartUtc,
            ProbeWindowMs = session.ProbeWindowMs,
            Message = targetKnown
                ? "Rendezvous session issued. Start coordinated outbound probes at probeStartUtc."
                : "Rendezvous session issued; target is not currently registered on this bootstrap."
        };
    }

    private void RegisterPeer(PeerInfo peer)
    {
        if (!SecurityLimits.IsValidUserId(peer.UserId)) return;
        if (!SecurityLimits.IsValidDisplayName(peer.DisplayName)) return;

        if (_peers.TryGetValue(peer.UserId, out var existing))
        {
            existing.LastSeen = DateTime.UtcNow;
            existing.Peer.Address = peer.Address;
            existing.Peer.Port = peer.Port;
            existing.Peer.DisplayName = peer.DisplayName;
            if (!string.IsNullOrWhiteSpace(peer.PublicKeyPem))
                existing.Peer.PublicKeyPem = peer.PublicKeyPem;
            PeerRefreshed?.Invoke(this, new BootstrapPeerEventArgs(existing.Peer, "refreshed"));
            return;
        }

        if (_peers.Count >= SecurityLimits.MaxRoutingTableSize)
            EvictStalest();

        var entry = new BootstrapPeerEntry { Peer = peer, LastSeen = DateTime.UtcNow };
        if (_peers.TryAdd(peer.UserId, entry))
        {
            Interlocked.Increment(ref _peerCount);
            PeerRegistered?.Invoke(this, new BootstrapPeerEventArgs(peer, "registered"));
        }
    }

    private void EvictStalest()
    {
        var stalest = _peers.Values.OrderBy(e => e.LastSeen).FirstOrDefault();
        if (stalest != null && _peers.TryRemove(stalest.Peer.UserId, out var removed))
        {
            PeerDisconnected?.Invoke(this, new BootstrapPeerEventArgs(removed.Peer, "evicted"));
        }
    }

    private void PruneStalePeers()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-10);
        foreach (var stale in _peers.Where(kv => kv.Value.LastSeen < cutoff).ToList())
        {
            if (_peers.TryRemove(stale.Key, out var removed))
            {
                _relayedManifests.TryRemove(stale.Key, out _);
                PeerDisconnected?.Invoke(this, new BootstrapPeerEventArgs(removed.Peer, "stale-timeout"));
            }
        }

        // Also prune relayed manifests that might not have an active peer entry
        foreach (var userId in _relayedManifests.Keys)
        {
            if (!_peers.ContainsKey(userId))
            {
                _relayedManifests.TryRemove(userId, out _);
            }
        }
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

internal sealed class BootstrapRendezvousSession
{
    public required string SessionId { get; set; }
    public required string InitiatorUserId { get; set; }
    public required string TargetUserId { get; set; }
    public int InitiatorPort { get; set; }
    public DateTime ProbeStartUtc { get; set; }
    public int ProbeWindowMs { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}
