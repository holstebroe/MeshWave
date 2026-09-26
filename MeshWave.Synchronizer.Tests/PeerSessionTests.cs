using System.Net;
using System.Net.Sockets;
using MeshWave.Common.Core.Crypto;
using MeshWave.Common.Core.Models;
using MeshWave.Common.Core.P2P;
using Xunit;

namespace MeshWave.Synchronizer.Tests;

/// <summary>
/// Persistent sessions (C1), introductions and hole punching (C2) and the Announce dial-back (C5).
/// </summary>
public class PeerSessionTests : IAsyncDisposable
{
    private readonly List<TestNode> _nodes = [];

    [Fact]
    public async Task TcpSession_AuthenticatesBothSides_AndCarriesRequestsBothWays()
    {
        var alice = CreateNode("Alice", listen: true);
        var bob = CreateNode("Bob", listen: false);
        await alice.StartAsync();
        await bob.StartAsync();

        var session = await bob.Sessions.ConnectTcpAsync("127.0.0.1", alice.Port, TestContext.Current.CancellationToken);
        Assert.NotNull(session);

        var aliceSide = await WaitForSessionAsync(alice, bob.Identity.UserId);
        var bobSide = await WaitForSessionAsync(bob, alice.Identity.UserId);
        Assert.Equal(alice.Identity.UserId, bobSide.RemoteUserId);
        Assert.Equal(bob.Identity.UserId, aliceSide.RemoteUserId);

        // Bob has no listener, yet Alice can request his manifest over the session he opened.
        var response = await aliceSide.RequestAsync(new ManifestRequest { Type = ManifestRequestType.GetManifest, StreamType = ManifestStreamType.Content }, 5000, TestContext.Current.CancellationToken);
        Assert.Equal(bob.Identity.UserId, response?.Manifest?.UserId);

        // And Bob can request Alice's through the client, which routes over the session.
        var fetched = await bob.Client.FetchManifestAsync(new PeerInfo { UserId = alice.Identity.UserId, DisplayName = "Alice", Address = "127.0.0.1", Port = 0 },
            new NullManifestStore(), ManifestStreamType.Content, TestContext.Current.CancellationToken);
        Assert.Equal(alice.Identity.UserId, fetched?.UserId);
    }

    [Fact]
    public async Task TcpSessions_DialledFromBothSides_AreDeduplicatedToTheSameSession()
    {
        var alice = CreateNode("Alice", listen: true);
        var bob = CreateNode("Bob", listen: true);
        await alice.StartAsync();
        await bob.StartAsync();

        await Task.WhenAll(
            alice.Sessions.ConnectTcpAsync("127.0.0.1", bob.Port, TestContext.Current.CancellationToken),
            bob.Sessions.ConnectTcpAsync("127.0.0.1", alice.Port, TestContext.Current.CancellationToken));

        await WaitUntilAsync(() =>
            alice.Sessions.Sessions.Count(s => s.IsAuthenticated) == 1 && bob.Sessions.Sessions.Count(s => s.IsAuthenticated) == 1);

        var aliceSide = alice.Sessions.GetSession(bob.Identity.UserId)!;
        var bobSide = bob.Sessions.GetSession(alice.Identity.UserId)!;
        Assert.Equal(aliceSide.Rank, bobSide.Rank);
    }

    [Fact]
    public async Task Introduction_LetsTwoPeersWithoutOpenPorts_TalkDirectlyOverUdp()
    {
        var introducer = CreateNode("Alice", listen: true);
        var bob = CreateNode("Bob", listen: false);
        var carol = CreateNode("Carol", listen: false);
        await introducer.StartAsync();
        await bob.StartAsync();
        await carol.StartAsync();

        Assert.NotNull(await bob.Sessions.ConnectTcpAsync("127.0.0.1", introducer.Port, TestContext.Current.CancellationToken));
        Assert.NotNull(await carol.Sessions.ConnectTcpAsync("127.0.0.1", introducer.Port, TestContext.Current.CancellationToken));
        await WaitForSessionAsync(introducer, carol.Identity.UserId);

        var session = await bob.Sessions.RequestIntroductionAsync(carol.Identity.UserId, TestContext.Current.CancellationToken);

        Assert.NotNull(session);
        Assert.Equal("udp", session!.TransportKind);
        var carolSide = await WaitForSessionAsync(carol, bob.Identity.UserId);
        Assert.Equal("udp", carolSide.TransportKind);

        // Large messages are fragmented and reassembled by the reliable UDP channel.
        var response = await session.RequestAsync(new ManifestRequest { Type = ManifestRequestType.GetManifest, StreamType = ManifestStreamType.Content }, 10000, TestContext.Current.CancellationToken);
        Assert.Equal(carol.Identity.UserId, response?.Manifest?.UserId);
        Assert.Equal(carol.LocalManifest.Operations.Count, response!.Manifest!.Operations.Count);
    }

    [Fact]
    public async Task Introduction_IsRefused_WhenTheIntroducerHasNoSessionWithTheTarget()
    {
        var introducer = CreateNode("Alice", listen: true);
        var bob = CreateNode("Bob", listen: false);
        var carol = CreateNode("Carol", listen: false);
        await introducer.StartAsync();
        await bob.StartAsync();
        await carol.StartAsync();

        Assert.NotNull(await bob.Sessions.ConnectTcpAsync("127.0.0.1", introducer.Port, TestContext.Current.CancellationToken));

        var session = await bob.Sessions.RequestIntroductionAsync(carol.Identity.UserId, TestContext.Current.CancellationToken);
        Assert.Null(session);
    }

    [Fact]
    public async Task Announce_WithUnreachablePort_IsRegisteredAsOutboundOnly()
    {
        var node = CreateNode("Alice", listen: true);
        await node.StartAsync();
        PeerInfo? announced = null;
        node.Server.PeerAnnounced += (_, e) => announced = e.Peer;

        var (priv, pub) = CryptoService.GenerateKeyPair();
        var closedPort = FindFreePort();
        var self = PeerRecords.Sign(new PeerInfo
        {
            UserId = CryptoService.DeriveUserIdFromPublicKey(pub),
            DisplayName = "Bob",
            Address = "127.0.0.1",
            Port = closedPort,
            PublicKeyPem = pub
        }, priv, DateTime.UtcNow);

        var result = await new ManifestExchangeClient(5000).AnnounceWithResultAsync("127.0.0.1", node.Port, self, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("127.0.0.1", result!.ObservedAddress);
        Assert.False(result.DialBackSucceeded);
        Assert.NotNull(announced);
        Assert.Equal(0, announced!.Port);
        Assert.Empty(announced.Signature); // the owner's signature no longer describes the registered port
    }

    [Fact]
    public async Task Announce_WithReachablePort_KeepsPortAndSignature()
    {
        var node = CreateNode("Alice", listen: true);
        var bob = CreateNode("Bob", listen: true);
        await node.StartAsync();
        await bob.StartAsync();
        PeerInfo? announced = null;
        node.Server.PeerAnnounced += (_, e) => announced = e.Peer;

        var self = PeerRecords.Sign(new PeerInfo
        {
            UserId = bob.Identity.UserId,
            DisplayName = "Bob",
            Address = "127.0.0.1",
            Port = bob.Port,
            PublicKeyPem = bob.Identity.PublicKeyPem
        }, bob.Identity.PrivateKeyPem, DateTime.UtcNow);

        var result = await new ManifestExchangeClient(5000).AnnounceWithResultAsync("127.0.0.1", node.Port, self, TestContext.Current.CancellationToken);

        Assert.True(result?.DialBackSucceeded);
        Assert.Equal(bob.Port, announced?.Port);
        Assert.True(PeerRecords.IsValidlySigned(announced!));
    }

    private async Task<PeerSession> WaitForSessionAsync(TestNode node, string userId)
    {
        PeerSession? session = null;
        await WaitUntilAsync(() => (session = node.Sessions.GetSession(userId)) != null);
        return session!;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 15000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Condition not met in time.");
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }

    private TestNode CreateNode(string name, bool listen)
    {
        var node = new TestNode(name, listen);
        _nodes.Add(node);
        return node;
    }

    private static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var node in _nodes) await node.DisposeAsync();
    }

    private sealed class TestNode : IAsyncDisposable
    {
        private readonly bool _listen;

        public TestNode(string name, bool listen)
        {
            _listen = listen;
            var (priv, pub) = CryptoService.GenerateKeyPair();
            Port = FindFreePort();
            Identity = new LocalPeerIdentity
            {
                UserId = CryptoService.DeriveUserIdFromPublicKey(pub),
                DisplayName = name,
                PublicKeyPem = pub,
                PrivateKeyPem = priv,
                ManifestPort = Port
            };

            var manager = new ManifestManager();
            LocalManifest = manager.CreateManifest(Identity.UserId);
            LocalManifest.StreamType = ManifestStreamType.Content;
            for (var i = 0; i < 50; i++)
                manager.AppendSignedOperation(LocalManifest, ManifestOperationType.Create, $"{name}-track-{i}", "Track", $"hash-{i}",
                    new Dictionary<string, string> { ["title"] = new string('x', 200) }, priv);

            Server = new ManifestExchangeServer(Port);
            Sessions = new PeerSessionManager();
            Client = new ManifestExchangeClient(5000) { SessionResolver = Sessions.Resolve };
        }

        public int Port { get; }
        public LocalPeerIdentity Identity { get; }
        public Manifest LocalManifest { get; }
        public ManifestExchangeServer Server { get; }
        public PeerSessionManager Sessions { get; }
        public ManifestExchangeClient Client { get; }

        public async Task StartAsync()
        {
            var self = PeerRecords.Sign(new PeerInfo
            {
                UserId = Identity.UserId,
                DisplayName = Identity.DisplayName,
                Address = "127.0.0.1",
                Port = _listen ? Port : 0,
                PublicKeyPem = Identity.PublicKeyPem
            }, Identity.PrivateKeyPem, DateTime.UtcNow);

            Server.Configure(st => st == ManifestStreamType.Content ? LocalManifest : null, selfInfoProvider: () => self);
            Sessions.Configure(Identity, () => self, (request, address, ct) => Server.HandleRequestAsync(request, address, inlineContent: true, ct), isIntroducer: _listen);
            Sessions.Start(Port);

            if (_listen)
            {
                Server.SessionUpgradeHandler = Sessions.AcceptTcpSession;
                await Server.StartAsync(st => st == ManifestStreamType.Content ? LocalManifest : null, selfInfoProvider: () => self);
            }
        }

        public async ValueTask DisposeAsync()
        {
            Sessions.Dispose();
            await Server.StopAsync();
            Server.Dispose();
        }
    }

    private sealed class NullManifestStore : IManifestStore
    {
        public Manifest? Get(string userId, ManifestStreamType streamType = ManifestStreamType.Content) => null;
        public IReadOnlyCollection<Manifest> GetAll() => [];
        public int MergeAndSave(Manifest remote, string publicKeyPem, ManifestManager manager) => 0;
        public void LoadAll() { }
        public void Remove(string userId) { }
        public void ClearAll() { }
    }
}
