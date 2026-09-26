using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeshWave.Common.Core.P2P;
using NLog;

namespace MeshWave.Synchronizer;

/// <summary>
/// A Stream that downloads chunks from multiple peers concurrently in the background,
/// enabling immediate sequential reading while load balancing.
/// </summary>
public class ParallelChunkStream : Stream
{
    private readonly string _contentHash;
    private readonly Func<PeerInfo, string, long, long, CancellationToken, Task<(byte[]? Bytes, long TotalLength, string FailureReason)>> _requestChunk;
    private readonly Logger _logger;
    private readonly List<PeerInfo> _peers;

    private const int ChunkSize = 512 * 1024; // 512 KB chunks

    private long _length;
    private long _position;
    private bool _initialized;
    private bool _disposed;

    private readonly object _lock = new();
    private readonly ConcurrentDictionary<int, byte[]> _completedChunks = new();
    private readonly ManualResetEventSlim _chunkReadyEvent = new(false);

    private CancellationTokenSource _cts = new();

    /// <param name="requestChunk">Fetches (peer, hash, offset, length) and returns the bytes and the total content length.</param>
    public ParallelChunkStream(string contentHash, IEnumerable<PeerInfo> peers,
        Func<PeerInfo, string, long, long, CancellationToken, Task<(byte[]? Bytes, long TotalLength, string FailureReason)>> requestChunk, Logger logger)
    {
        _contentHash = contentHash;
        _peers = peers.ToList();
        _requestChunk = requestChunk;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;

        // Try to get total length from the first responsive peer
        foreach (var peer in _peers)
        {
            var (_, totalLength, failureReason) = await _requestChunk(peer, _contentHash, 0, 0, _cts.Token);

            if (totalLength > 0)
            {
                _length = totalLength;
                _initialized = true;
                break;
            }
        }

        if (_initialized)
        {
            StartWorkers();
        }
    }

    private void StartWorkers()
    {
        int totalChunks = (int)Math.Ceiling((double)_length / ChunkSize);
        var pendingChunks = new ConcurrentQueue<int>(Enumerable.Range(0, totalChunks));

        // Start one worker per peer
        foreach (var peer in _peers)
        {
            _ = Task.Run(async () => await WorkerLoop(peer, pendingChunks, _cts.Token), _cts.Token);
        }
    }

    private async Task WorkerLoop(PeerInfo peer, ConcurrentQueue<int> pendingChunks, CancellationToken token)
    {
        while (!token.IsCancellationRequested && pendingChunks.TryDequeue(out int chunkIndex))
        {
            if (_completedChunks.ContainsKey(chunkIndex)) continue;

            long offset = chunkIndex * ChunkSize;
            long length = Math.Min(ChunkSize, _length - offset);

            var (bytes, _, failureReason) = await _requestChunk(peer, _contentHash, offset, length, token);

            var chunk = bytes == null ? null : ExtractChunk(bytes, offset, length);
            if (chunk != null)
            {
                _completedChunks[chunkIndex] = chunk;
                _chunkReadyEvent.Set();
            }
            else
            {
                _logger.Warn($"Peer {peer.UserId} failed to download chunk {chunkIndex} of {_contentHash}: {failureReason}");
                // Put back in queue to be retried
                pendingChunks.Enqueue(chunkIndex);

                // Pause slightly on failure before picking next chunk
                await Task.Delay(1000, token);
            }
        }
    }

    /// <summary>
    /// Returns the bytes for the requested chunk from a peer response, or null if the response is unusable.
    /// Peers that support chunking return exactly <paramref name="length"/> bytes. Older peers ignore the chunk
    /// request and return the whole file, in which case the chunk is sliced out at <paramref name="offset"/>.
    /// </summary>
    internal static byte[]? ExtractChunk(byte[] bytes, long offset, long length)
    {
        if (bytes.Length == length)
            return bytes;

        if (bytes.Length >= offset + length && bytes.Length > length)
        {
            var exactChunk = new byte[length];
            Array.Copy(bytes, offset, exactChunk, 0, length);
            return exactChunk;
        }

        return null;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set => _position = value;
    }

    public override void Flush() { }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ParallelChunkStream));
        if (!_initialized) throw new InvalidOperationException("Stream is not initialized.");

        int totalRead = 0;

        while (count > 0 && _position < _length)
        {
            int chunkIndex = (int)(_position / ChunkSize);
            long chunkOffset = _position % ChunkSize;

            if (_completedChunks.TryGetValue(chunkIndex, out var chunkBytes))
            {
                int toCopy = (int)Math.Min(count, chunkBytes.Length - chunkOffset);
                Array.Copy(chunkBytes, chunkOffset, buffer, offset, toCopy);

                _position += toCopy;
                offset += toCopy;
                count -= toCopy;
                totalRead += toCopy;
            }
            else
            {
                // Wait for the chunk to be downloaded
                try
                {
                    _chunkReadyEvent.Wait(_cts.Token);
                    _chunkReadyEvent.Reset();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        return totalRead;
    }

    // Asynchronous Read override
    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ParallelChunkStream));
        if (!_initialized) throw new InvalidOperationException("Stream is not initialized.");

        int totalRead = 0;

        while (count > 0 && _position < _length)
        {
            int chunkIndex = (int)(_position / ChunkSize);
            long chunkOffset = _position % ChunkSize;

            if (_completedChunks.TryGetValue(chunkIndex, out var chunkBytes))
            {
                int toCopy = (int)Math.Min(count, chunkBytes.Length - chunkOffset);
                Array.Copy(chunkBytes, chunkOffset, buffer, offset, toCopy);

                _position += toCopy;
                offset += toCopy;
                count -= toCopy;
                totalRead += toCopy;
            }
            else
            {
                // Wait for the chunk asynchronously without blocking a thread
                try
                {
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
                    await Task.Run(() => _chunkReadyEvent.Wait(linkedCts.Token), linkedCts.Token);
                    _chunkReadyEvent.Reset();
                }
                catch (OperationCanceledException)
                {
                    break; // Break and return what we have read
                }
            }
        }

        return totalRead;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        _position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };
        return _position;
    }

    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _cts.Cancel();
                _cts.Dispose();
                _chunkReadyEvent.Dispose();
                _completedChunks.Clear();
            }
            _disposed = true;
        }
        base.Dispose(disposing);
    }
}
