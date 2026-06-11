using MeshWave.Common.Core;
using MeshWave.Bootstrap.Core;
using MeshWave.Synchronizer;
using NLog;

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
    private static BootstrapCoordinator? _coordinator;

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

        var logger = LogManager.GetCurrentClassLogger();
        _coordinator = new BootstrapCoordinator(port, logger);
        _coordinator.PeerRegistered += (_, e) =>
            Console.WriteLine($"[peer+] {DateTime.UtcNow:HH:mm:ss} user={e.Peer.UserId} addr={e.Peer.Address}:{e.Peer.Port} reason={e.Reason}");
        _coordinator.PeerRefreshed += (_, e) =>
            Console.WriteLine($"[peer~] {DateTime.UtcNow:HH:mm:ss} user={e.Peer.UserId} addr={e.Peer.Address}:{e.Peer.Port} reason={e.Reason}");
        _coordinator.PeerDisconnected += (_, e) =>
            Console.WriteLine($"[peer-] {DateTime.UtcNow:HH:mm:ss} user={e.Peer.UserId} addr={e.Peer.Address}:{e.Peer.Port} reason={e.Reason}");

        await _coordinator.StartAsync(cts.Token);

        Console.WriteLine($"Listening on port {port}. Press Ctrl+C to stop.\n");

        // Bootstrap: seed our own table from any configured seeds
        if (seeds.Count > 0)
            await _coordinator.SeedFromNodesAsync(seeds, cts.Token);

        // Status loop — low-cost console heartbeat every 60 s
        var statusTask = StatusLoopAsync(cts.Token);

        await Task.WhenAny(
            Task.Delay(Timeout.Infinite, cts.Token),
            statusTask);

        await _coordinator.StopAsync();
        _coordinator.Dispose();
        Console.WriteLine("Bootstrap node stopped.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Periodic status output
    // ──────────────────────────────────────────────────────────────────────

    private static async Task StatusLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), ct);
                var coordinator = _coordinator;
                var live = coordinator?.GetLivePeers().Count ?? 0;
                var total = coordinator?.RegisteredPeerCount ?? 0;
                var requests = coordinator?.RequestCount ?? 0;
                Console.WriteLine($"[status] {DateTime.UtcNow:HH:mm:ss}  live={live}/{total}  total-requests={requests}");
            }
            catch (OperationCanceledException) { break; }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Argument parsing
    // ──────────────────────────────────────────────────────────────────────

    private static (int port, List<string> seeds) ParseArgs(string[] args)
    {
        var port = ManifestExchangeServer.DefaultPort;
        var seeds = new List<string>();

        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == "--port" && int.TryParse(args[i + 1], out var p))
                port = p;
            else if (args[i] == "--seeds")
                seeds.AddRange(args[i + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

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

