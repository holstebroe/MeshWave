using System.Security.Cryptography;
using System.Text.Json;
using MeshWave.Common.Core.Models;
using MeshWave.Synchronizer;
using MeshWave.TestUtilities;

namespace MeshWave.NatLab;

/// <summary>
/// A minimal, headless MeshWave peer used by the NAT-realistic test harness (P2P protocol review H3). It runs the
/// same production peer wiring as the WPF app and the in-process integration tests (<see cref="TestPeerFactory"/>),
/// but as a standalone process so it can be launched inside a separate Linux network namespace
/// (<c>ip netns exec &lt;ns&gt; dotnet MeshWave.NatLab.dll ...</c>) behind a simulated NAT, instead of always
/// running on loopback in the same process as every other peer.
///
/// Usage:
///   MeshWave.NatLab --role server --name alice --bootstrap 10.0.0.1:39877 --timeout 60
///   MeshWave.NatLab --role client --name bob   --bootstrap 10.0.0.1:39877 --timeout 60
///
/// A server publishes a signed Track Create operation for a random in-memory payload and serves it. A client waits
/// for the mesh to converge, discovers the server's track, and downloads it, verifying the SHA-256 content hash
/// (F2) end to end across whatever connection path the two peers actually established (persistent session, direct
/// TCP, or a UDP hole-punched session — see <see cref="PeerConnectionAttemptReport"/>). The result, including which
/// path succeeded, is printed as one line prefixed "NATLAB_RESULT:" so the shell harness can pick it out from the
/// normal peer log lines that surround it.
/// </summary>
internal static class Program
{
    private const string TrackId = "natlab-track";
    private const int DefaultContentSizeBytes = 256 * 1024;

    private static async Task<int> Main(string[] args)
    {
        var options = Options.Parse(args);
        if (options == null)
        {
            Console.Error.WriteLine("Usage: MeshWave.NatLab --role server|client --name <name> --bootstrap <host:port> [--timeout <seconds>] [--content-size <bytes>]");
            return 2;
        }

        var peer = TestPeerFactory.CreatePeer(options.Name);
        try
        {
            return options.Role == Role.Server
                ? await RunServerAsync(peer, options)
                : await RunClientAsync(peer, options);
        }
        finally
        {
            await peer.DisposeAsync();
        }
    }

    private static async Task<int> RunServerAsync(TestPeer peer, Options options)
    {
        var payload = new byte[options.ContentSizeBytes];
        new Random(1234).NextBytes(payload);
        var contentHash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

        await peer.StartAsync(bootstrapNodes: [options.Bootstrap], actAsListener: true,
            contentProvider: h => string.Equals(h, contentHash, StringComparison.OrdinalIgnoreCase) ? payload : null);
        peer.BroadcastProfile(options.Name, isArtist: true);
        peer.AnnounceTrack(TrackId, contentHash, new Dictionary<string, string> { ["title"] = TrackId });

        Console.WriteLine($"[natlab] server '{options.Name}' publishing hash={contentHash} size={payload.Length}");

        var deadline = DateTime.UtcNow.AddSeconds(options.TimeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            await peer.SyncAsync();
            await Task.Delay(1000);
        }

        EmitResult(new Result(true, "server", options.Name, "served for the configured window", contentHash, []));
        return 0;
    }

    private static async Task<int> RunClientAsync(TestPeer peer, Options options)
    {
        await peer.StartAsync(bootstrapNodes: [options.Bootstrap], actAsListener: true);
        peer.BroadcastProfile(options.Name, isArtist: true);

        var deadline = DateTime.UtcNow.AddSeconds(options.TimeoutSeconds);
        string? authorUserId = null;
        string? contentHash = null;

        while (DateTime.UtcNow < deadline && authorUserId == null)
        {
            await peer.SyncAsync();

            foreach (var manifest in peer.Orchestrator.PeerManifests)
            {
                var op = manifest.Operations.FirstOrDefault(o =>
                    o.OperationType == ManifestOperationType.Create && o.TargetType == "Track" && !string.IsNullOrWhiteSpace(o.ContentHash));
                if (op == null) continue;

                authorUserId = manifest.UserId;
                contentHash = op.ContentHash;
                break;
            }

            if (authorUserId == null)
                await Task.Delay(1000);
        }

        if (authorUserId == null || contentHash == null)
        {
            EmitResult(new Result(false, "client", options.Name, "never discovered the server's track within the timeout", null, []));
            return 1;
        }

        Console.WriteLine($"[natlab] client '{options.Name}' discovered author={authorUserId} hash={contentHash}, requesting content...");

        var bytes = await peer.Orchestrator.RequestContentAsync(authorUserId, contentHash);
        var report = peer.Orchestrator.LastConnectionAttemptReport;
        var attempts = report?.Attempts.Select(a => $"{a.Method}={(a.Success ? "ok" : "fail")}").ToList() ?? [];

        var success = bytes != null;
        EmitResult(new Result(
            success,
            "client",
            options.Name,
            success ? $"downloaded {bytes!.Length} bytes and verified the SHA-256 hash" : "download failed: " + (report?.BuildUserFacingSummary() ?? "no report"),
            contentHash,
            attempts));

        return success ? 0 : 1;
    }

    private static void EmitResult(Result result)
    {
        Console.WriteLine("NATLAB_RESULT:" + JsonSerializer.Serialize(result));
    }

    private sealed record Result(bool Success, string Role, string Name, string Details, string? ContentHash, List<string> ConnectionAttempts);

    private enum Role { Server, Client }

    private sealed record Options(Role Role, string Name, string Bootstrap, int TimeoutSeconds, int ContentSizeBytes)
    {
        public static Options? Parse(string[] args)
        {
            string? role = null, name = null, bootstrap = null;
            var timeoutSeconds = 60;
            var contentSize = DefaultContentSizeBytes;

            for (var i = 0; i < args.Length - 1; i++)
                switch (args[i])
                {
                    case "--role": role = args[++i]; break;
                    case "--name": name = args[++i]; break;
                    case "--bootstrap": bootstrap = args[++i]; break;
                    case "--timeout": int.TryParse(args[++i], out timeoutSeconds); break;
                    case "--content-size": int.TryParse(args[++i], out contentSize); break;
                }

            if (name == null || bootstrap == null) return null;
            if (!string.Equals(role, "server", StringComparison.OrdinalIgnoreCase) && !string.Equals(role, "client", StringComparison.OrdinalIgnoreCase))
                return null;

            return new Options(
                string.Equals(role, "server", StringComparison.OrdinalIgnoreCase) ? Role.Server : Role.Client,
                name, bootstrap, timeoutSeconds, contentSize);
        }
    }
}
