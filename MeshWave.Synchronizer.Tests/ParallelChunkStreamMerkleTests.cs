using System.Security.Cryptography;
using MeshWave.Common.Core.Crypto;
using MeshWave.Common.Core.P2P;
using NLog;
using Xunit;

namespace MeshWave.Synchronizer.Tests;

/// <summary>
/// Tests for T2 (streaming downloads were not hash-verified): with an expected Merkle root, ParallelChunkStream must
/// reject a chunk that does not verify (rather than handing it to the reader) and keep retrying until a peer supplies
/// a chunk that does verify.
/// </summary>
public class ParallelChunkStreamMerkleTests
{
    private const string ContentHash = "test-content";
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private static PeerInfo Peer(string id) => new() { UserId = id, DisplayName = id, Address = "127.0.0.1", Port = 0 };

    [Fact]
    public async Task Stream_WithExpectedRoot_ReadsCorrectBytes_EvenWhenOnePeerServesTamperedChunks()
    {
        var data = new byte[ContentMerkleTree.ChunkSizeBytes + 100];
        new Random(1).NextBytes(data);

        using var leafStream = new MemoryStream(data);
        var leaves = ContentMerkleTree.ComputeLeafHashes(leafStream);
        var root = ContentMerkleTree.ComputeRoot(leaves);

        var goodPeer = Peer("good");
        var badPeer = Peer("bad");

        Task<(byte[]? Bytes, long TotalLength, IReadOnlyList<byte[]>? MerkleProof, string FailureReason)> RequestChunk(
            PeerInfo peer, string hash, long offset, long length, CancellationToken ct)
        {
            if (length == 0) return Task.FromResult<(byte[]?, long, IReadOnlyList<byte[]>?, string)>((null, data.LongLength, null, string.Empty));

            var chunkIndex = (int)(offset / ContentMerkleTree.ChunkSizeBytes);
            var correctBytes = data.AsSpan((int)offset, (int)length).ToArray();
            var proof = ContentMerkleTree.ComputeProof(leaves, chunkIndex);

            if (peer.UserId == "bad")
            {
                var tampered = (byte[])correctBytes.Clone();
                tampered[0] ^= 0xFF;
                return Task.FromResult<(byte[]?, long, IReadOnlyList<byte[]>?, string)>((tampered, data.LongLength, proof, string.Empty));
            }

            return Task.FromResult<(byte[]?, long, IReadOnlyList<byte[]>?, string)>((correctBytes, data.LongLength, proof, string.Empty));
        }

        using var stream = new ParallelChunkStream(ContentHash, [goodPeer, badPeer], RequestChunk, Logger, root);
        await stream.InitializeAsync();
        Assert.Equal(data.LongLength, stream.Length);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var result = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token)) > 0)
            result.Write(buffer, 0, read);

        Assert.Equal(data, result.ToArray());
    }

    [Fact]
    public async Task Stream_WithExpectedRoot_NeverHandsOutATamperedChunk_WhenOnlyABadPeerIsAvailable()
    {
        var data = new byte[500]; // single chunk, smaller than ChunkSizeBytes
        new Random(2).NextBytes(data);

        using var leafStream = new MemoryStream(data);
        var leaves = ContentMerkleTree.ComputeLeafHashes(leafStream);
        var root = ContentMerkleTree.ComputeRoot(leaves);

        var badPeer = Peer("bad");

        Task<(byte[]? Bytes, long TotalLength, IReadOnlyList<byte[]>? MerkleProof, string FailureReason)> RequestChunk(
            PeerInfo peer, string hash, long offset, long length, CancellationToken ct)
        {
            if (length == 0) return Task.FromResult<(byte[]?, long, IReadOnlyList<byte[]>?, string)>((null, data.LongLength, null, string.Empty));

            var tampered = new byte[length];
            new Random(3).NextBytes(tampered); // never matches the real content
            var proof = ContentMerkleTree.ComputeProof(leaves, 0);
            return Task.FromResult<(byte[]?, long, IReadOnlyList<byte[]>?, string)>((tampered, data.LongLength, proof, string.Empty));
        }

        using var stream = new ParallelChunkStream(ContentHash, [badPeer], RequestChunk, Logger, root);
        await stream.InitializeAsync();

        // The only peer's data never verifies, so the chunk never completes. ReadAsync gives up once its token is
        // cancelled and returns 0 (the same as it would for a peer that never answers), rather than handing back
        // tampered bytes.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var buffer = new byte[data.Length];
        var read = await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token);

        Assert.Equal(0, read);
    }

    [Fact]
    public async Task Stream_WithoutExpectedRoot_AcceptsWhateverBytesArrive_ForBackwardCompatibility()
    {
        var data = new byte[300];
        new Random(4).NextBytes(data);
        var peer = Peer("any");

        Task<(byte[]? Bytes, long TotalLength, IReadOnlyList<byte[]>? MerkleProof, string FailureReason)> RequestChunk(
            PeerInfo p, string hash, long offset, long length, CancellationToken ct)
        {
            if (length == 0) return Task.FromResult<(byte[]?, long, IReadOnlyList<byte[]>?, string)>((null, data.LongLength, null, string.Empty));
            var bytes = data.AsSpan((int)offset, (int)length).ToArray();
            return Task.FromResult<(byte[]?, long, IReadOnlyList<byte[]>?, string)>((bytes, data.LongLength, null, string.Empty));
        }

        using var stream = new ParallelChunkStream(ContentHash, [peer], RequestChunk, Logger, expectedMerkleRoot: null);
        await stream.InitializeAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token)) > 0)
            result.Write(buffer, 0, read);

        Assert.Equal(data, result.ToArray());
    }
}
