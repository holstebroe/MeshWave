using System.Security.Cryptography;
using System.Text;

namespace MeshWave.Common.Core.Crypto;

/// <summary>
/// Provides cryptographic utilities for signing and verification.
/// Uses RSA for digital signatures (Ed25519 support varies by framework version).
/// </summary>
public class CryptoService
{
    private const int RsaKeySize = 4096;

    /// <summary>
    /// Generates a new RSA keypair.
    /// Returns (privateKeyPem, publicKeyPem).
    /// </summary>
    public static (string privateKeyPem, string publicKeyPem) GenerateKeyPair()
    {
        using var rsa = RSA.Create(RsaKeySize);
        var privateKeyPem = rsa.ExportRSAPrivateKeyPem();
        var publicKeyPem = rsa.ExportRSAPublicKeyPem();
        return (privateKeyPem, publicKeyPem);
    }

    /// <summary>
    /// Derives a user ID from a public key (SHA256 hash of public key, formatted as GUID-like string).
    /// </summary>
    public static string DeriveUserIdFromPublicKey(string publicKeyPem)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(publicKeyPem));
        return new Guid(hash.Take(16).ToArray()).ToString();
    }

    /// <summary>
    /// Signs data using RSA private key.
    /// </summary>
    public static string SignData(string data, string privateKeyPem)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem.ToCharArray());
        var signature = rsa.SignData(Encoding.UTF8.GetBytes(data), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return Convert.ToBase64String(signature);
    }

    /// <summary>
    /// Verifies a signature using RSA public key.
    /// </summary>
    public static bool VerifySignature(string data, string signature, string publicKeyPem)
    {
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem.ToCharArray());
            var signatureBytes = Convert.FromBase64String(signature);
            return rsa.VerifyData(Encoding.UTF8.GetBytes(data), signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch
        {
            return false;
        }
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
