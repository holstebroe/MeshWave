using MeshWave.Mvvm;

namespace MeshWave.ViewModels;

/// <summary>
/// View model for music playback with waveform visualization.
/// </summary>
public class PlaybackViewModel : ViewModelBase
{
    private string _currentTrackTitle = string.Empty;
    private string _currentArtist = string.Empty;
    private TimeSpan _currentPosition;
    private TimeSpan _duration;
    private bool _isPlaying = false;
    private double _volume = 1.0;
    private List<string> _comments = [];

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
        set => SetProperty(ref _currentPosition, value);
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
        set => SetProperty(ref _volume, Math.Clamp(value, 0.0, 1.0));
    }

    public List<string> Comments
    {
        get => _comments;
        set => SetProperty(ref _comments, value);
    }

    public void Play()
    {
        // TODO: Implement audio playback
        IsPlaying = true;
    }

    public void Pause()
    {
        // TODO: Implement audio pause
        IsPlaying = false;
    }

    public void Seek(TimeSpan position)
    {
        // TODO: Implement seek
        CurrentPosition = position;
    }

    public void AddComment(string text, double? timestampSeconds = null)
    {
        // TODO: Implement comment addition
    }
}
