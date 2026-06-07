using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using NAudio.Wave;

namespace MeshWave.Services
{
    /// <summary>
    /// Audio playback service using NAudio.
    /// </summary>
    public class AudioPlaybackService : IAudioPlaybackService
    {
        private IWavePlayer? _waveOut;
        private WaveStream? _audioFile;
        private GrowingFileStream? _growingStream;
        private DispatcherTimer? _positionTimer;
        private string? _currentFilePath;
        private bool _isDisposed;
        private bool _isBuffering;

        public event EventHandler<TimeSpan>? PositionChanged;
        public event EventHandler? PlaybackStopped;
        public event EventHandler<bool>? BufferingChanged;

        public bool IsPlaying => _waveOut?.PlaybackState == PlaybackState.Playing;
        public bool IsBuffering
        {
            get => _isBuffering;
            private set
            {
                if (_isBuffering != value)
                {
                    _isBuffering = value;
                    BufferingChanged?.Invoke(this, value);
                }
            }
        }
        public TimeSpan CurrentPosition => _audioFile?.CurrentTime ?? TimeSpan.Zero;
        public TimeSpan Duration => _audioFile?.TotalTime ?? TimeSpan.Zero;

        public void LoadFile(string filePath)
        {
            Stop();
            _currentFilePath = filePath;
            _audioFile = new AudioFileReader(filePath);
            _waveOut = new WaveOutEvent();
            _waveOut.Init(_audioFile);
            _waveOut.PlaybackStopped += OnPlaybackStopped;

            _positionTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _positionTimer.Tick += (s, e) => PositionChanged?.Invoke(this, CurrentPosition);
        }

        public async Task LoadGrowingFileAsync(string tempPath, long totalBytes, CancellationToken ct = default)
        {
            Stop();
            _currentFilePath = tempPath;
            IsBuffering = true;

            // Buffered Start: Wait for some initial data
            const int initialBufferBytes = 256 * 1024; // 256 KB
            while (new FileInfo(tempPath).Length < Math.Min(initialBufferBytes, totalBytes) && !ct.IsCancellationRequested)
            {
                await Task.Delay(500, ct);
            }

            if (ct.IsCancellationRequested) return;

            _growingStream = new GrowingFileStream(tempPath, totalBytes);

            // NAudio WaveFileReader/Mp3FileReader often need a seekable stream.
            // MP3 can be streamed via Mp3FileReader(Stream).
            if (tempPath.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
            {
                _audioFile = new Mp3FileReader(_growingStream);
            }
            else
            {
                // Fallback to trying to open it as a standard file if format detection fails on stream
                _audioFile = new AudioFileReader(tempPath);
            }

            _waveOut = new WaveOutEvent();
            _waveOut.Init(_audioFile);
            _waveOut.PlaybackStopped += OnPlaybackStopped;

            _positionTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _positionTimer.Tick += (s, e) =>
            {
                PositionChanged?.Invoke(this, CurrentPosition);
                CheckBuffering();
            };

            IsBuffering = false;
        }

        private void CheckBuffering()
        {
            if (_audioFile == null || _waveOut == null || _growingStream == null) return;

            // Mp3FileReader uses a buffer, but we can check if we're near the current end
            if (IsPlaying && _audioFile.Position >= _growingStream.Length - 32768 && _growingStream.Length < _growingStream.TotalLength)
            {
                IsBuffering = true;
                _waveOut.Pause();
            }
            else if (IsBuffering && _audioFile.Position < _growingStream.Length - 128 * 1024)
            {
                IsBuffering = false;
                _waveOut.Play();
            }
        }

        public void Play()
        {
            // If disposed or never loaded, reload
            if (_waveOut == null && !string.IsNullOrEmpty(_currentFilePath))
            {
                LoadFile(_currentFilePath);
            }

            _waveOut?.Play();
            _positionTimer?.Start();
        }

        public void Pause()
        {
            _waveOut?.Pause();
            _positionTimer?.Stop();
        }

        public void Stop()
        {
            _waveOut?.Stop();
            _positionTimer?.Stop();
            if (_audioFile != null)
            {
                _audioFile.CurrentTime = TimeSpan.Zero;
            }
        }

        public void SetPosition(TimeSpan position)
        {
            if (_audioFile != null)
            {
                _audioFile.CurrentTime = position;
                PositionChanged?.Invoke(this, position);
            }
        }

        public void SetVolume(float volume)
        {
            if (_waveOut != null)
            {
                _waveOut.Volume = Math.Clamp(volume, 0f, 1f);
            }
        }

        private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
        {
            _positionTimer?.Stop();

            if (_isDisposed) return;

            // Ensure we notify the final position at the end of the track
            PositionChanged?.Invoke(this, Duration);
            PlaybackStopped?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            _positionTimer?.Stop();
            _waveOut?.Stop();
            _audioFile?.Dispose();
            _waveOut?.Dispose();
            _audioFile = null;
            _waveOut = null;

            PositionChanged = null;
            PlaybackStopped = null;
        }
    }

    /// <summary>
    /// A Stream implementation that allows reading from a file that is still being written to.
    /// </summary>
    internal class GrowingFileStream : Stream
    {
        private readonly string _path;
        private readonly long _totalLength;
        private FileStream? _currentStream;
        private long _position;

        public GrowingFileStream(string path, long totalLength)
        {
            _path = path;
            _totalLength = totalLength;
            _currentStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        }

        public long TotalLength => _totalLength;

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _currentStream?.Length ?? 0;
        public override long Position { get => _position; set => _position = value; }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_currentStream == null) return 0;

            // Re-open if closed or just refresh the length by seeking to current position in a Share.ReadWrite stream
            _currentStream.Position = _position;
            int read = _currentStream.Read(buffer, offset, count);
            _position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin: _position = offset; break;
                case SeekOrigin.Current: _position += offset; break;
                case SeekOrigin.End: _position = _totalLength + offset; break;
            }
            return _position;
        }

        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _currentStream?.Dispose();
                _currentStream = null;
            }
            base.Dispose(disposing);
        }
    }
}
