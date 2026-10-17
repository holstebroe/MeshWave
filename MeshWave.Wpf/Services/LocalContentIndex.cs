using System.Collections.Concurrent;
using System.IO;
using MeshWave.Common.Core.Crypto;
using MeshWave.Common.Core.P2P;
using MeshWave.Synchronizer;

namespace MeshWave.Wpf.Services;

/// <summary>
/// Serves local file content to peers by content hash. Replaces the previous approach of hashing every file in the
/// library on every chunk request (P2P protocol review T1): a hash-&gt;path index is built once and refreshed
/// periodically (or once, on demand, when a lookup misses), and each request reads only the requested byte range
/// from a <see cref="FileStream"/> instead of loading the whole file into memory.
/// Also builds the Merkle proof for a chunk-aligned request (T2), from leaf hashes computed once per file and cached.
/// </summary>
public sealed class LocalContentIndex
{
    private static readonly TimeSpan RebuildInterval = TimeSpan.FromMinutes(2);

    private readonly Func<IReadOnlyList<(string Root, bool IsImageCache)>> _rootsProvider;
    private readonly Func<HashSet<string>> _supportedExtensionsProvider;

    private readonly object _rebuildLock = new();
    private Dictionary<string, string> _hashToPath = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, (DateTime WriteUtc, long Length, string Hash)> _fileHashCache = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastBuiltUtc = DateTime.MinValue;

    private readonly ConcurrentDictionary<string, byte[][]> _merkleLeafCache = new(StringComparer.OrdinalIgnoreCase);
    private const int MaxCachedMerkleTrees = 200;

    /// <param name="rootsProvider">Folders to scan, each flagged whether it holds cover/icon images (only .png/.jpg there count) rather than music files.</param>
    /// <param name="supportedExtensionsProvider">Music file extensions to index outside the image roots.</param>
    public LocalContentIndex(Func<IReadOnlyList<(string Root, bool IsImageCache)>> rootsProvider, Func<HashSet<string>> supportedExtensionsProvider)
    {
        _rootsProvider = rootsProvider;
        _supportedExtensionsProvider = supportedExtensionsProvider;
    }

    /// <summary>
    /// Resolves a byte-range slice of content by hash. Matches the <c>Func&lt;string, long?, long?, ContentSlice?&gt;</c>
    /// content-provider contract of <see cref="SyncOrchestrator.StartAsync"/>.
    /// </summary>
    public ContentSlice? TryReadSlice(string contentHash, long? chunkOffset, long? chunkLength)
    {
        var path = ResolvePath(contentHash);
        if (path == null) return null;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var totalLength = stream.Length;
            var (offset, length) = ManifestExchangeServer.ResolveContentSlice(totalLength, chunkOffset, chunkLength);

            var bytes = new byte[length];
            if (length > 0)
            {
                stream.Seek(offset, SeekOrigin.Begin);
                ReadExactly(stream, bytes);
            }

            var proof = TryBuildChunkProof(contentHash, path, offset, length, totalLength);
            return new ContentSlice { TotalLength = totalLength, Bytes = bytes, MerkleProof = proof };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null; // file moved/deleted/locked between lookup and read: treat as not found (best effort).
        }
    }

    private static void ReadExactly(Stream stream, byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = stream.Read(buffer, offset, buffer.Length - offset);
            if (read <= 0) throw new EndOfStreamException();
            offset += read;
        }
    }

    /// <summary>
    /// Builds the Merkle proof for a request that exactly aligns to one <see cref="ContentMerkleTree.ChunkSizeBytes"/>
    /// chunk boundary. Other requests (whole-file, probes, odd ranges) get no proof; the requester falls back to
    /// verifying the whole download's hash once complete (F2).
    /// </summary>
    private IReadOnlyList<byte[]>? TryBuildChunkProof(string contentHash, string path, long offset, long length, long totalLength)
    {
        if (length <= 0 || offset % ContentMerkleTree.ChunkSizeBytes != 0)
            return null;

        var chunkIndex = (int)(offset / ContentMerkleTree.ChunkSizeBytes);
        var expectedLength = Math.Min(ContentMerkleTree.ChunkSizeBytes, totalLength - offset);
        if (length != expectedLength)
            return null;

        if (_merkleLeafCache.Count > MaxCachedMerkleTrees)
            _merkleLeafCache.Clear();

        var leaves = _merkleLeafCache.GetOrAdd(contentHash, _ =>
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return ContentMerkleTree.ComputeLeafHashes(stream);
        });

        return chunkIndex < leaves.Length ? ContentMerkleTree.ComputeProof(leaves, chunkIndex) : null;
    }

    private string? ResolvePath(string contentHash)
    {
        if (string.IsNullOrWhiteSpace(contentHash)) return null;

        RebuildIfStale();

        Dictionary<string, string> index;
        lock (_rebuildLock) index = _hashToPath;

        if (index.TryGetValue(contentHash, out var path) && File.Exists(path))
            return path;

        // Might be a file added since the last scan: rebuild once, on demand, and try again.
        Rebuild();
        lock (_rebuildLock) index = _hashToPath;
        return index.TryGetValue(contentHash, out path) && File.Exists(path) ? path : null;
    }

    private void RebuildIfStale()
    {
        if (DateTime.UtcNow - _lastBuiltUtc < RebuildInterval) return;
        Rebuild();
    }

    private void Rebuild()
    {
        lock (_rebuildLock)
        {
            if (DateTime.UtcNow - _lastBuiltUtc < RebuildInterval && _hashToPath.Count > 0) return;

            var extensions = _supportedExtensionsProvider();
            var newHashToPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var newFileHashCache = new Dictionary<string, (DateTime, long, string)>(StringComparer.OrdinalIgnoreCase);

            foreach (var (root, isImageCache) in _rootsProvider())
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;

                foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
                {
                    if (isImageCache)
                    {
                        var ext = Path.GetExtension(file);
                        if (!string.Equals(ext, ".png", StringComparison.OrdinalIgnoreCase) && !string.Equals(ext, ".jpg", StringComparison.OrdinalIgnoreCase))
                            continue;
                    }
                    else if (!extensions.Contains(Path.GetExtension(file)))
                    {
                        continue;
                    }

                    try
                    {
                        var info = new FileInfo(file);
                        if (!info.Exists || info.Length <= 0) continue;

                        string hash;
                        if (_fileHashCache.TryGetValue(file, out var cached) && cached.WriteUtc == info.LastWriteTimeUtc && cached.Length == info.Length)
                            hash = cached.Hash;
                        else
                            hash = CryptoService.ComputeFileHash(file);

                        newFileHashCache[file] = (info.LastWriteTimeUtc, info.Length, hash);
                        newHashToPath[hash] = file;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // skip files that vanish or are locked mid-scan
                    }
                }
            }

            _hashToPath = newHashToPath;
            _fileHashCache = newFileHashCache;
            _lastBuiltUtc = DateTime.UtcNow;
        }
    }
}
