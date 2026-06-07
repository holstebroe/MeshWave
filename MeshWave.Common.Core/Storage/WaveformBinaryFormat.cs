namespace MeshWave.Common.Core.Storage;

/// <summary>
/// Handles binary serialization of waveform data.
/// Format: [MAGIC 32b] [VERSION 16b] [LENGTH 16b] [Data (length bytes)]
/// </summary>
public static class WaveformBinaryFormat
{
    private const uint Magic = 0x57415645; // 'WAVE'
    private const ushort CurrentVersion = 1;

    public static byte[] Encode(float[] samples)
    {
        if (samples == null) throw new ArgumentNullException(nameof(samples));

        // Clamping length to ushort.MaxValue if necessary, though 4096 is well within.
        var length = (ushort)Math.Min(samples.Length, ushort.MaxValue);

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(Magic);
        writer.Write(CurrentVersion);
        writer.Write(length);

        for (var i = 0; i < length; i++)
        {
            // Scale 0.0-1.0 float to 0-255 byte
            var b = (byte)Math.Clamp(samples[i] * 255f, 0, 255);
            writer.Write(b);
        }

        return ms.ToArray();
    }

    public static float[]? Decode(byte[] data)
    {
        if (data == null || data.Length < 8) return null;

        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms);

        try
        {
            var magic = reader.ReadUInt32();
            if (magic != Magic) return null;

            var version = reader.ReadUInt16();
            if (version != CurrentVersion) return null;

            var length = reader.ReadUInt16();

            // Validate that we have enough data left in the stream
            if (ms.Length - ms.Position < length) return null; // Corrupted

            var samples = new float[length];
            for (var i = 0; i < length; i++)
            {
                var b = reader.ReadByte();
                samples[i] = b / 255f;
            }

            return samples;
        }
        catch (EndOfStreamException)
        {
            return null;
        }
    }
}
