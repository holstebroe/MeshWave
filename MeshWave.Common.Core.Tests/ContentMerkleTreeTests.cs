using System.Security.Cryptography;
using MeshWave.Common.Core.Crypto;
using Xunit;

namespace MeshWave.Common.Core.Tests;

/// <summary>
/// Tests for the per-chunk Merkle verification added for the P2P protocol review's T2 (streaming downloads were not
/// hash-verified): a chunk is only accepted once it verifies against the author-published root.
/// </summary>
public class ContentMerkleTreeTests
{
    private static byte[][] Leaves(int count) =>
        Enumerable.Range(0, count).Select(i => SHA256.HashData([(byte)i])).ToArray();

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(17)]
    public void ComputeProof_VerifiesForEveryLeaf(int leafCount)
    {
        var leaves = Leaves(leafCount);
        var root = ContentMerkleTree.ComputeRoot(leaves);

        for (var i = 0; i < leafCount; i++)
        {
            var proof = ContentMerkleTree.ComputeProof(leaves, i);
            Assert.True(ContentMerkleTree.VerifyProof(leaves[i], i, proof, root), $"leaf {i} of {leafCount} failed to verify");
        }
    }

    [Fact]
    public void VerifyProof_RejectsATamperedChunk()
    {
        var leaves = Leaves(5);
        var root = ContentMerkleTree.ComputeRoot(leaves);
        var proof = ContentMerkleTree.ComputeProof(leaves, 2);

        var tamperedLeafHash = SHA256.HashData([99]); // not leaves[2]

        Assert.False(ContentMerkleTree.VerifyProof(tamperedLeafHash, 2, proof, root));
    }

    [Fact]
    public void VerifyProof_RejectsAProofForTheWrongIndex()
    {
        var leaves = Leaves(5);
        var root = ContentMerkleTree.ComputeRoot(leaves);
        var proofForIndex2 = ContentMerkleTree.ComputeProof(leaves, 2);

        // The correct leaf hash, but claimed at a different position: must not verify.
        Assert.False(ContentMerkleTree.VerifyProof(leaves[2], 3, proofForIndex2, root));
    }

    [Fact]
    public void VerifyProof_RejectsAgainstTheWrongRoot()
    {
        var leaves = Leaves(4);
        var otherRoot = ContentMerkleTree.ComputeRoot(Leaves(4));
        // Recompute leaves so the "other" root is actually different (different content bytes).
        var leaves2 = Enumerable.Range(100, 4).Select(i => SHA256.HashData([(byte)i])).ToArray();
        var root2 = ContentMerkleTree.ComputeRoot(leaves2);

        var proof = ContentMerkleTree.ComputeProof(leaves, 0);
        Assert.False(ContentMerkleTree.VerifyProof(leaves[0], 0, proof, root2));
        Assert.NotEqual(Convert.ToHexString(otherRoot), Convert.ToHexString(root2));
    }

    [Fact]
    public void ComputeLeafHashes_SplitsAStreamIntoChunkSizedLeaves()
    {
        var totalBytes = ContentMerkleTree.ChunkSizeBytes * 2 + 123;
        var data = new byte[totalBytes];
        new Random(42).NextBytes(data);

        using var stream = new MemoryStream(data);
        var leaves = ContentMerkleTree.ComputeLeafHashes(stream);

        Assert.Equal(3, leaves.Length);
        Assert.Equal(SHA256.HashData(data.AsSpan(0, ContentMerkleTree.ChunkSizeBytes)), leaves[0]);
        Assert.Equal(SHA256.HashData(data.AsSpan(ContentMerkleTree.ChunkSizeBytes, ContentMerkleTree.ChunkSizeBytes)), leaves[1]);
        Assert.Equal(SHA256.HashData(data.AsSpan(ContentMerkleTree.ChunkSizeBytes * 2, 123)), leaves[2]);
    }

    [Fact]
    public void EndToEnd_LeafHashesFromAFile_RootAndProofRoundTrip()
    {
        var data = new byte[ContentMerkleTree.ChunkSizeBytes + 1000];
        new Random(7).NextBytes(data);

        using var stream = new MemoryStream(data);
        var leaves = ContentMerkleTree.ComputeLeafHashes(stream);
        var root = ContentMerkleTree.ComputeRoot(leaves);

        // Chunk 1 (the short final chunk) verifies with its own bytes...
        var chunk1 = data.AsSpan(ContentMerkleTree.ChunkSizeBytes, 1000).ToArray();
        var proof1 = ContentMerkleTree.ComputeProof(leaves, 1);
        Assert.True(ContentMerkleTree.VerifyProof(SHA256.HashData(chunk1), 1, proof1, root));

        // ...but not with a chunk of different bytes.
        var wrongChunk = new byte[1000];
        Assert.False(ContentMerkleTree.VerifyProof(SHA256.HashData(wrongChunk), 1, proof1, root));
    }
}
