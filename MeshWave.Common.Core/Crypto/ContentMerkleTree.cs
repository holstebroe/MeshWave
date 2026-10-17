using System.Security.Cryptography;

namespace MeshWave.Common.Core.Crypto;

/// <summary>
/// Builds and verifies a binary Merkle tree over fixed-size content chunks, so a peer streaming a large file
/// (<see cref="MeshWave.Synchronizer.ParallelChunkStream"/> in the P2P protocol review's T2) can verify each chunk
/// as it arrives instead of only after the whole file has been downloaded.
/// A chunk's leaf hash is SHA-256 of its bytes; an interior node is SHA-256(left || right); an odd node at a level
/// is paired with itself. This is MeshWave's own convention, not compatible with any external Merkle format.
/// </summary>
public static class ContentMerkleTree
{
    /// <summary>Chunk size used for parallel content downloads and per-chunk Merkle verification.</summary>
    public const int ChunkSizeBytes = 512 * 1024;

    /// <summary>Reads <paramref name="stream"/> in <see cref="ChunkSizeBytes"/> windows and hashes each one, in order.</summary>
    public static byte[][] ComputeLeafHashes(Stream stream)
    {
        var leaves = new List<byte[]>();
        var buffer = new byte[ChunkSizeBytes];

        int read;
        while ((read = ReadChunk(stream, buffer)) > 0)
        {
            leaves.Add(SHA256.HashData(buffer.AsSpan(0, read)));
            if (read < ChunkSizeBytes) break;
        }

        return leaves.Count > 0 ? leaves.ToArray() : [SHA256.HashData([])];
    }

    /// <summary>Convenience: leaf-hashes <paramref name="stream"/> and returns the root as a lowercase hex string.</summary>
    public static string ComputeRootHex(Stream stream)
    {
        return Convert.ToHexString(ComputeRoot(ComputeLeafHashes(stream))).ToLowerInvariant();
    }

    /// <summary>Reads up to <paramref name="buffer"/>'s length from <paramref name="stream"/>, looping over short reads.</summary>
    private static int ReadChunk(Stream stream, byte[] buffer)
    {
        var offset = 0;
        int read;
        while (offset < buffer.Length && (read = stream.Read(buffer, offset, buffer.Length - offset)) > 0)
            offset += read;
        return offset;
    }

    /// <summary>Builds the root hash of the tree over <paramref name="leafHashes"/>.</summary>
    public static byte[] ComputeRoot(IReadOnlyList<byte[]> leafHashes)
    {
        if (leafHashes.Count == 0) return SHA256.HashData([]);

        var level = leafHashes.ToArray();
        while (level.Length > 1)
            level = NextLevel(level);

        return level[0];
    }

    /// <summary>
    /// Builds the sibling-hash proof for the leaf at <paramref name="leafIndex"/>: one hash per tree level, from the
    /// leaf's sibling up to the level just below the root. <see cref="VerifyProof"/> recombines them with the leaf's
    /// own hash to recompute the root.
    /// </summary>
    public static byte[][] ComputeProof(IReadOnlyList<byte[]> leafHashes, int leafIndex)
    {
        if (leafIndex < 0 || leafIndex >= leafHashes.Count)
            throw new ArgumentOutOfRangeException(nameof(leafIndex));

        var proof = new List<byte[]>();
        var level = leafHashes.ToArray();
        var index = leafIndex;

        while (level.Length > 1)
        {
            var siblingIndex = index % 2 == 0 ? index + 1 : index - 1;
            proof.Add(siblingIndex < level.Length ? level[siblingIndex] : level[index]);

            level = NextLevel(level);
            index /= 2;
        }

        return proof.ToArray();
    }

    /// <summary>
    /// Recomputes the root from <paramref name="leafHash"/> (the SHA-256 of the received chunk) at
    /// <paramref name="leafIndex"/> using <paramref name="proof"/>, and compares it to <paramref name="expectedRoot"/>.
    /// </summary>
    public static bool VerifyProof(byte[] leafHash, int leafIndex, IReadOnlyList<byte[]> proof, byte[] expectedRoot)
    {
        var hash = leafHash;
        var index = leafIndex;

        foreach (var sibling in proof)
        {
            hash = index % 2 == 0 ? CombineHash(hash, sibling) : CombineHash(sibling, hash);
            index /= 2;
        }

        return hash.AsSpan().SequenceEqual(expectedRoot);
    }

    private static byte[][] NextLevel(byte[][] level)
    {
        var next = new byte[(level.Length + 1) / 2][];
        for (var i = 0; i < next.Length; i++)
        {
            var left = level[i * 2];
            var right = i * 2 + 1 < level.Length ? level[i * 2 + 1] : left;
            next[i] = CombineHash(left, right);
        }
        return next;
    }

    private static byte[] CombineHash(byte[] left, byte[] right)
    {
        Span<byte> buffer = stackalloc byte[64];
        left.CopyTo(buffer);
        right.CopyTo(buffer[32..]);
        return SHA256.HashData(buffer);
    }
}
