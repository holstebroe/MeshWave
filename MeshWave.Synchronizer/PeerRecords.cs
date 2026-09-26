using System.Globalization;
using MeshWave.Common.Core.Crypto;
using MeshWave.Common.Core.P2P;

namespace MeshWave.Synchronizer;

/// <summary>
/// Signing and verification of self-published <see cref="PeerInfo"/> records and session handshake proofs.
/// </summary>
public static class PeerRecords
{
    /// <summary>Signed records dated further in the future than this are rejected (clock skew allowance).</summary>
    private static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Returns a copy of <paramref name="peer"/> signed with <paramref name="privateKey"/> at <paramref name="signedAtUtc"/>.
    /// </summary>
    public static PeerInfo Sign(PeerInfo peer, string privateKey, DateTime signedAtUtc)
    {
        var signedAt = TruncateToMilliseconds(signedAtUtc);
        var copy = Clone(peer);
        copy.SignedAtUtc = signedAt;
        copy.LastSeen = signedAt;
        copy.Signature = CryptoService.SignData(CanonicalRecord(copy.UserId, copy.Address, copy.Port, copy.EncryptionPublicKey, signedAt), privateKey);
        return copy;
    }

    /// <summary>
    /// True when the record is signed by the key that owns its UserId, and is not dated in the future.
    /// </summary>
    public static bool IsValidlySigned(PeerInfo peer)
    {
        if (string.IsNullOrWhiteSpace(peer.Signature) || peer.SignedAtUtc == null)
            return false;
        if (peer.SignedAtUtc.Value > DateTime.UtcNow + MaxClockSkew)
            return false;
        if (!CryptoService.IsPublicKeyForUser(peer.UserId, peer.PublicKey))
            return false;

        var data = CanonicalRecord(peer.UserId, peer.Address, peer.Port, peer.EncryptionPublicKey, peer.SignedAtUtc.Value);
        return CryptoService.VerifySignature(data, peer.Signature, peer.PublicKey);
    }

    /// <summary>Signs a session nonce to prove ownership of <paramref name="userId"/>'s key to the peer that chose the nonce.</summary>
    public static string SignSessionNonce(string userId, long nonce, string privateKey)
    {
        return CryptoService.SignData(SessionProofData(userId, nonce), privateKey);
    }

    public static bool VerifySessionNonce(string userId, long nonce, string proof, string publicKey)
    {
        if (string.IsNullOrWhiteSpace(proof) || !CryptoService.IsPublicKeyForUser(userId, publicKey))
            return false;
        return CryptoService.VerifySignature(SessionProofData(userId, nonce), proof, publicKey);
    }

    public static PeerInfo Clone(PeerInfo peer)
    {
        return new PeerInfo
        {
            UserId = peer.UserId,
            DisplayName = peer.DisplayName,
            Address = peer.Address,
            Port = peer.Port,
            PublicKey = peer.PublicKey,
            EncryptionPublicKey = peer.EncryptionPublicKey,
            LastSeen = peer.LastSeen,
            Capabilities = peer.Capabilities.ToList(),
            SignedAtUtc = peer.SignedAtUtc,
            Signature = peer.Signature
        };
    }

    /// <summary>Returns a copy without signature, e.g. after a relaying node changed the address to the one it observed.</summary>
    public static PeerInfo Unsigned(PeerInfo peer)
    {
        var copy = Clone(peer);
        copy.SignedAtUtc = null;
        copy.Signature = string.Empty;
        return copy;
    }

    private static string CanonicalRecord(string userId, string address, int port, string encryptionPublicKey, DateTime signedAtUtc)
    {
        var ms = new DateTimeOffset(DateTime.SpecifyKind(signedAtUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
        return string.Create(CultureInfo.InvariantCulture, $"meshwave-peer-v2|{userId}|{address}|{port}|{encryptionPublicKey}|{ms}");
    }

    private static string SessionProofData(string userId, long nonce)
    {
        return string.Create(CultureInfo.InvariantCulture, $"meshwave-session-v1|{userId}|{nonce}");
    }

    private static DateTime TruncateToMilliseconds(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        return new DateTime(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
    }
}
