using MeshWave.Common.Core;
using System.Text.Json;
using MeshWave.Common.Core.Crypto;

namespace MeshWave.Synchronizer;

/// <summary>
/// Manages the local peer's cryptographic identity.
/// Generates an Ed25519 signing key pair and an X25519 encryption key pair on first run and persists them to a
/// JSON file in AppData. The UserId is always derived from the signing public key fingerprint so it is stable
/// and verifiable.
/// </summary>
public class P2PIdentityService
{
    private readonly string _identityFilePath;

    public P2PIdentityService(IMeshWaveEnvironment environment, string? identityFilePath = null)
    {
        _identityFilePath = identityFilePath ?? environment.CombineInAppData("p2p_identity.json");
    }

    /// <summary>
    /// Loads the persisted identity or generates a new one if none exists.
    /// </summary>
    public LocalPeerIdentity LoadOrCreate(string displayName)
    {
        var stored = TryLoad();
        if (stored != null)
        {
            // Update display name if it changed in settings
            stored.DisplayName = SecurityLimits.Truncate(displayName, SecurityLimits.MaxDisplayNameLength);
            return stored;
        }

        return Generate(displayName);
    }

    /// <summary>
    /// Returns true if an identity file already exists on disk.
    /// </summary>
    public bool IdentityExists()
    {
        return File.Exists(_identityFilePath);
    }

    /// <summary>
    /// Regenerates a fresh keypair and saves it (call only when user explicitly resets identity).
    /// </summary>
    public LocalPeerIdentity Regenerate(string displayName)
    {
        return Generate(displayName);
    }

    private LocalPeerIdentity? TryLoad()
    {
        try
        {
            if (!File.Exists(_identityFilePath))
                return null;

            var json = File.ReadAllText(_identityFilePath);
            return JsonSerializer.Deserialize<StoredIdentity>(json)?.ToLocalPeerIdentity();
        }
        catch
        {
            return null;
        }
    }

    private LocalPeerIdentity Generate(string displayName)
    {
        var (privateKey, publicKey) = CryptoService.GenerateSigningKeyPair();
        var (encryptionPrivateKey, encryptionPublicKey) = CryptoService.GenerateEncryptionKeyPair();
        var userId = CryptoService.DeriveUserIdFromPublicKey(publicKey);

        var identity = new LocalPeerIdentity
        {
            UserId = userId,
            DisplayName = SecurityLimits.Truncate(displayName, SecurityLimits.MaxDisplayNameLength),
            PublicKey = publicKey,
            PrivateKey = privateKey,
            EncryptionPublicKey = encryptionPublicKey,
            EncryptionPrivateKey = encryptionPrivateKey
        };

        Save(identity);
        return identity;
    }

    private void Save(LocalPeerIdentity identity)
    {
        var dir = Path.GetDirectoryName(_identityFilePath)!;
        Directory.CreateDirectory(dir);

        var stored = StoredIdentity.From(identity);
        var json = JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_identityFilePath, json);
    }

    private sealed class StoredIdentity
    {
        public string UserId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string PublicKey { get; set; } = string.Empty;
        public string PrivateKey { get; set; } = string.Empty;

        // Required so that an identity file written before these existed fails to deserialize and is regenerated,
        // rather than silently loading with blank encryption keys.
        public required string EncryptionPublicKey { get; set; }
        public required string EncryptionPrivateKey { get; set; }

        public int ManifestPort { get; set; } = ManifestExchangeServer.DefaultPort;

        public LocalPeerIdentity ToLocalPeerIdentity()
        {
            return new LocalPeerIdentity
            {
                UserId = UserId,
                DisplayName = DisplayName,
                PublicKey = PublicKey,
                PrivateKey = PrivateKey,
                EncryptionPublicKey = EncryptionPublicKey,
                EncryptionPrivateKey = EncryptionPrivateKey,
                ManifestPort = ManifestPort
            };
        }

        public static StoredIdentity From(LocalPeerIdentity id)
        {
            return new StoredIdentity
            {
                UserId = id.UserId,
                DisplayName = id.DisplayName,
                PublicKey = id.PublicKey,
                PrivateKey = id.PrivateKey,
                EncryptionPublicKey = id.EncryptionPublicKey,
                EncryptionPrivateKey = id.EncryptionPrivateKey,
                ManifestPort = id.ManifestPort
            };
        }
    }
}
