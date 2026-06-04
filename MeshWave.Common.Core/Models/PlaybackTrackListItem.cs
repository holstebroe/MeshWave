using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MeshWave.Common.Core.Models;

public sealed class PlaybackTrackListItem : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private string _artist = string.Empty;
    private bool _isNowPlaying;

    public string TrackId { get; set; } = string.Empty;
    public string Title { get => _title; set => SetField(ref _title, value); }
    public string Artist { get => _artist; set => SetField(ref _artist, value); }
    public TimeSpan Duration { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public int TrackNumber { get; set; }
    public string TrackNumberLabel => TrackNumber > 0 ? $"{TrackNumber}" : "-";
    public bool IsNowPlaying { get => _isNowPlaying; set => SetField(ref _isNowPlaying, value); }
    public int PlayCount { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
