using System.Net;
using System.Net.Sockets;
using MeshWave.Common.Core.Crypto;
using MeshWave.Common.Core.Models;
using MeshWave.Common.Core.Storage;
using MeshWave.Synchronizer;

namespace MeshWave.TestUtilities;

public static class TestPeerFactory
{
    public static TestPeer CreatePeer(string name)
    {
        var port = FindFreePort();
        var tempDir = Path.Combine(Path.GetTempPath(), $"mw_test_{name}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var discovery = new NullPeerDiscovery(); // Typically want to avoid UDP broadcast in tests
        var peerRouter = new PeerRouter(lanDiscovery: discovery);
        var server = new ManifestExchangeServer(port);
        var client = new ManifestExchangeClient(timeoutMs: 2000);
        var mgr = new ManifestManager();
        var userRepo = new UserRepository(tempDir);
        var store = PeerManifestStore.CreateAtBase(tempDir);

        var orchestrator = new SyncOrchestrator(peerRouter, server, client, mgr, store, userRepository: userRepo);

        var (privKey, pubKey) = CryptoService.GenerateKeyPair();
        var userId = CryptoService.DeriveUserIdFromPublicKey(pubKey);

        var identity = new LocalPeerIdentity
        {
            UserId = userId,
            DisplayName = name,
            PublicKeyPem = pubKey,
            PrivateKeyPem = privKey,
            ManifestPort = port
        };

        return new TestPeer(name, tempDir, port, orchestrator, identity);
    }

    public static void InitializeWithTestData(TestPeer peer, string sourceUserTestDataName)
    {
        var testDataRoot = FindTestDataPath();
        var sourceDir = Path.Combine(testDataRoot, sourceUserTestDataName);
        if (!Directory.Exists(sourceDir))
            throw new DirectoryNotFoundException($"Test data for user {sourceUserTestDataName} not found at {sourceDir}");

        // For simplicity, we just copy everything to the peer's base directory as if it was their music folder
        // In a real scenario, we might want to be more specific about where it goes.
        CopyDirectory(sourceDir, peer.BaseDir);
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDir, file);
            var destFile = Path.Combine(destinationDir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
            File.Copy(file, destFile, true);
        }
    }

    private static readonly HashSet<int> _usedPorts = new();
    private static readonly object _portLock = new();

    private static string FindTestDataPath()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10; i++)
        {
            var candidate = Path.Combine(dir, "TestData");
            if (Directory.Exists(candidate))
                return candidate;

            var parent = Path.GetDirectoryName(dir);
            if (parent == null || parent == dir) break;
            dir = parent;
        }

        // Try from current working directory as well
        dir = Directory.GetCurrentDirectory();
        for (var i = 0; i < 10; i++)
        {
            var candidate = Path.Combine(dir, "TestData");
            if (Directory.Exists(candidate))
                return candidate;

            var parent = Path.GetDirectoryName(dir);
            if (parent == null || parent == dir) break;
            dir = parent;
        }

        throw new DirectoryNotFoundException("TestData directory not found.");
    }

    public static int FindFreePort()
    {
        lock (_portLock)
        {
            int port;
            int retry = 0;
            while (true)
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                port = ((IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();

                if (!_usedPorts.Contains(port))
                {
                    _usedPorts.Add(port);
                    return port;
                }

                if (++retry > 100) throw new Exception("Could not find a free port after 100 retries.");
            }
        }
    }
}

public class NullPeerDiscovery : PeerDiscovery
{
    public override Task StartDiscoveryAsync(LocalPeerIdentity identity, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public override Task StopDiscoveryAsync() => Task.CompletedTask;
}
