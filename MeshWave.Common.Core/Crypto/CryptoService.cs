using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Agreement;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

namespace MeshWave.Common.Core.Crypto;

/// <summary>
/// Provides cryptographic utilities for signing, verification and sealing.
/// Signing uses Ed25519; encryption uses X25519 (ECDH) with HKDF-SHA256 and AES-256-GCM, since Ed25519 keys
/// cannot be used for encryption. The two key pairs are unrelated: a signing identity does not double as an
/// encryption identity. .NET's built-in <see cref="System.Security.Cryptography"/> has no Ed25519/X25519 support
/// as of net10.0, so both are implemented with BouncyCastle (pure managed, no OS-provider dependency).
/// Public/private keys are the raw key material, base64-encoded (32 bytes each); there is no PEM encoding.
/// </summary>
public class CryptoService
{
    private const int KeyLengthBytes = 32;
    private const int GcmNonceBytes = 12;
    private const int GcmTagBits = 128;
    private static readonly byte[] EncryptionInfo = Encoding.UTF8.GetBytes("meshwave-seal-v1");

    /// <summary>
    /// Generates a new Ed25519 signing key pair. Returns (privateKey, publicKey), base64-encoded.
    /// </summary>
    public static (string privateKey, string publicKey) GenerateSigningKeyPair()
    {
        var random = new SecureRandom();
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(random));
        var keyPair = generator.GenerateKeyPair();
        var privateKey = (Ed25519PrivateKeyParameters)keyPair.Private;
        var publicKey = (Ed25519PublicKeyParameters)keyPair.Public;
        return (Convert.ToBase64String(privateKey.GetEncoded()), Convert.ToBase64String(publicKey.GetEncoded()));
    }

    /// <summary>
    /// Generates a new X25519 encryption key pair. Returns (privateKey, publicKey), base64-encoded.
    /// </summary>
    public static (string privateKey, string publicKey) GenerateEncryptionKeyPair()
    {
        var random = new SecureRandom();
        var generator = new X25519KeyPairGenerator();
        generator.Init(new X25519KeyGenerationParameters(random));
        var keyPair = generator.GenerateKeyPair();
        var privateKey = (X25519PrivateKeyParameters)keyPair.Private;
        var publicKey = (X25519PublicKeyParameters)keyPair.Public;
        return (Convert.ToBase64String(privateKey.GetEncoded()), Convert.ToBase64String(publicKey.GetEncoded()));
    }

    /// <summary>
    /// Derives a user ID from an Ed25519 signing public key (SHA256 hash of the key, formatted as a GUID-like string).
    /// </summary>
    public static string DeriveUserIdFromPublicKey(string publicKey)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(publicKey));
        return new Guid(hash.Take(16).ToArray()).ToString();
    }

    /// <summary>
    /// Returns true when <paramref name="publicKey"/> is the signing key that <paramref name="userId"/> was derived from.
    /// User IDs are self-certifying, so a key claimed for a user must always pass this check before it is trusted.
    /// </summary>
    public static bool IsPublicKeyForUser(string? userId, string? publicKey)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(publicKey))
            return false;

        return string.Equals(DeriveUserIdFromPublicKey(publicKey), userId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Signs data with an Ed25519 private key. Returns the 64-byte signature, base64-encoded.
    /// </summary>
    public static string SignData(string data, string privateKey)
    {
        var key = new Ed25519PrivateKeyParameters(Convert.FromBase64String(privateKey), 0);
        var signer = new Ed25519Signer();
        signer.Init(true, key);
        var bytes = Encoding.UTF8.GetBytes(data);
        signer.BlockUpdate(bytes, 0, bytes.Length);
        return Convert.ToBase64String(signer.GenerateSignature());
    }

    /// <summary>
    /// Verifies an Ed25519 signature. Returns false (never throws) for a malformed key or signature.
    /// </summary>
    public static bool VerifySignature(string data, string signature, string publicKey)
    {
        try
        {
            var key = new Ed25519PublicKeyParameters(Convert.FromBase64String(publicKey), 0);
            var verifier = new Ed25519Signer();
            verifier.Init(false, key);
            var bytes = Encoding.UTF8.GetBytes(data);
            verifier.BlockUpdate(bytes, 0, bytes.Length);
            return verifier.VerifySignature(Convert.FromBase64String(signature));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Seals data to an X25519 public key: an ephemeral key pair agrees a shared secret with
    /// <paramref name="publicKey"/>, HKDF-SHA256 derives an AES-256-GCM key from it, and the ephemeral public key,
    /// nonce, ciphertext and authentication tag are packed together and base64-encoded. Only the holder of the
    /// matching private key can open it (see <see cref="DecryptData"/>).
    /// </summary>
    public static string EncryptData(string data, string publicKey)
    {
        var recipientKey = new X25519PublicKeyParameters(Convert.FromBase64String(publicKey), 0);

        var random = new SecureRandom();
        var ephemeralGenerator = new X25519KeyPairGenerator();
        ephemeralGenerator.Init(new X25519KeyGenerationParameters(random));
        var ephemeralKeyPair = ephemeralGenerator.GenerateKeyPair();
        var ephemeralPrivateKey = (X25519PrivateKeyParameters)ephemeralKeyPair.Private;
        var ephemeralPublicKey = (X25519PublicKeyParameters)ephemeralKeyPair.Public;

        var sharedSecret = new byte[X25519PrivateKeyParameters.SecretSize];
        var agreement = new X25519Agreement();
        agreement.Init(ephemeralPrivateKey);
        agreement.CalculateAgreement(recipientKey, sharedSecret, 0);

        var aesKey = DeriveAesKey(sharedSecret);

        var nonce = new byte[GcmNonceBytes];
        random.NextBytes(nonce);

        var plaintext = Encoding.UTF8.GetBytes(data);
        var cipher = new GcmBlockCipher(new AesEngine());
        cipher.Init(true, new AeadParameters(new KeyParameter(aesKey), GcmTagBits, nonce));
        var ciphertext = new byte[cipher.GetOutputSize(plaintext.Length)];
        var length = cipher.ProcessBytes(plaintext, 0, plaintext.Length, ciphertext, 0);
        length += cipher.DoFinal(ciphertext, length);

        var ephemeralPublicKeyBytes = ephemeralPublicKey.GetEncoded();
        var envelope = new byte[KeyLengthBytes + GcmNonceBytes + length];
        Buffer.BlockCopy(ephemeralPublicKeyBytes, 0, envelope, 0, KeyLengthBytes);
        Buffer.BlockCopy(nonce, 0, envelope, KeyLengthBytes, GcmNonceBytes);
        Buffer.BlockCopy(ciphertext, 0, envelope, KeyLengthBytes + GcmNonceBytes, length);

        return Convert.ToBase64String(envelope);
    }

    /// <summary>
    /// Opens data sealed with <see cref="EncryptData"/> using the matching X25519 private key.
    /// Returns null if decryption fails (e.g. invalid key, corrupted ciphertext, or a mismatched key/tag).
    /// </summary>
    public static string? DecryptData(string encryptedData, string privateKey)
    {
        try
        {
            var envelope = Convert.FromBase64String(encryptedData);
            if (envelope.Length < KeyLengthBytes + GcmNonceBytes)
                return null;

            var ephemeralPublicKey = new X25519PublicKeyParameters(envelope, 0);
            var nonce = envelope.AsSpan(KeyLengthBytes, GcmNonceBytes).ToArray();
            var ciphertextOffset = KeyLengthBytes + GcmNonceBytes;
            var ciphertextLength = envelope.Length - ciphertextOffset;

            var recipientPrivateKey = new X25519PrivateKeyParameters(Convert.FromBase64String(privateKey), 0);
            var sharedSecret = new byte[X25519PrivateKeyParameters.SecretSize];
            var agreement = new X25519Agreement();
            agreement.Init(recipientPrivateKey);
            agreement.CalculateAgreement(ephemeralPublicKey, sharedSecret, 0);

            var aesKey = DeriveAesKey(sharedSecret);

            var cipher = new GcmBlockCipher(new AesEngine());
            cipher.Init(false, new AeadParameters(new KeyParameter(aesKey), GcmTagBits, nonce));
            var plaintext = new byte[cipher.GetOutputSize(ciphertextLength)];
            var length = cipher.ProcessBytes(envelope, ciphertextOffset, ciphertextLength, plaintext, 0);
            length += cipher.DoFinal(plaintext, length);

            return Encoding.UTF8.GetString(plaintext, 0, length);
        }
        catch
        {
            return null;
        }
    }

    private static byte[] DeriveAesKey(byte[] sharedSecret)
    {
        var hkdf = new HkdfBytesGenerator(new Sha256Digest());
        hkdf.Init(new HkdfParameters(sharedSecret, null, EncryptionInfo));
        var aesKey = new byte[KeyLengthBytes];
        hkdf.GenerateBytes(aesKey, 0, KeyLengthBytes);
        return aesKey;
    }

    /// <summary>
    /// Computes SHA256 hash of data and returns as hex string.
    /// </summary>
    public static string ComputeHash(byte[] data)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(data);
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Computes SHA256 hash of a file and returns as hex string.
    /// </summary>
    public static string ComputeFileHash(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var fileStream = File.OpenRead(filePath);
        var hash = sha256.ComputeHash(fileStream);
        return Convert.ToHexString(hash);
    }
}
