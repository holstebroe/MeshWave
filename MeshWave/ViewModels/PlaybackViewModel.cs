using MeshWave.LibraryManager;
using MeshWave.Mvvm;
using MeshWave.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;

namespace MeshWave.ViewModels;

/// <summary>
/// View model for music playback with waveform visualization.
/// </summary>
public class PlaybackViewModel : ViewModelBase, IDisposable
{
    private string _currentTrackTitle = string.Empty;
    private string _currentArtist = string.Empty;
    private string _currentTrackId = string.Empty;
    private TimeSpan _currentPosition;
    private TimeSpan _duration;
    private bool _isPlaying = false;
    private double _volume = 1.0;
    private ObservableCollection<string> _comments = [];
    private ObservableCollection<TimelineCommentMarker> _timelineMarkers = [];
    private AudioPlaybackService? _audioService;
    private string? _currentFilePath;
    private bool _isUpdatingPosition = false;
    private string _coverImagePath = string.Empty;
    private float[] _waveformSamples = [];

    public PlaybackViewModel()
    {
        PlayCommand = new RelayCommand(_ => Play());
        PauseCommand = new RelayCommand(_ => Pause());
        StopCommand = new RelayCommand(_ => Stop());
        PlayPauseToggleCommand = new RelayCommand(_ => PlayPauseToggle());
    }

    public ICommand PlayCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand PlayPauseToggleCommand { get; }

    public string CurrentTrackTitle
    {
        get => _currentTrackTitle;
        set => SetProperty(ref _currentTrackTitle, value);
    }

    public string CurrentArtist
    {
        get => _currentArtist;
        set => SetProperty(ref _currentArtist, value);
    }

    public TimeSpan CurrentPosition
    {
        get => _currentPosition;
        set
        {
            if (_isUpdatingPosition) return;

            _isUpdatingPosition = true;
            SetProperty(ref _currentPosition, value);
            _audioService?.SetPosition(value);
            _isUpdatingPosition = false;
        }
    }

    public TimeSpan Duration
    {
        get => _duration;
        set => SetProperty(ref _duration, value);
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set => SetProperty(ref _isPlaying, value);
    }

    public double Volume
    {
        get => _volume;
        set
        {
            SetProperty(ref _volume, Math.Clamp(value, 0.0, 1.0));
            _audioService?.SetVolume((float)_volume);
        }
    }

    public ObservableCollection<string> Comments
    {
        get => _comments;
        set => SetProperty(ref _comments, value);
    }

    public ObservableCollection<TimelineCommentMarker> TimelineMarkers
    {
        get => _timelineMarkers;
        set => SetProperty(ref _timelineMarkers, value);
    }

    public string CoverImagePath
    {
        get => _coverImagePath;
        set => SetProperty(ref _coverImagePath, value);
    }

    public float[] WaveformSamples
    {
        get => _waveformSamples;
        set => SetProperty(ref _waveformSamples, value);
    }

    public void Play()
    {
        if (_audioService != null)
        {
            _audioService.Play();
            IsPlaying = true;
        }
    }

    public void Pause()
    {
        if (_audioService != null)
        {
            _audioService.Pause();
            IsPlaying = false;
        }
    }

    public void PlayPauseToggle()
    {
        if (IsPlaying)
        {
            Pause();
        }
        else
        {
            Play();
        }
    }

    public void Stop()
    {
        if (_audioService != null)
        {
            _audioService.Stop();
            IsPlaying = false;
            CurrentPosition = TimeSpan.Zero;
        }
    }

    public void Seek(TimeSpan position)
    {
        _audioService?.SetPosition(position);
        CurrentPosition = position;
    }

    public void AddComment(string text, double? timestampSeconds = null)
    {
        var timestamp = timestampSeconds.HasValue
            ? TimeSpan.FromSeconds(timestampSeconds.Value)
            : CurrentPosition;
        var comment = $"[{timestamp:mm\\:ss}] {text}";
        Comments.Add(comment);

        var marker = new TimelineCommentMarker
        {
            TimestampSeconds = timestamp.TotalSeconds,
            Label = text,
            UserDisplayName = "You",
            UserIconPath = CoverImagePath
        };
        TimelineMarkers.Add(marker);
        OnPropertyChanged(nameof(TimelineMarkers));
        SaveTimelineMarkers();
    }

    public void LoadTrack(string trackTitle, string artist, TimeSpan duration, string? filePath = null)
    {
        CurrentTrackTitle = trackTitle;
        CurrentArtist = artist;
        Duration = duration;
        CurrentPosition = TimeSpan.Zero;
        _currentFilePath = filePath;
        _currentTrackId = Path.GetFileNameWithoutExtension(filePath ?? string.Empty) ?? string.Empty;

        if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
        {
            var coverResolver = new LocalLibraryManager(Path.GetDirectoryName(filePath) ?? string.Empty);
            CoverImagePath = coverResolver.GetTrackCoverPath(filePath);
            WaveformSamples = coverResolver.GetTrackWaveform(filePath);
            LoadTimelineMarkers();

            _audioService?.Dispose();
            _audioService = new AudioPlaybackService();
            _audioService.PositionChanged += (s, pos) =>
            {
                _isUpdatingPosition = true;
                SetProperty(ref _currentPosition, pos, nameof(CurrentPosition));
                _isUpdatingPosition = false;
            };
            _audioService.PlaybackStopped += (s, e) => IsPlaying = false;
            _audioService.LoadFile(filePath);
            Duration = _audioService.Duration;
            Play();

            if (WaveformSamples.Length == 0)
            {
                _ = Task.Run(() => GenerateWaveformInBackground(filePath));
            }
        }
        else
        {
            CoverImagePath = string.Empty;
            WaveformSamples = [];
            TimelineMarkers.Clear();
        }
    }

    private void GenerateWaveformInBackground(string filePath)
    {
        try
        {
            var samples = WaveformService.GenerateWaveform(filePath, 1024);
            if (samples.Length == 0)
            {
                return;
            }

            var cachePath = LocalLibraryManager.GetWaveformCachePathForTrack(filePath);
            var cacheFolder = Path.GetDirectoryName(cachePath);
            if (!string.IsNullOrWhiteSpace(cacheFolder))
            {
                Directory.CreateDirectory(cacheFolder);
            }

            var json = JsonSerializer.Serialize(samples);
            File.WriteAllText(cachePath, json);

            if (string.Equals(_currentFilePath, filePath, StringComparison.OrdinalIgnoreCase))
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    WaveformSamples = samples;
                });
            }
        }
        catch
        {
            // ignore waveform generation failures
        }
    }

    private string GetTimelineMarkerPath()
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath))
        {
            return string.Empty;
        }

        var albumFolder = Path.GetDirectoryName(_currentFilePath) ?? string.Empty;
        return Path.Combine(albumFolder, ".comments", $"{_currentTrackId}.timeline.json");
    }

    private void LoadTimelineMarkers()
    {
        TimelineMarkers.Clear();

        var markerPath = GetTimelineMarkerPath();
        if (string.IsNullOrWhiteSpace(markerPath) || !File.Exists(markerPath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(markerPath);
            var markers = JsonSerializer.Deserialize<List<TimelineCommentMarker>>(json) ?? [];
            foreach (var marker in markers)
            {
                TimelineMarkers.Add(marker);
            }

            OnPropertyChanged(nameof(TimelineMarkers));
        }
        catch
        {
            // ignore marker load failures
        }
    }

    private void SaveTimelineMarkers()
    {
        var markerPath = GetTimelineMarkerPath();
        if (string.IsNullOrWhiteSpace(markerPath))
        {
            return;
        }

        try
        {
            var folder = Path.GetDirectoryName(markerPath);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                Directory.CreateDirectory(folder);
            }

            var json = JsonSerializer.Serialize(TimelineMarkers.ToList());
            File.WriteAllText(markerPath, json);
        }
        catch
        {
            // ignore marker save failures
        }
    }

    public void Dispose()
    {
        _audioService?.Dispose();
    }
}

public sealed class TimelineCommentMarker
{
    public double TimestampSeconds { get; set; }
    public string Label { get; set; } = string.Empty;
    public string UserDisplayName { get; set; } = string.Empty;
    public string UserIconPath { get; set; } = string.Empty;
}
