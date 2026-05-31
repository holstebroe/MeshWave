namespace MeshWave.Common.Core.Models;

public class PlaybackResumeState
{
    public string TrackFilePath { get; set; } = string.Empty;
    public string TrackTitle { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public double DurationSeconds { get; set; }
    public double PositionSeconds { get; set; }
    public bool WasPlaying { get; set; }
    public string SelectedTrackId { get; set; } = string.Empty;
    public string ContextTitle { get; set; } = string.Empty;
    public string ContextIconPath { get; set; } = string.Empty;
    public List<PlaybackResumeTrack> ContextTracks { get; set; } = [];
}

public class PlaybackResumeTrack
{
    public string TrackId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public double DurationSeconds { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public int TrackNumber { get; set; }
    public int PlayCount { get; set; }
}
