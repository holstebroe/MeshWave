using MeshWave.Common.Core;
using MeshWave.Common.Core.Crypto;
using MeshWave.Common.Core.P2P;
using Xunit;

namespace MeshWave.Synchronizer.Tests;

/// <summary>
/// Routing-table liveness and address authenticity (C6): only direct contact or the peer's own signed record may keep
/// a peer alive or change its address; relayed (PEX) hearsay may only introduce unknown peers.
/// </summary>
public class PeerRouterLivenessTests
{
    private static readonly Lazy<(string Private, string Public, string UserId)> Key = new(() =>
    {
        var (priv, pub) = CryptoService.GenerateSigningKeyPair();
        return (priv, pub, CryptoService.DeriveUserIdFromPublicKey(pub));
    });

    private static PeerRouter CreateRouter()
    {
        return new PeerRouter(new PeerDiscovery(), new ManifestExchangeClient(timeoutMs: 100));
    }

    private static PeerInfo Record(string address, int port, DateTime? lastSeen = null)
    {
        return new PeerInfo
        {
            UserId = Key.Value.UserId,
            DisplayName = "Carol",
            Address = address,
            Port = port,
            PublicKey = Key.Value.Public,
            LastSeen = lastSeen ?? DateTime.UtcNow
        };
    }

    [Fact]
    public void LivenessCutoff_IsLongerThanTheBootstrapHeartbeat()
    {
        Assert.True(SecurityLimits.PeerLivenessTimeoutMinutes > SecurityLimits.BootstrapRetryIntervalMinutes);
        Assert.True(SecurityLimits.PeerRecordResignMinutes < SecurityLimits.BootstrapRetryIntervalMinutes);
    }

    [Fact]
    public void UnsignedHearsay_CannotChangeTheAddressOfAKnownPeer()
    {
        using var router = CreateRouter();
        router.LearnPeerDirect(Record("10.0.0.1", 0));

        router.LearnPeers([Record("6.6.6.6", 666)]);

        var peer = Assert.Single(router.GetPeers());
        Assert.Equal("10.0.0.1", peer.Address);
        Assert.Equal(0, peer.Port);
    }

    [Fact]
    public async Task NewerSignedRecord_UpdatesTheAddressOfAKnownPeer()
    {
        using var router = CreateRouter();
        router.LearnPeerDirect(Record("10.0.0.1", 0));
        await Task.Delay(20, TestContext.Current.CancellationToken);

        router.LearnPeers([PeerRecords.Sign(Record("10.0.0.2", 0), Key.Value.Private, DateTime.UtcNow)]);

        var peer = Assert.Single(router.GetPeers());
        Assert.Equal("10.0.0.2", peer.Address);
        Assert.True(PeerRecords.IsValidlySigned(peer));
    }

    [Fact]
    public async Task TamperedSignedRecord_IsTreatedAsUnsigned()
    {
        using var router = CreateRouter();
        router.LearnPeerDirect(Record("10.0.0.1", 0));
        await Task.Delay(20, TestContext.Current.CancellationToken);

        var forged = PeerRecords.Sign(Record("10.0.0.2", 0), Key.Value.Private, DateTime.UtcNow);
        forged.Address = "6.6.6.6";
        router.LearnPeers([forged]);

        Assert.Equal("10.0.0.1", Assert.Single(router.GetPeers()).Address);
    }

    [Fact]
    public void Hearsay_DoesNotRefreshLastSeen()
    {
        using var router = CreateRouter();
        var elevenMinutesAgo = DateTime.UtcNow.AddMinutes(-11);
        router.LearnPeers([Record("10.0.0.1", 0, elevenMinutesAgo)]);

        // Another node claims it saw the peer just now; without the peer's signature that changes nothing.
        router.LearnPeers([Record("10.0.0.1", 0, DateTime.UtcNow)]);

        var peer = Assert.Single(router.GetPeersForExchange());
        Assert.True(peer.LastSeen < DateTime.UtcNow.AddMinutes(-10));
    }

    [Fact]
    public void StaleHearsay_DoesNotAddAPeer()
    {
        using var router = CreateRouter();
        router.LearnPeers([Record("10.0.0.1", 0, DateTime.UtcNow.AddMinutes(-(SecurityLimits.PeerLivenessTimeoutMinutes + 1)))]);
        Assert.Empty(router.GetPeers());
    }

    [Fact]
    public void DirectContact_RefreshesLastSeen()
    {
        using var router = CreateRouter();
        router.LearnPeers([Record("10.0.0.1", 0, DateTime.UtcNow.AddMinutes(-11))]);

        router.MarkContacted(Key.Value.UserId);

        var peer = Assert.Single(router.GetPeersForExchange());
        Assert.True(peer.LastSeen > DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public void PeerRecords_SignatureCoversAddressPortAndTime()
    {
        var signed = PeerRecords.Sign(Record("10.0.0.1", 4000), Key.Value.Private, DateTime.UtcNow);
        Assert.True(PeerRecords.IsValidlySigned(signed));

        var otherPort = PeerRecords.Clone(signed);
        otherPort.Port = 4001;
        Assert.False(PeerRecords.IsValidlySigned(otherPort));

        var otherTime = PeerRecords.Clone(signed);
        otherTime.SignedAtUtc = signed.SignedAtUtc!.Value.AddSeconds(-1);
        Assert.False(PeerRecords.IsValidlySigned(otherTime));

        var future = PeerRecords.Sign(Record("10.0.0.1", 4000), Key.Value.Private, DateTime.UtcNow.AddHours(1));
        Assert.False(PeerRecords.IsValidlySigned(future));
    }

    [Fact]
    public void PeerRecords_SurviveProtobufRoundTrip()
    {
        var signed = PeerRecords.Sign(Record("10.0.0.1", 4000), Key.Value.Private, DateTime.UtcNow);
        var bytes = MeshWave.Common.Core.Serialization.ManifestSerializer.SerializeResponse(new ManifestResponse { Peers = [signed] });
        var roundTripped = MeshWave.Common.Core.Serialization.ManifestSerializer.DeserializeResponse(bytes).Peers.Single();

        Assert.True(PeerRecords.IsValidlySigned(roundTripped));
    }

    [Fact]
    public void SessionProof_OnlyVerifiesForTheSignedNonce()
    {
        var proof = PeerRecords.SignSessionNonce(Key.Value.UserId, 42, Key.Value.Private);

        Assert.True(PeerRecords.VerifySessionNonce(Key.Value.UserId, 42, proof, Key.Value.Public));
        Assert.False(PeerRecords.VerifySessionNonce(Key.Value.UserId, 43, proof, Key.Value.Public));
    }
}
