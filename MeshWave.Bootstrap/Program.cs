using System.Collections.Concurrent;
using System.Net;
using MeshWave.Synchronizer;

namespace MeshWave.Bootstrap;

/// <summary>
/// MeshWave Bootstrap Node
/// 
/// A minimal, low-bandwidth server that helps peers discover each other.
/// It does NOT store or serve any music manifests — it only maintains a
/// routing table and answers PEX (GetPeers) requests.
///
/// Bandwidth usage is kept to a minimum:
///   - No manifest data is stored or transmitted.
///   - Peer table is capped at SecurityLimits.MaxRoutingTableSize.
///   - Each GetPeers response returns at most SecurityLimits.MaxPeersPerExchange peers.
///   - Idle connections are rejected via a short read timeout.
///
/// Usage:
///   MeshWave.Bootstrap [--port 39877] [--seeds host:port,host:port]
/// </summary>
internal class Program
{
    private static readonly ConcurrentDictionary<string, BootstrapPeerEntry> _peers = new(StringComparer.OrdinalIgnoreCase);
    private static int _requestCount;
    private static int _peerCount;

    static async Task Main(string[] args)
    {
        var (port, seeds) = ParseArgs(args);

        Console.WriteLine("=== MeshWave Bootstrap Node ===");
        Console.WriteLine($"  Listen port : {port}");
        Console.WriteLine($"  Seed nodes  : {(seeds.Count > 0 ? string.Join(", ", seeds) : "(none)")}");
        Console.WriteLine($"  Max peers   : {SecurityLimits.MaxRoutingTableSize}");
        Console.WriteLine($"  Max PEX rsp : {SecurityLimits.MaxPeersPerExchange}");
        Console.WriteLine();

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("\nShutting down…");
            cts.Cancel();
        };

        // The bootstrap server re-uses ManifestExchangeServer from the Synchronizer.
        // We pass a null manifest provider (no manifest data served) and supply our
        // routing table as the PEX peers provider.
        var server = new ManifestExchangeServer(port);
        server.ManifestReceived += OnManifestReceived;

        await server.StartAsync(
            localManifestProvider: () => null,          // no manifest — pure bootstrap
            peersProvider: GetLivePeers,
            cancellationToken: cts.Token);

        Console.WriteLine($"Listening on port {port}. Press Ctrl+C to stop.\n");

        // Bootstrap: seed our own table from any configured seeds
        if (seeds.Count > 0)
            await SeedFromNodesAsync(seeds, port, cts.Token);

        // Status loop — low-cost console heartbeat every 60 s
        var statusTask = StatusLoopAsync(cts.Token);

        await Task.WhenAny(
            Task.Delay(Timeout.Infinite, cts.Token),
            statusTask);

        await server.StopAsync();
        Console.WriteLine("Bootstrap node stopped.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Peer table helpers
    // ──────────────────────────────────────────────────────────────────────

    private static IReadOnlyList<PeerInfo> GetLivePeers()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-10);
        return _peers.Values
            .Where(e => e.LastSeen >= cutoff)
            .OrderByDescending(e => e.LastSeen)
            .Take(SecurityLimits.MaxPeersPerExchange)
            .Select(e => e.Peer)
            .ToList();
    }

    private static void RegisterPeer(PeerInfo peer)
    {
        if (!SecurityLimits.IsValidUserId(peer.UserId)) return;
        if (!SecurityLimits.IsValidDisplayName(peer.DisplayName)) return;

        if (_peers.TryGetValue(peer.UserId, out var existing))
        {
            existing.LastSeen = DateTime.UtcNow;
        }
        else
        {
            // Evict stale entries if at cap
            if (_peers.Count >= SecurityLimits.MaxRoutingTableSize)
                EvictStalest();

            var entry = new BootstrapPeerEntry { Peer = peer, LastSeen = DateTime.UtcNow };
            if (_peers.TryAdd(peer.UserId, entry))
            {
                Interlocked.Increment(ref _peerCount);
                Console.WriteLine($"[+] Peer registered: {peer.DisplayName,-24} {peer.Address}:{peer.Port}  (total {_peers.Count})");
            }
        }
    }

    private static void EvictStalest()
    {
        var stalest = _peers.Values.OrderBy(e => e.LastSeen).FirstOrDefault();
        if (stalest != null)
            _peers.TryRemove(stalest.Peer.UserId, out _);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Incoming manifest — extract peer info but discard the manifest content
    // ──────────────────────────────────────────────────────────────────────

    private static void OnManifestReceived(object? sender, ManifestReceivedEventArgs e)
    {
        Interlocked.Increment(ref _requestCount);

        // A peer pushed its manifest so we know it's alive and reachable.
        // We only learn UserId + DisplayName + address from it; we never store the ops.
        var manifest = e.Manifest;
        if (manifest == null) return;

        // Parse the remote address into a PeerInfo so it can be shared via PEX
        if (!IPAddress.TryParse(e.PeerAddress, out _)) return;

        var peer = new PeerInfo
        {
            UserId      = manifest.UserId,
            DisplayName = SecurityLimits.Truncate(manifest.UserId, SecurityLimits.MaxDisplayNameLength),
            Address     = e.PeerAddress,
            Port        = ManifestExchangeServer.DefaultPort,
            LastSeen    = DateTime.UtcNow
        };

        RegisterPeer(peer);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Seed from other known bootstrap nodes
    // ──────────────────────────────────────────────────────────────────────

    private static async Task SeedFromNodesAsync(List<string> seeds, int localPort, CancellationToken ct)
    {
        var client = new ManifestExchangeClient(timeoutMs: 5_000);
        foreach (var seed in seeds.Take(SecurityLimits.MaxBootstrapNodes))
        {
            try
            {
                var (host, port) = ParseEndpoint(seed, localPort);
                var peers = await client.FetchPeersAsync(host, port, ct);
                foreach (var p in peers)
                    RegisterPeer(p);

                Console.WriteLine($"[seed] Learned {peers.Count} peers from {seed}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[seed] Could not reach {seed}: {ex.Message}");
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Periodic status output
    // ──────────────────────────────────────────────────────────────────────

    private static async Task StatusLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), ct);
                int live = GetLivePeers().Count;
                Console.WriteLine($"[status] {DateTime.UtcNow:HH:mm:ss}  live={live}/{_peers.Count}  total-requests={_requestCount}  total-registered={_peerCount}");
            }
            catch (OperationCanceledException) { break; }
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Argument parsing
    // ──────────────────────────────────────────────────────────────────────

    private static (int port, List<string> seeds) ParseArgs(string[] args)
    {
        int port = ManifestExchangeServer.DefaultPort;
        var seeds = new List<string>();

        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--port" && int.TryParse(args[i + 1], out var p))
                port = p;
            else if (args[i] == "--seeds")
                seeds.AddRange(args[i + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        return (port, seeds);
    }

    private static (string host, int port) ParseEndpoint(string endpoint, int defaultPort)
    {
        var lastColon = endpoint.LastIndexOf(':');
        if (lastColon > 0 && int.TryParse(endpoint[(lastColon + 1)..], out var p))
            return (endpoint[..lastColon], p);
        return (endpoint, defaultPort);
    }
}

internal class BootstrapPeerEntry
{
    public required PeerInfo Peer { get; set; }
    public DateTime LastSeen { get; set; }
}
