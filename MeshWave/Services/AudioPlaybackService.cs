using System;
using System.Windows.Threading;
using NAudio.Wave;

namespace MeshWave.Services
{
    /// <summary>
    /// Audio playback service using NAudio.
    /// </summary>
    public class AudioPlaybackService : IDisposable
    {
        private IWavePlayer? _waveOut;
        private AudioFileReader? _audioFile;
        private DispatcherTimer? _positionTimer;
        private string? _currentFilePath;

        public event EventHandler<TimeSpan>? PositionChanged;
        public event EventHandler? PlaybackStopped;

        public bool IsPlaying => _waveOut?.PlaybackState == PlaybackState.Playing;
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
            PlaybackStopped?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            _positionTimer?.Stop();
            _waveOut?.Stop();
            _audioFile?.Dispose();
            _waveOut?.Dispose();
            _audioFile = null;
            _waveOut = null;
        }
    }
}
