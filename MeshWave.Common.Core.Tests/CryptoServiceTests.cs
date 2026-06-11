using System.Text;
using MeshWave.Common.Core.Crypto;
using Xunit;

namespace MeshWave.Common.Core.Tests;

public class CryptoServiceTests
{
    [Fact]
    public void GenerateKeyPair_ReturnsValidPemKeys()
    {
        // Act
        var (privateKey, publicKey) = CryptoService.GenerateKeyPair();

        // Assert
        Assert.NotNull(privateKey);
        Assert.NotNull(publicKey);
        Assert.StartsWith("-----BEGIN RSA PRIVATE KEY-----", privateKey);
        Assert.StartsWith("-----BEGIN RSA PUBLIC KEY-----", publicKey);
    }

    [Fact]
    public void DeriveUserIdFromPublicKey_ReturnConsistentGuid()
    {
        // Arrange
        var (_, publicKey) = CryptoService.GenerateKeyPair();

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
        var (privateKey, publicKey) = CryptoService.GenerateKeyPair();
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
        var (privateKey, publicKey) = CryptoService.GenerateKeyPair();
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
        var (privateKey, publicKey) = CryptoService.GenerateKeyPair();
        var data = "Test data to sign";
        var signature = CryptoService.SignData(data, privateKey);
        var alteredData = "Altered test data";

        // Act
        var isValid = CryptoService.VerifySignature(alteredData, signature, publicKey);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void VerifySignature_ReturnsFalse_ForInvalidSignature()
    {
        // Arrange
        var (_, publicKey) = CryptoService.GenerateKeyPair();
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
}
