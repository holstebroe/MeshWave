using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MeshWave.Services
{
    public interface IAudioPlaybackService : IDisposable
    {
        event EventHandler<TimeSpan>? PositionChanged;
        event EventHandler? PlaybackStopped;
        event EventHandler<bool>? BufferingChanged;

        bool IsPlaying { get; }
        bool IsBuffering { get; }
        TimeSpan CurrentPosition { get; }
        TimeSpan Duration { get; }

        void LoadFile(string filePath);
        Task LoadGrowingFileAsync(string tempPath, long totalBytes, CancellationToken ct = default);
        void Play();
        void Pause();
        void Stop();
        void SetPosition(TimeSpan position);
        void SetVolume(float volume);
    }
}
