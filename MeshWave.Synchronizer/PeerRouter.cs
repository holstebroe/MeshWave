using MeshWave.Common.Core;
using System.Collections.Concurrent;
using MeshWave.Common.Core.P2P;
using NLog;

namespace MeshWave.Synchronizer;

/// <summary>
/// PeerRouter maintains a routing table fed from multiple sources:
/// 1. LAN UDP broadcast (via PeerDiscovery)
/// 2. Configured bootstrap nodes (initial contact points like torrent trackers)
/// 3. Peer Exchange (PEX) — peers share their known peers with each other
///
/// This allows the network to span the internet, not just the local LAN.
/// Bootstrap nodes are the only out-of-band configuration needed.
/// </summary>
public class PeerRouter : IDisposable
{
    private readonly ConcurrentDictionary<string, RoutedPeer> _table = new(StringComparer.OrdinalIgnoreCase);
    private readonly PeerDiscovery _lanDiscovery;
    private readonly ManifestExchangeClient _exchangeClient;
    private readonly Lock _bootstrapLock = new();

    private IReadOnlyList<string> _bootstrapNodes = [];
    private CancellationTokenSource? _cts;
    private Task? _bootstrapTask;
    private Task? _maintenanceTask;

    public PeerRouter(PeerDiscovery? lanDiscovery = null, ManifestExchangeClient? exchangeClient = null, Logger? logger = null)
    {
        _lanDiscovery = lanDiscovery ?? new PeerDiscovery();
        _exchangeClient = exchangeClient ?? new ManifestExchangeClient(timeoutMs: SecurityLimits.ConnectTimeoutMs, logger: logger);
    }

    public event EventHandler<PeerInfo>? PeerAdded;
    public event EventHandler<string>? PeerRemoved;

    /// <summary>
    /// Starts LAN discovery, connects to bootstrap nodes, and begins periodic maintenance.
    /// </summary>
    public async Task StartAsync(LocalPeerIdentity identity, IReadOnlyList<string> bootstrapNodes, CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _bootstrapNodes = bootstrapNodes;   // remember for periodic re-contact

        _lanDiscovery.PeerDiscovered += OnLanPeerDiscovered;
        await _lanDiscovery.StartDiscoveryAsync(identity, _cts.Token);

        _bootstrapTask = BootstrapAsync(bootstrapNodes, _cts.Token);
        _maintenanceTask = MaintenanceLoopAsync(_cts.Token);
    }

    public async Task StopAsync()
    {
        _lanDiscovery.PeerDiscovered -= OnLanPeerDiscovered;
        await _lanDiscovery.StopDiscoveryAsync();

        _cts?.Cancel();

        if (_bootstrapTask != null) try { await _bootstrapTask; } catch { }
        if (_maintenanceTask != null) try { await _maintenanceTask; } catch { }
    }

    /// <summary>
    /// Returns all currently live peers from the routing table.
    /// </summary>
    public IReadOnlyList<PeerInfo> GetPeers()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-5);
        return _table.Values
            .Where(p => p.LastSeen >= cutoff)
            .OrderByDescending(p => p.LastSeen)
            .Take(SecurityLimits.MaxRoutingTableSize)
            .Select(p => p.Info)
            .ToList();
    }

    /// <summary>
    /// Adds peers learned from a PEX response. Called by SyncOrchestrator.
    /// </summary>
    public void LearnPeers(IEnumerable<PeerInfo> peers)
    {
        foreach (var peer in peers.Take(SecurityLimits.MaxPeersPerExchange))
        {
            if (!SecurityLimits.IsValidUserId(peer.UserId)) continue;
            if (!SecurityLimits.IsValidDisplayName(peer.DisplayName)) continue;
            AddOrRefreshPeer(peer);
        }
    }

    /// <summary>
    /// Returns a sample of known peers for sharing in PEX responses.
    /// </summary>
    public IReadOnlyList<PeerInfo> GetPeersForExchange()
    {
        return GetPeers()
            .Where(p => !string.IsNullOrWhiteSpace(p.PublicKeyPem))
            .Take(SecurityLimits.MaxPeersPerExchange)
            .ToList();
    }

    private void OnLanPeerDiscovered(object? sender, PeerInfo peer)
    {
        AddOrRefreshPeer(peer);
    }

    private void AddOrRefreshPeer(PeerInfo peer)
    {
        if (_table.TryGetValue(peer.UserId, out var existing))
        {
            existing.LastSeen = DateTime.UtcNow;

            // Update address and port only if the new info is more specific
            if (!string.IsNullOrWhiteSpace(peer.Address))
                existing.Info.Address = peer.Address;

            if (peer.Port > 0)
                existing.Info.Port = peer.Port;

            if (!string.IsNullOrWhiteSpace(peer.PublicKeyPem))
                existing.Info.PublicKeyPem = peer.PublicKeyPem;

            // Sync capabilities
            foreach (var cap in peer.Capabilities)
                if (!existing.Info.Capabilities.Contains(cap))
                    existing.Info.Capabilities.Add(cap);
        }
        else
        {
            if (_table.Count >= SecurityLimits.MaxRoutingTableSize)
                EvictStalestPeer();

            var routed = new RoutedPeer { Info = peer, LastSeen = DateTime.UtcNow, Source = PeerSource.Unknown };
            if (_table.TryAdd(peer.UserId, routed))
            {
                PeerAdded?.Invoke(this, peer);
                _ = Task.Run(() => TryPexWithPeerAsync(peer, _cts?.Token ?? CancellationToken.None));
            }
        }
    }

    private async Task TryPexWithPeerAsync(PeerInfo peer, CancellationToken ct)
    {
        try
        {
            var discovered = await _exchangeClient.FetchPeersAsync(peer.Address, peer.Port, cancellationToken: ct);
            if (discovered != null) LearnPeers(discovered);
        }
        catch { }
    }

    private void EvictStalestPeer()
    {
        var stalest = _table.Values.OrderBy(p => p.LastSeen).FirstOrDefault();
        if (stalest != null && _table.TryRemove(stalest.Info.UserId, out _))
            PeerRemoved?.Invoke(this, stalest.Info.UserId);
    }

    private async Task BootstrapAsync(IReadOnlyList<string> bootstrapNodes, CancellationToken ct)
    {
        // Cap to MaxBootstrapNodes
        var nodes = bootstrapNodes.Take(SecurityLimits.MaxBootstrapNodes).ToList();

        var tasks = nodes.Select(node => TryBootstrapFromNodeAsync(node, ct));
        await Task.WhenAll(tasks);
    }

    private async Task TryBootstrapFromNodeAsync(string nodeAddress, CancellationToken ct)
    {
        // Bootstrap node format: "host:port"
        if (!TryParseEndpoint(nodeAddress, out var host, out var port))
            return;

        try
        {
            var peers = await _exchangeClient.FetchPeersAsync(host, port, cancellationToken: ct);
            if (peers != null)
            {
                LearnPeers(peers);

                // The bootstrap node itself is a potential peer
                var bootstrapPeer = new PeerInfo
                {
                    UserId = $"bootstrap:{host}:{port}",
                    DisplayName = $"Bootstrap ({host})",
                    Address = host,
                    Port = port
                };
                AddOrRefreshPeer(bootstrapPeer);
            }
        }
        catch { /* node unreachable – skip silently */ }
    }

    private async Task MaintenanceLoopAsync(CancellationToken ct)
    {
        // Periodically re-bootstrap and do PEX with random known peers.
        // Two independent counters track PEX and bootstrap intervals so
        // neither blocks the other.
        var cyclesSinceBootstrap = 0;
        const int pexIntervalSeconds = 30;
        const int bootstrapEveryNCycles = (SecurityLimits.BootstrapRetryIntervalMinutes * 60) / pexIntervalSeconds;

        while (!ct.IsCancellationRequested)
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(pexIntervalSeconds), ct);
                cyclesSinceBootstrap++;

                // PEX: ask a sample of known peers for their peer lists
                var sample = GetPeers()
                    .Where(p => !p.UserId.StartsWith("bootstrap:"))
                    .OrderBy(_ => Guid.NewGuid())
                    .Take(5)
                    .ToList();

                foreach (var peer in sample)
                    try
                    {
                        var discovered = await _exchangeClient.FetchPeersAsync(peer.Address, peer.Port, cancellationToken: ct);
                        if (discovered != null) LearnPeers(discovered);
                    }
                    catch { }

                // Periodic bootstrap re-contact — ensures peers can find the network
                // even if a bootstrap node was restarted since the last connection.
                if (cyclesSinceBootstrap >= bootstrapEveryNCycles && _bootstrapNodes.Count > 0)
                {
                    cyclesSinceBootstrap = 0;
                    await BootstrapAsync(_bootstrapNodes, ct);
                }
            }
            catch (OperationCanceledException) { break; }
            catch { }
    }

    private static bool TryParseEndpoint(string nodeAddress, out string host, out int port)
    {
        host = string.Empty;
        port = 0;

        var lastColon = nodeAddress.LastIndexOf(':');
        if (lastColon <= 0) return false;

        host = nodeAddress[..lastColon];
        return int.TryParse(nodeAddress[(lastColon + 1)..], out port) && port > 0 && port < 65536;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _lanDiscovery.Dispose();
        _cts?.Dispose();
    }

    private enum PeerSource { Unknown, Lan, Bootstrap, Pex }

    private class RoutedPeer
    {
        public required PeerInfo Info { get; set; }
        public DateTime LastSeen { get; set; }
        public PeerSource Source { get; set; }
    }
}
