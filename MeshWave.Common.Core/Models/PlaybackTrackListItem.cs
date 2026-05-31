namespace MeshWave.Common.Core.Models;

public sealed class PlaybackTrackListItem
{
    public string TrackId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public int TrackNumber { get; set; }
    public string TrackNumberLabel => TrackNumber > 0 ? $"{TrackNumber}" : "-";
    public bool IsNowPlaying { get; set; }
    public int PlayCount { get; set; }
}
