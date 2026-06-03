using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MeshWave.Common.Core.Storage;

/// <summary>
/// A read-only stream that can read from a file while it is still being written to by another process/thread.
/// It waits for more data to become available if the current end-of-file is reached,
/// until the file is marked as complete.
/// </summary>
public class GrowingFileStream : Stream
{
    private readonly string _filePath;
    private readonly FileStream _fileStream;
    private long _expectedTotalLength;
    private bool _isComplete;
    private Exception? _error;
    private readonly object _lock = new();

    public GrowingFileStream(string filePath, long expectedTotalLength)
    {
        _filePath = filePath;
        _expectedTotalLength = expectedTotalLength;

        // Open with ReadWrite and Share ReadWrite to allow concurrent writing.
        _fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _expectedTotalLength;
    public override long Position
    {
        get => _fileStream.Position;
        set => _fileStream.Position = value;
    }

    public void MarkComplete()
    {
        lock (_lock)
        {
            _isComplete = true;
        }
    }

    public void ReportError(Exception ex)
    {
        lock (_lock)
        {
            _error = ex;
        }
    }

    public override void Flush() => _fileStream.Flush();

    public override int Read(byte[] buffer, int offset, int count)
    {
        while (true)
        {
            int bytesRead = _fileStream.Read(buffer, offset, count);
            if (bytesRead > 0) return bytesRead;

            lock (_lock)
            {
                if (_error != null) throw _error;
                if (_isComplete || _fileStream.Position >= _expectedTotalLength)
                    return 0;
            }

            // Wait for more data
            Thread.Sleep(100);
        }
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        while (true)
        {
            int bytesRead = await _fileStream.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
            if (bytesRead > 0) return bytesRead;

            lock (_lock)
            {
                if (_error != null) throw _error;
                if (_isComplete || _fileStream.Position >= _expectedTotalLength)
                    return 0;
            }

            // Wait for more data
            await Task.Delay(100, cancellationToken);
        }
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            int bytesRead = await _fileStream.ReadAsync(buffer, cancellationToken);
            if (bytesRead > 0) return bytesRead;

            lock (_lock)
            {
                if (_error != null) throw _error;
                if (_isComplete || _fileStream.Position >= _expectedTotalLength)
                    return 0;
            }

            // Wait for more data
            await Task.Delay(100, cancellationToken);
        }
    }

    public override long Seek(long offset, SeekOrigin origin) => _fileStream.Seek(offset, origin);

    public override void SetLength(long value)
    {
        lock (_lock)
        {
            _expectedTotalLength = value;
        }
    }

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _fileStream.Dispose();
        }
        base.Dispose(disposing);
    }
}
