using MeshWave.Common.Core;
using System.Collections.Concurrent;
using MeshWave.Common.Core.Crypto;
using MeshWave.Common.Core.P2P;
using NLog;

namespace MeshWave.Synchronizer;

/// <summary>
/// PeerRouter maintains a routing table fed from multiple sources:
/// 1. LAN UDP broadcast (via PeerDiscovery)
/// 2. Configured bootstrap nodes (initial contact points like torrent trackers)
/// 3. Peer Exchange (PEX) — peers share their known peers with each other
///
/// Liveness and addresses are only taken from first-hand evidence: a direct contact with the peer, or a record the
/// peer signed itself (<see cref="PeerRecords"/>). Hearsay (unsigned PEX entries) can introduce an unknown peer but can
/// neither keep a known peer alive nor change its address.
/// </summary>
public class PeerRouter : IDisposable
{
    private readonly ConcurrentDictionary<string, RoutedPeer> _table = new(StringComparer.OrdinalIgnoreCase);
    private readonly PeerDiscovery _lanDiscovery;
    private readonly ManifestExchangeClient _exchangeClient;
    private readonly Logger _logger;

    private IReadOnlyList<string> _bootstrapNodes = [];
    private string? _localUserId;
    private Func<PeerInfo?>? _selfAnnouncementProvider;
    private CancellationTokenSource? _cts;
    private Task? _bootstrapTask;
    private Task? _maintenanceTask;

    public PeerRouter(PeerDiscovery? lanDiscovery = null, ManifestExchangeClient? exchangeClient = null, Logger? logger = null)
    {
        _lanDiscovery = lanDiscovery ?? new PeerDiscovery();
        _exchangeClient = exchangeClient ?? new ManifestExchangeClient(timeoutMs: SecurityLimits.ConnectTimeoutMs, logger: logger);
        _logger = logger ?? LogManager.GetCurrentClassLogger();
    }

    public event EventHandler<PeerInfo>? PeerAdded;
    public event EventHandler<string>? PeerRemoved;

    /// <summary>
    /// Raised after announcing to a bootstrap node: the address the node observed for us, and whether it could
    /// connect back to our announced port (null when it did not check).
    /// </summary>
    public event EventHandler<AnnounceResult>? SelfObserved;

    /// <summary>
    /// Whether this node can currently reach a peer (e.g. over a persistent session) even though it is not dialable.
    /// Set by the orchestrator; used to include such peers in PEX sampling.
    /// </summary>
    public Func<PeerInfo, bool>? HasSession { get; set; }

    /// <summary>
    /// Starts LAN discovery, connects to bootstrap nodes, and begins periodic maintenance.
    /// </summary>
    /// <param name="selfAnnouncementProvider">
    /// Returns this peer's info to register with every bootstrap node on each (re)contact, so that bootstrap nodes
    /// learn about us even if we never push a manifest to them. The announcements double as the registration heartbeat.
    /// </param>
    public async Task StartAsync(LocalPeerIdentity identity, IReadOnlyList<string> bootstrapNodes, CancellationToken cancellationToken = default, Func<PeerInfo?>? selfAnnouncementProvider = null)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _localUserId = identity.UserId;
        _selfAnnouncementProvider = selfAnnouncementProvider;

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
        var cutoff = LivenessCutoff();
        return _table.Values
            .Where(p => p.LastSeen >= cutoff)
            .OrderByDescending(p => p.LastSeen)
            .Take(SecurityLimits.MaxRoutingTableSize)
            .Select(p => p.Info)
            .ToList();
    }

    /// <summary>
    /// Adds peers learned second-hand (PEX, bootstrap peer lists). See the class remarks for what hearsay may change.
    /// </summary>
    public void LearnPeers(IEnumerable<PeerInfo> peers)
    {
        foreach (var peer in peers.Take(SecurityLimits.MaxPeersPerExchange))
            if (IsAcceptable(peer))
                AddOrRefreshPeer(peer, direct: false);
    }

    /// <summary>
    /// Adds or refreshes a peer this node is in direct contact with (it announced itself, pushed to us, completed a
    /// session handshake, or answered a request). <paramref name="peer"/>'s address must be the observed one.
    /// </summary>
    public void LearnPeerDirect(PeerInfo peer)
    {
        if (IsAcceptable(peer))
            AddOrRefreshPeer(peer, direct: true);
    }

    /// <summary>
    /// Records a successful direct exchange with a known peer, keeping it alive in the routing table.
    /// </summary>
    public void MarkContacted(string userId)
    {
        if (_table.TryGetValue(userId, out var existing))
        {
            existing.LastSeen = DateTime.UtcNow;
            existing.LastDirectContact = existing.LastSeen;
        }
    }

    /// <summary>
    /// Returns a sample of known peers for sharing in PEX responses, each with the time it was last seen alive.
    /// </summary>
    public IReadOnlyList<PeerInfo> GetPeersForExchange()
    {
        var cutoff = LivenessCutoff();
        return _table.Values
            .Where(p => p.LastSeen >= cutoff && !string.IsNullOrWhiteSpace(p.Info.PublicKeyPem))
            .OrderByDescending(p => p.LastSeen)
            .Take(SecurityLimits.MaxPeersPerExchange)
            .Select(p =>
            {
                var copy = PeerRecords.Clone(p.Info);
                copy.LastSeen = p.LastSeen;
                return copy;
            })
            .ToList();
    }

    private bool IsAcceptable(PeerInfo peer)
    {
        if (!SecurityLimits.IsValidUserId(peer.UserId)) return false;
        if (!SecurityLimits.IsValidDisplayName(peer.DisplayName)) return false;
        return !string.Equals(peer.UserId, _localUserId, StringComparison.OrdinalIgnoreCase);
    }

    private void OnLanPeerDiscovered(object? sender, PeerInfo peer)
    {
        LearnPeerDirect(peer);
    }

    private void AddOrRefreshPeer(PeerInfo peer, bool direct)
    {
        // UserIds are derived from public keys. An entry carrying a key that does not hash to its UserId is forged
        // (e.g. a malicious PEX response) and must not be allowed to replace a real peer's key or address.
        if (!string.IsNullOrWhiteSpace(peer.PublicKeyPem) && !CryptoService.IsPublicKeyForUser(peer.UserId, peer.PublicKeyPem))
            return;

        var now = DateTime.UtcNow;
        var signed = !string.IsNullOrWhiteSpace(peer.Signature) && PeerRecords.IsValidlySigned(peer);

        if (_table.TryGetValue(peer.UserId, out var existing))
        {
            lock (existing)
            {
                var info = existing.Info;
                if (string.IsNullOrWhiteSpace(info.PublicKeyPem) && !string.IsNullOrWhiteSpace(peer.PublicKeyPem))
                    info.PublicKeyPem = peer.PublicKeyPem;

                if (direct)
                {
                    existing.LastSeen = now;
                    existing.LastDirectContact = now;
                    if (!string.IsNullOrWhiteSpace(peer.Address)) info.Address = peer.Address;
                    info.Port = Math.Max(0, peer.Port);
                    ApplySignature(info, signed ? peer : null);
                }
                else if (signed && peer.SignedAtUtc > (info.SignedAtUtc ?? DateTime.MinValue) && peer.SignedAtUtc > existing.LastDirectContact)
                {
                    // The peer's own statement, newer than anything we observed ourselves.
                    info.Address = peer.Address;
                    info.Port = Math.Max(0, peer.Port);
                    ApplySignature(info, peer);
                    var signedAt = Min(now, peer.SignedAtUtc!.Value);
                    if (signedAt > existing.LastSeen) existing.LastSeen = signedAt;
                }
                // Unsigned hearsay about a known peer changes nothing.

                foreach (var cap in peer.Capabilities.Take(8))
                    if (!info.Capabilities.Contains(cap))
                        info.Capabilities.Add(cap);
            }
            return;
        }

        var lastSeen = direct ? now : signed ? Min(now, peer.SignedAtUtc!.Value) : Min(now, peer.LastSeen);
        if (lastSeen < LivenessCutoff())
            return;

        if (_table.Count >= SecurityLimits.MaxRoutingTableSize)
            EvictStalestPeer();

        var record = signed ? PeerRecords.Clone(peer) : PeerRecords.Unsigned(peer);
        record.Port = Math.Max(0, record.Port);
        var routed = new RoutedPeer { Info = record, LastSeen = lastSeen, LastDirectContact = direct ? now : DateTime.MinValue };
        if (_table.TryAdd(peer.UserId, routed))
        {
            PeerAdded?.Invoke(this, record);
            _ = Task.Run(() => TryPexWithPeerAsync(record, _cts?.Token ?? CancellationToken.None));
        }
    }

    private static void ApplySignature(PeerInfo info, PeerInfo? signedRecord)
    {
        if (signedRecord != null && signedRecord.Address == info.Address && signedRecord.Port == info.Port)
        {
            info.SignedAtUtc = signedRecord.SignedAtUtc;
            info.Signature = signedRecord.Signature;
        }
        else
        {
            info.SignedAtUtc = null;
            info.Signature = string.Empty;
        }
    }

    private static DateTime Min(DateTime a, DateTime b)
    {
        return a < b ? a : b;
    }

    private static DateTime LivenessCutoff()
    {
        return DateTime.UtcNow.AddMinutes(-SecurityLimits.PeerLivenessTimeoutMinutes);
    }

    private bool CanReach(PeerInfo peer)
    {
        return IsDialable(peer) || HasSession?.Invoke(peer) == true;
    }

    private async Task TryPexWithPeerAsync(PeerInfo peer, CancellationToken ct)
    {
        if (peer.UserId.StartsWith("bootstrap:", StringComparison.OrdinalIgnoreCase) || !CanReach(peer)) return;

        try
        {
            var discovered = await _exchangeClient.FetchPeersAsync(peer, ct);
            if (discovered != null)
            {
                MarkContacted(peer.UserId);
                LearnPeers(discovered);
            }
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

    /// <summary>
    /// Re-announces to all bootstrap nodes and refreshes their peer lists now.
    /// </summary>
    public Task RefreshBootstrapNodesAsync(CancellationToken ct = default)
    {
        return BootstrapAsync(_bootstrapNodes, ct);
    }

    private async Task TryBootstrapFromNodeAsync(string nodeAddress, CancellationToken ct)
    {
        // Bootstrap node format: "host:port"
        if (!TryParseEndpoint(nodeAddress, out var host, out var port))
            return;

        try
        {
            // Register ourselves first so the bootstrap node knows our observed public address.
            var self = _selfAnnouncementProvider?.Invoke();
            if (self != null)
            {
                var result = await _exchangeClient.AnnounceWithResultAsync(host, port, self, ct);
                if (result != null)
                    SelfObserved?.Invoke(this, result);

                // A regular peer acting as bootstrap reports itself here. We just reached it at host:port,
                // which is more reliable than the address it believes it has. Only its own (first) record is
                // trusted, so a node cannot pin other users' records to its address.
                var responder = result?.Peers.FirstOrDefault();
                if (responder != null)
                {
                    var reached = PeerRecords.Clone(responder);
                    if (reached.Address != host || reached.Port != port)
                    {
                        reached = PeerRecords.Unsigned(reached);
                        reached.Address = host;
                        reached.Port = port;
                    }
                    LearnPeerDirect(reached);
                }
            }

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
                AddOrRefreshBootstrapEntry(bootstrapPeer);
            }
        }
        catch { /* node unreachable – skip silently */ }
    }

    private void AddOrRefreshBootstrapEntry(PeerInfo bootstrapPeer)
    {
        var now = DateTime.UtcNow;
        var routed = _table.GetOrAdd(bootstrapPeer.UserId, _ => new RoutedPeer { Info = bootstrapPeer, LastSeen = now, LastDirectContact = now });
        routed.LastSeen = now;
        routed.LastDirectContact = now;
        if (ReferenceEquals(routed.Info, bootstrapPeer))
            PeerAdded?.Invoke(this, bootstrapPeer);
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

                // PEX: ask a sample of reachable peers for their peer lists
                var sample = GetPeers()
                    .Where(p => !p.UserId.StartsWith("bootstrap:") && CanReach(p))
                    .OrderBy(_ => Guid.NewGuid())
                    .Take(5)
                    .ToList();

                foreach (var peer in sample)
                    await TryPexWithPeerAsync(peer, ct);

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

    /// <summary>
    /// Peers announced with port 0 are outbound-only (no listener) and cannot be connected to.
    /// </summary>
    public static bool IsDialable(PeerInfo peer)
    {
        return !string.IsNullOrWhiteSpace(peer.Address) && peer.Port is > 0 and < 65536;
    }

    internal static bool TryParseEndpoint(string nodeAddress, out string host, out int port)
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

    private class RoutedPeer
    {
        public required PeerInfo Info { get; set; }
        public DateTime LastSeen { get; set; }
        public DateTime LastDirectContact { get; set; }
    }
}
