using MeshWave.Common.Core.Storage;
using Xunit;

namespace MeshWave.Common.Core.Tests;

public class WaveformBinaryFormatTests
{
    [Fact]
    public void EncodeDecode_RoundTrip_PreservesValues()
    {
        // Arrange
        float[] original = [0.0f, 0.5f, 1.0f, 0.25f, 0.75f];

        // Act
        var encoded = WaveformBinaryFormat.Encode(original);
        var decoded = WaveformBinaryFormat.Decode(encoded);

        // Assert
        Assert.NotNull(decoded);
        Assert.Equal(original.Length, decoded.Length);
        for (var i = 0; i < original.Length; i++)
            // Allow for small precision loss due to 8-bit quantization (1/255)
            Assert.Equal(original[i], decoded[i], precision: 2);
    }

    [Fact]
    public void Decode_ReturnsNull_ForInvalidMagic()
    {
        // Arrange
        byte[] data = [0, 0, 0, 0, 1, 0, 5, 0, 255, 255, 255, 255, 255];

        // Act
        var result = WaveformBinaryFormat.Decode(data);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Decode_ReturnsNull_ForInvalidVersion()
    {
        // Arrange: Magic OK, but Version = 2
        byte[] data = [0x45, 0x56, 0x41, 0x57, 0x02, 0x00, 0x01, 0x00, 0x7F];

        // Act
        var result = WaveformBinaryFormat.Decode(data);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Decode_ReturnsNull_ForCorruptedData()
    {
        // Arrange: Length says 10, but only 5 bytes provided
        byte[] data = [0x45, 0x56, 0x41, 0x57, 0x01, 0x00, 0x0A, 0x00, 1, 2, 3, 4, 5];

        // Act
        var result = WaveformBinaryFormat.Decode(data);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Encode_HandlesLargeArrays()
    {
        // Arrange
        var large = new float[10000];
        for (var i = 0; i < large.Length; i++) large[i] = 0.1f;

        // Act
        var encoded = WaveformBinaryFormat.Encode(large);
        var decoded = WaveformBinaryFormat.Decode(encoded);

        // Assert
        Assert.NotNull(decoded);
        Assert.Equal(10000, decoded.Length);
    }
}
