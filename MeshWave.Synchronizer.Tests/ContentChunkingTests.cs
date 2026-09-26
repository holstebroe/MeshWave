using Xunit;

namespace MeshWave.Synchronizer.Tests;

/// <summary>
/// Tests for byte-range (chunked) content transfer.
/// </summary>
public class ContentChunkingTests
{
    [Theory]
    [InlineData(1000L, null, null, 0L, 1000L)]     // no chunk requested: whole content
    [InlineData(1000L, 0L, 0L, 0L, 0L)]            // zero-length probe for the total length
    [InlineData(1000L, 200L, 300L, 200L, 300L)]    // a chunk in the middle
    [InlineData(1000L, 900L, 300L, 900L, 100L)]    // last chunk is clamped to the end
    [InlineData(1000L, 1500L, 10L, 1000L, 0L)]     // offset past the end sends nothing
    [InlineData(1000L, -5L, 10L, 0L, 10L)]         // negative offset is clamped
    [InlineData(1000L, 400L, null, 400L, 600L)]    // offset without length: rest of the content
    public void ResolveContentSlice_ClampsToContentBounds(long total, long? offset, long? length, long expectedOffset, long expectedLength)
    {
        var (sliceOffset, sliceLength) = ManifestExchangeServer.ResolveContentSlice(total, offset, length);

        Assert.Equal(expectedOffset, sliceOffset);
        Assert.Equal(expectedLength, sliceLength);
    }

    [Fact]
    public void ExtractChunk_ReturnsExactResponse_WhenPeerSupportsChunking()
    {
        var chunk = new byte[] { 5, 6, 7 };
        Assert.Same(chunk, ParallelChunkStream.ExtractChunk(chunk, offset: 5, length: 3));
    }

    [Fact]
    public void ExtractChunk_SlicesAtOffset_WhenLegacyPeerSendsWholeFile()
    {
        var wholeFile = Enumerable.Range(0, 10).Select(i => (byte)i).ToArray();

        var chunk = ParallelChunkStream.ExtractChunk(wholeFile, offset: 4, length: 3);

        Assert.Equal(new byte[] { 4, 5, 6 }, chunk);
    }

    [Fact]
    public void ExtractChunk_ReturnsNull_ForShortResponse()
    {
        Assert.Null(ParallelChunkStream.ExtractChunk(new byte[2], offset: 0, length: 3));
    }

    [Fact]
    public void ContentMatchesHash_VerifiesSha256Hashes()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));

        Assert.True(SyncOrchestrator.ContentMatchesHash(bytes, hash));
        Assert.True(SyncOrchestrator.ContentMatchesHash(bytes, hash.ToLowerInvariant()));
        Assert.False(SyncOrchestrator.ContentMatchesHash(new byte[] { 1, 2, 4 }, hash));
    }

    [Fact]
    public void ContentMatchesHash_AcceptsNonSha256Identifiers()
    {
        Assert.True(SyncOrchestrator.ContentMatchesHash(new byte[] { 1 }, "legacy-hash-id"));
    }
}
