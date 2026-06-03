using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Threading;
using MeshWave.Common.Core.Storage;
using NAudio.Wave;

namespace MeshWave.Services
{
    /// <summary>
    /// Audio playback service using NAudio.
    /// </summary>
    public class AudioPlaybackService : IDisposable
    {
        private IWavePlayer? _waveOut;
        private WaveStream? _audioFile;
        private DispatcherTimer? _positionTimer;
        private string? _currentFilePath;
        private bool _isDisposed;
        private bool _isBuffering;

        public event EventHandler<TimeSpan>? PositionChanged;
        public event EventHandler? PlaybackStopped;
        public event EventHandler<bool>? IsBufferingChanged;

        public bool IsPlaying => _waveOut?.PlaybackState == PlaybackState.Playing;

        public bool IsBuffering
        {
            get => _isBuffering;
            private set
            {
                if (_isBuffering != value)
                {
                    _isBuffering = value;
                    IsBufferingChanged?.Invoke(this, _isBuffering);
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

        /// <summary>
        /// Loads a file that is still growing (being downloaded).
        /// Waits for a minimum buffer before initiating playback.
        /// </summary>
        public async Task LoadGrowingFileAsync(string filePath, long expectedLength, Task? completionTask = null)
        {
            Stop();
            _currentFilePath = filePath;
            IsBuffering = true;

            // Wait until we have at least 256KB or the file is complete
            while (!_isDisposed)
            {
                var info = new FileInfo(filePath);
                if (info.Exists && (info.Length >= 256 * 1024 || info.Length >= expectedLength || (completionTask?.IsCompleted == true)))
                    break;

                await Task.Delay(500);
            }

            if (_isDisposed) return;

            var growingStream = new GrowingFileStream(filePath, expectedLength);
            if (completionTask != null)
            {
                _ = completionTask.ContinueWith(t => {
                    if (t.IsCompletedSuccessfully) growingStream.MarkComplete();
                    else if (t.IsFaulted) growingStream.ReportError(t.Exception?.InnerException ?? new Exception("Download failed."));
                });
            }

            _audioFile = new StreamMediaFoundationReader(growingStream);

            _waveOut = new WaveOutEvent();
            _waveOut.Init(_audioFile);
            _waveOut.PlaybackStopped += OnPlaybackStopped;

            _positionTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _positionTimer.Tick += (s, e) => PositionChanged?.Invoke(this, CurrentPosition);

            IsBuffering = false;
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
}
