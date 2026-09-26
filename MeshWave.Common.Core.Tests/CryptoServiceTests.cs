using System.Text;
using MeshWave.Common.Core.Crypto;
using Xunit;

namespace MeshWave.Common.Core.Tests;

public class CryptoServiceTests
{
    [Fact]
    public void GenerateSigningKeyPair_ReturnsDistinct32ByteBase64Keys()
    {
        // Act
        var (privateKey, publicKey) = CryptoService.GenerateSigningKeyPair();

        // Assert: raw 32-byte Ed25519 keys, base64-encoded (not PEM).
        Assert.Equal(32, Convert.FromBase64String(privateKey).Length);
        Assert.Equal(32, Convert.FromBase64String(publicKey).Length);
        Assert.NotEqual(privateKey, publicKey);
    }

    [Fact]
    public void GenerateEncryptionKeyPair_ReturnsDistinct32ByteBase64Keys()
    {
        // Act
        var (privateKey, publicKey) = CryptoService.GenerateEncryptionKeyPair();

        // Assert: raw 32-byte X25519 keys, base64-encoded (not PEM).
        Assert.Equal(32, Convert.FromBase64String(privateKey).Length);
        Assert.Equal(32, Convert.FromBase64String(publicKey).Length);
        Assert.NotEqual(privateKey, publicKey);
    }

    [Fact]
    public void DeriveUserIdFromPublicKey_ReturnConsistentGuid()
    {
        // Arrange
        var (_, publicKey) = CryptoService.GenerateSigningKeyPair();

        // Act
        var userId1 = CryptoService.DeriveUserIdFromPublicKey(publicKey);
        var userId2 = CryptoService.DeriveUserIdFromPublicKey(publicKey);

        // Assert
        Assert.Equal(userId1, userId2);
        Assert.NotEmpty(userId1);
        // Verify it's a valid GUID format
        Assert.True(Guid.TryParse(userId1, out _));
    }

    [Fact]
    public void SignData_ProducesValidSignature()
    {
        // Arrange
        var (privateKey, publicKey) = CryptoService.GenerateSigningKeyPair();
        var data = "Test data to sign";

        // Act
        var signature = CryptoService.SignData(data, privateKey);

        // Assert
        Assert.NotNull(signature);
        Assert.NotEmpty(signature);
        // Signature should be base64 encoded - should not throw
        var signatureBytes = Convert.FromBase64String(signature);
        Assert.NotEmpty(signatureBytes);
    }

    [Fact]
    public void VerifySignature_ReturnsTrue_ForValidSignature()
    {
        // Arrange
        var (privateKey, publicKey) = CryptoService.GenerateSigningKeyPair();
        var data = "Test data to sign";
        var signature = CryptoService.SignData(data, privateKey);

        // Act
        var isValid = CryptoService.VerifySignature(data, signature, publicKey);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void VerifySignature_ReturnsFalse_ForAlteredData()
    {
        // Arrange
        var (privateKey, publicKey) = CryptoService.GenerateSigningKeyPair();
        var data = "Test data to sign";
        var signature = CryptoService.SignData(data, privateKey);
        var alteredData = "Altered test data";

        // Act
        var isValid = CryptoService.VerifySignature(alteredData, signature, publicKey);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void EncryptDecryptData_Roundtrip_Success()
    {
        // Arrange
        var (privateKey, publicKey) = CryptoService.GenerateEncryptionKeyPair();
        var data = "CastVote payload data";

        // Act
        var encryptedData = CryptoService.EncryptData(data, publicKey);
        var decryptedData = CryptoService.DecryptData(encryptedData, privateKey);

        // Assert
        Assert.NotNull(encryptedData);
        Assert.NotEqual(data, encryptedData);
        Assert.Equal(data, decryptedData);
    }

    [Fact]
    public void EncryptData_ProducesADifferentEnvelopeEachTime()
    {
        // Arrange: the ephemeral key pair and nonce must differ per call, even for identical input.
        var (_, publicKey) = CryptoService.GenerateEncryptionKeyPair();
        var data = "CastVote payload data";

        // Act
        var first = CryptoService.EncryptData(data, publicKey);
        var second = CryptoService.EncryptData(data, publicKey);

        // Assert
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void DecryptData_ReturnsNull_ForInvalidKey()
    {
        // Arrange
        var (_, publicKey) = CryptoService.GenerateEncryptionKeyPair();
        var (otherPrivateKey, _) = CryptoService.GenerateEncryptionKeyPair();
        var data = "CastVote payload data";

        // Act
        var encryptedData = CryptoService.EncryptData(data, publicKey);
        var decryptedData = CryptoService.DecryptData(encryptedData, otherPrivateKey);

        // Assert
        Assert.Null(decryptedData);
    }

    [Fact]
    public void DecryptData_ReturnsNull_ForCorruptedCiphertext()
    {
        // Arrange
        var (privateKey, _) = CryptoService.GenerateEncryptionKeyPair();

        // Act & Assert 1: Invalid Base64
        var invalidBase64 = "This is not valid base64!";
        var result1 = CryptoService.DecryptData(invalidBase64, privateKey);
        Assert.Null(result1);

        // Act & Assert 2: Valid Base64 but invalid ciphertext
        var invalidCiphertext = Convert.ToBase64String(Encoding.UTF8.GetBytes("Valid base64 but not a ciphertext"));
        var result2 = CryptoService.DecryptData(invalidCiphertext, privateKey);
        Assert.Null(result2);
    }

    [Fact]
    public void VerifySignature_ReturnsFalse_ForInvalidSignature()
    {
        // Arrange
        var (_, publicKey) = CryptoService.GenerateSigningKeyPair();
        var data = "Test data to sign";
        var invalidSignature = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5 });

        // Act
        var isValid = CryptoService.VerifySignature(data, invalidSignature, publicKey);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void ComputeHash_ReturnsConsistentHash()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("Test data");

        // Act
        var hash1 = CryptoService.ComputeHash(data);
        var hash2 = CryptoService.ComputeHash(data);

        // Assert
        Assert.Equal(hash1, hash2);
        Assert.NotEmpty(hash1);
    }

    [Fact]
    public void ComputeFileHash_ReturnsValidHash()
    {
        // Arrange
        var tempFilePath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.txt");
        File.WriteAllText(tempFilePath, "Test file content");

        try
        {
            // Act
            var hash = CryptoService.ComputeFileHash(tempFilePath);

            // Assert
            Assert.NotEmpty(hash);
            // Verify hash is valid hex string (only 0-9, a-f, A-F)
            Assert.All(hash, c => Assert.True((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')));
        }
        finally
        {
            File.Delete(tempFilePath);
        }
    }

    [Fact]
    public void IsPublicKeyForUser_OnlyAcceptsTheKeyTheUserIdWasDerivedFrom()
    {
        var (_, publicKey) = CryptoService.GenerateSigningKeyPair();
        var (_, otherPublicKey) = CryptoService.GenerateSigningKeyPair();
        var userId = CryptoService.DeriveUserIdFromPublicKey(publicKey);

        Assert.True(CryptoService.IsPublicKeyForUser(userId, publicKey));
        Assert.True(CryptoService.IsPublicKeyForUser(userId.ToUpperInvariant(), publicKey));
        Assert.False(CryptoService.IsPublicKeyForUser(userId, otherPublicKey));
        Assert.False(CryptoService.IsPublicKeyForUser(userId, null));
        Assert.False(CryptoService.IsPublicKeyForUser(null, publicKey));
    }
}
