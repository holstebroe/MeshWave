using MeshWave.Common.Core.Models;
using MeshWave.LibraryManager;
using MeshWave.Models;
using MeshWave.Mvvm;
using MeshWave.Services;
using MeshWave.Synchronizer;
using System.Collections.Generic;
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
    private readonly UserProfileService _profileService = new();
    private readonly MyMusicMetadataService _myMusicMetadataService = new();
    private string _coverImagePath = string.Empty;
    private float[] _waveformSamples = [];
    private string _trackDescription = string.Empty;
    private int _currentTrackVersion = 1;
    private bool _isMuted = false;
    private double _preMuteVolume = 1.0;
    private ObservableCollection<PlaybackTrackListItem> _albumTracks = [];
    private PlaybackTrackListItem? _selectedAlbumTrack;
    private string _trackContextTitle = "Current Album / Playlist";
    private string _trackContextIconPath = string.Empty;
    private WaveformStyle _waveformStyle = WaveformStyle.Filled;
    private readonly SyncOrchestrator? _sync;
    private readonly Dictionary<string, HashSet<string>> _importedCommentOperationIdsByPeer = new(StringComparer.OrdinalIgnoreCase);
    private bool _isCurrentTrackLikedByMe;

    public PlaybackViewModel(SyncOrchestrator? sync = null)
    {
        _sync = sync;

        PlayCommand = new RelayCommand(_ => Play());
        PauseCommand = new RelayCommand(_ => Pause());
        StopCommand = new RelayCommand(_ => Stop());
        PlayPauseToggleCommand = new RelayCommand(_ => PlayPauseToggle());
        PlayAlbumTrackCommand = new RelayCommand(param => PlayAlbumTrack(param as PlaybackTrackListItem));
        PreviousTrackCommand = new RelayCommand(_ => PlayPreviousTrack(), _ => CanGoToPreviousTrack);
        NextTrackCommand = new RelayCommand(_ => PlayNextTrack(), _ => CanGoToNextTrack);
        ToggleMuteCommand = new RelayCommand(_ => ToggleMute());

        if (_sync != null)
            _sync.ManifestMerged += OnManifestMerged;
    }

    public ICommand PlayCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand PlayPauseToggleCommand { get; }
    public ICommand PlayAlbumTrackCommand { get; }
    public ICommand PreviousTrackCommand { get; }
    public ICommand NextTrackCommand { get; }
    public ICommand ToggleMuteCommand { get; }

    public string PlayPauseIcon => IsPlaying ? "⏸" : "▶";

    public bool HasTrackLoaded => !string.IsNullOrWhiteSpace(_currentFilePath) && !string.IsNullOrWhiteSpace(CurrentTrackTitle);

    public WaveformStyle WaveformStyle
    {
        get => _waveformStyle;
        set => SetProperty(ref _waveformStyle, value);
    }

    public bool IsMuted
    {
        get => _isMuted;
        private set => SetProperty(ref _isMuted, value);
    }

    public string CurrentTrackTitle
    {
        get => _currentTrackTitle;
        set => SetProperty(ref _currentTrackTitle, value);
    }

    public bool IsOwnedTrack
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_currentFilePath))
                return false;

            var settings = new SettingsService().LoadSettings();
            if (string.IsNullOrWhiteSpace(settings.BaseFolder))
                return false;

            var myMusicRoot = Path.Combine(settings.BaseFolder, "My Music");
            return _currentFilePath.StartsWith(myMusicRoot, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Stable identifier for the current track (filename without extension).</summary>
    public string CurrentTrackId => _currentTrackId;

    /// <summary>Alias matching the ApplicationViewModel consumption pattern.</summary>
    public string TrackTitle => _currentTrackTitle;

    /// <summary>Alias matching the ApplicationViewModel consumption pattern.</summary>
    public string Artist => _currentArtist;

    public string CurrentArtist
    {
        get => _currentArtist;
        set => SetProperty(ref _currentArtist, value);
    }

    public bool IsCurrentTrackLikedByMe
    {
        get => _isCurrentTrackLikedByMe;
        private set => SetProperty(ref _isCurrentTrackLikedByMe, value);
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
        set
        {
            if (SetProperty(ref _isPlaying, value))
            {
                OnPropertyChanged(nameof(PlayPauseIcon));
            }
        }
    }

    public bool HasMultipleVersions
    {
        get
        {
            if (TimelineMarkers.Count == 0) return false;
            var versions = TimelineMarkers.Select(m => m.TrackVersion <= 0 ? 1 : m.TrackVersion).Distinct().ToList();
            return versions.Count > 1;
        }
    }

    public double Volume
    {
        get => _volume;
        set
        {
            var clamped = Math.Clamp(value, 0.0, 1.0);
            SetProperty(ref _volume, clamped);
            if (!IsMuted)
                _audioService?.SetVolume((float)clamped);
        }
    }

    private void ToggleMute()
    {
        if (IsMuted)
        {
            IsMuted = false;
            _audioService?.SetVolume((float)_volume);
        }
        else
        {
            _preMuteVolume = _volume;
            IsMuted = true;
            _audioService?.SetVolume(0f);
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

    public string TrackDescription
    {
        get => _trackDescription;
        set => SetProperty(ref _trackDescription, value);
    }

    public float[] WaveformSamples
    {
        get => _waveformSamples;
        set => SetProperty(ref _waveformSamples, value);
    }

    public int CurrentTrackVersion
    {
        get => _currentTrackVersion;
        set
        {
            if (SetProperty(ref _currentTrackVersion, value))
            {
                RebuildComments();
            }
        }
    }

    public ObservableCollection<PlaybackTrackListItem> AlbumTracks
    {
        get => _albumTracks;
        set => SetProperty(ref _albumTracks, value);
    }

    public PlaybackTrackListItem? SelectedAlbumTrack
    {
        get => _selectedAlbumTrack;
        set => SetProperty(ref _selectedAlbumTrack, value);
    }

    public string TrackContextTitle
    {
        get => _trackContextTitle;
        set => SetProperty(ref _trackContextTitle, value);
    }

    public string TrackContextIconPath
    {
        get => _trackContextIconPath;
        set => SetProperty(ref _trackContextIconPath, value);
    }

    private bool _showOnlyCurrentVersionComments;
    public bool ShowOnlyCurrentVersionComments
    {
        get => _showOnlyCurrentVersionComments;
        set
        {
            if (SetProperty(ref _showOnlyCurrentVersionComments, value))
            {
                RebuildComments();
            }
        }
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

    public void AddComment(string text, double? timestampSeconds = null, string? replyToId = null)
    {
        var timestamp = timestampSeconds.HasValue
            ? TimeSpan.FromSeconds(timestampSeconds.Value)
            : CurrentPosition;
        var profile = _profileService.LoadProfile();

        var marker = new TimelineCommentMarker
        {
            Id = Guid.NewGuid().ToString("N"),
            TimestampSeconds = timestamp.TotalSeconds,
            Label = text,
            UserDisplayName = string.IsNullOrWhiteSpace(profile.DisplayName) ? "You" : profile.DisplayName,
            UserIconPath = string.IsNullOrWhiteSpace(profile.AvatarIconPath) ? profile.AvatarImagePath : profile.AvatarIconPath,
            TrackVersion = CurrentTrackVersion,
            ReplyToId = replyToId
        };

        var syncedCommentOperationId = _sync?.RecordComment(
            trackId: CurrentTrackId,
            commentText: text,
            replyToId: replyToId,
            metadata: new Dictionary<string, string>
            {
                ["displayName"] = marker.UserDisplayName,
                ["iconPath"] = marker.UserIconPath,
                ["trackVersion"] = marker.TrackVersion.ToString(),
                ["timestampSeconds"] = marker.TimestampSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });

        if (!string.IsNullOrWhiteSpace(syncedCommentOperationId))
            marker.Id = syncedCommentOperationId;

        TimelineMarkers.Add(marker);
        OnPropertyChanged(nameof(TimelineMarkers));
        RebuildComments();
        SaveTimelineMarkers();
    }

    public void SetAlbumTrackContext(IEnumerable<PlaybackTrackListItem> tracks, string? selectedTrackId = null, string? contextTitle = null, string? contextIconPath = null)
    {
        AlbumTracks = new ObservableCollection<PlaybackTrackListItem>(tracks);
        TrackContextTitle = string.IsNullOrWhiteSpace(contextTitle) ? "Current Album / Playlist" : contextTitle;
        TrackContextIconPath = contextIconPath ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(selectedTrackId))
        {
            SelectedAlbumTrack = AlbumTracks.FirstOrDefault(t => string.Equals(t.TrackId, selectedTrackId, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void LoadTrack(string trackTitle, string artist, TimeSpan duration, string? filePath = null, bool autoPlay = true, bool incrementPlayCount = true)
    {
        CurrentTrackTitle = trackTitle;
        CurrentArtist = artist;
        Duration = duration;
        CurrentPosition = TimeSpan.Zero;
        _currentFilePath = filePath;
        _currentTrackId = Path.GetFileNameWithoutExtension(filePath ?? string.Empty) ?? string.Empty;
        RefreshCurrentTrackLikeState();

        if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
        {
            var coverResolver = new LocalLibraryManager(Path.GetDirectoryName(filePath) ?? string.Empty);
            CoverImagePath = coverResolver.GetTrackCoverPath(filePath);
            WaveformSamples = coverResolver.GetTrackWaveform(filePath);

            var myMusicMeta = _myMusicMetadataService.LoadForTrack(filePath);
            TrackDescription = string.IsNullOrWhiteSpace(myMusicMeta.Description)
                ? string.Empty
                : myMusicMeta.Description;
            CurrentTrackVersion = myMusicMeta.Version <= 0 ? 1 : myMusicMeta.Version;
            OnPropertyChanged(nameof(IsOwnedTrack));

            if (incrementPlayCount)
                _myMusicMetadataService.IncrementPlayCount(filePath);

            LoadTimelineMarkers();
            SyncCommentsFromPeerManifests();

            _audioService?.Dispose();
            _audioService = new AudioPlaybackService();
            _audioService.PositionChanged += (s, pos) =>
            {
                _isUpdatingPosition = true;
                SetProperty(ref _currentPosition, pos, nameof(CurrentPosition));
                _isUpdatingPosition = false;
            };
            _audioService.PlaybackStopped += (s, e) =>
            {
                // In NAudio, natural completion triggers PlaybackStopped with a clean exit code.
                // Manual stops also trigger this. We use IsPlaying to guard state.
                if (!IsPlaying) return;

                IsPlaying = false;

                // Ensure the playhead is at the very end when playing stops naturally
                if (Duration > TimeSpan.Zero)
                {
                    CurrentPosition = Duration;
                }

                // Auto-advance to the next track if we were playing and reached the end
                if (CanGoToNextTrack)
                {
                    PlayNextTrack();
                }
            };
            _audioService.LoadFile(filePath);
            Duration = _audioService.Duration;
            if (autoPlay)
                Play();
            else
                Pause();

            var remapped = AlbumTracks.Select(t => new PlaybackTrackListItem
            {
                TrackId = t.TrackId,
                Title = t.Title,
                Artist = t.Artist,
                Duration = t.Duration,
                FilePath = t.FilePath,
                TrackNumber = t.TrackNumber,
                PlayCount = t.PlayCount,
                IsNowPlaying = string.Equals(t.FilePath, filePath, StringComparison.OrdinalIgnoreCase)
            }).ToList();
            AlbumTracks = new ObservableCollection<PlaybackTrackListItem>(remapped);
            SelectedAlbumTrack = AlbumTracks.FirstOrDefault(t => string.Equals(t.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

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
            TrackDescription = string.Empty;
            CurrentTrackVersion = 1;
            IsCurrentTrackLikedByMe = false;
            OnPropertyChanged(nameof(IsOwnedTrack));
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
            RebuildComments();   // clear the displayed comments list when there is no file
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
            RebuildComments();
        }
        catch
        {
            RebuildComments();   // still clear stale comments on error
        }
    }

    private void PlayAlbumTrack(PlaybackTrackListItem? item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.FilePath))
        {
            return;
        }

        LoadTrack(item.Title, item.Artist, item.Duration, item.FilePath);
    }

    public bool CanGoToPreviousTrack => AlbumTracks.Count > 1 && CurrentTrackIndex > 0;
    public bool CanGoToNextTrack => AlbumTracks.Count > 1 && CurrentTrackIndex < AlbumTracks.Count - 1;

    private int CurrentTrackIndex =>
        SelectedAlbumTrack is null ? -1 : AlbumTracks.IndexOf(SelectedAlbumTrack);

    private void PlayPreviousTrack()
    {
        var idx = CurrentTrackIndex;
        if (idx > 0)
            PlayAlbumTrack(AlbumTracks[idx - 1]);
    }

    private void PlayNextTrack()
    {
        var idx = CurrentTrackIndex;
        if (idx >= 0 && idx < AlbumTracks.Count - 1)
            PlayAlbumTrack(AlbumTracks[idx + 1]);
    }

    private void RebuildComments()
    {
        OnPropertyChanged(nameof(HasMultipleVersions));

        var visibleMarkers = (ShowOnlyCurrentVersionComments
            ? TimelineMarkers.Where(m => (m.TrackVersion <= 0 ? 1 : m.TrackVersion) == CurrentTrackVersion)
            : TimelineMarkers).ToList();

        var lines = new List<string>();
        foreach (var m in visibleMarkers.Where(m => string.IsNullOrEmpty(m.ReplyToId)))
        {
            lines.Add($"[{TimeSpan.FromSeconds(m.TimestampSeconds):mm\\:ss}] (v{(m.TrackVersion <= 0 ? 1 : m.TrackVersion)}) {m.UserDisplayName}: {m.Label}");
            foreach (var r in visibleMarkers.Where(r => r.ReplyToId == m.Id))
            {
                lines.Add($"  ↳ [{TimeSpan.FromSeconds(r.TimestampSeconds):mm\\:ss}] {r.UserDisplayName}: {r.Label}");
            }
        }

        Comments = new ObservableCollection<string>(lines);
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

    private void OnManifestMerged(object? sender, ManifestMergedEventArgs e)
    {
        if (_sync == null || string.IsNullOrWhiteSpace(CurrentTrackId))
            return;

        var manifest = _sync.GetPeerManifest(e.UserId);
        if (manifest == null)
            return;

        var changed = ApplyPeerComments(manifest);
        if (!changed)
            return;

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            OnPropertyChanged(nameof(TimelineMarkers));
            RebuildComments();
            SaveTimelineMarkers();
        });
    }

    private void SyncCommentsFromPeerManifests()
    {
        if (_sync == null || string.IsNullOrWhiteSpace(CurrentTrackId))
            return;

        var changed = false;
        foreach (var manifest in _sync.PeerManifests)
            changed |= ApplyPeerComments(manifest);

        if (!changed)
            return;

        OnPropertyChanged(nameof(TimelineMarkers));
        RebuildComments();
        SaveTimelineMarkers();
    }

    private bool ApplyPeerComments(Manifest manifest)
    {
        if (string.IsNullOrWhiteSpace(CurrentTrackId))
            return false;

        var imported = _importedCommentOperationIdsByPeer.GetValueOrDefault(manifest.UserId);
        if (imported == null)
        {
            imported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _importedCommentOperationIdsByPeer[manifest.UserId] = imported;
        }

        var changed = false;
        foreach (var op in manifest.Operations.OrderBy(o => o.SequenceNumber))
        {
            if (imported.Contains(op.OperationId))
                continue;

            if (!string.Equals(op.TargetType, "Track", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(op.TargetId, CurrentTrackId, StringComparison.OrdinalIgnoreCase))
            {
                imported.Add(op.OperationId);
                continue;
            }

            if (op.OperationType == ManifestOperationType.Comment)
            {
                var text = op.Metadata.GetValueOrDefault("text");
                if (!string.IsNullOrWhiteSpace(text) && TimelineMarkers.All(m => !string.Equals(m.Id, op.OperationId, StringComparison.OrdinalIgnoreCase)))
                {
                    var parsedVersion = int.TryParse(op.Metadata.GetValueOrDefault("trackVersion"), out var version)
                        ? version
                        : CurrentTrackVersion;

                    var marker = new TimelineCommentMarker
                    {
                        Id = op.OperationId,
                        TimestampSeconds = ParseDouble(op.Metadata.GetValueOrDefault("timestampSeconds"), op.Timestamp),
                        Label = text,
                        UserDisplayName = op.Metadata.GetValueOrDefault("displayName") ?? manifest.UserId,
                        UserIconPath = op.Metadata.GetValueOrDefault("iconPath") ?? string.Empty,
                        TrackVersion = parsedVersion <= 0 ? 1 : parsedVersion,
                        ReplyToId = op.Metadata.GetValueOrDefault("replyToId")
                    };

                    TimelineMarkers.Add(marker);
                    changed = true;
                }
            }
            else if (op.OperationType == ManifestOperationType.CommentDelete)
            {
                var commentOperationId = op.Metadata.GetValueOrDefault("commentOperationId");
                if (!string.IsNullOrWhiteSpace(commentOperationId))
                {
                    var existing = TimelineMarkers.FirstOrDefault(m => string.Equals(m.Id, commentOperationId, StringComparison.OrdinalIgnoreCase));
                    if (existing != null)
                    {
                        TimelineMarkers.Remove(existing);
                        changed = true;
                    }
                }
            }

            imported.Add(op.OperationId);
        }

        return changed;
    }

    public void ToggleLikeCurrentTrack()
    {
        if (_sync == null || string.IsNullOrWhiteSpace(CurrentTrackId))
            return;

        if (IsCurrentTrackLikedByMe)
        {
            _sync.RecordUnlike(CurrentTrackId);
            IsCurrentTrackLikedByMe = false;
        }
        else
        {
            _sync.RecordLike(CurrentTrackId);
            IsCurrentTrackLikedByMe = true;
        }
    }

    private void RefreshCurrentTrackLikeState()
    {
        if (_sync?.LocalManifest == null || string.IsNullOrWhiteSpace(CurrentTrackId))
        {
            IsCurrentTrackLikedByMe = false;
            return;
        }

        var lastLikeState = _sync.LocalManifest.Operations
            .Where(op => string.Equals(op.TargetType, "Track", StringComparison.OrdinalIgnoreCase)
                && string.Equals(op.TargetId, CurrentTrackId, StringComparison.OrdinalIgnoreCase)
                && (op.OperationType == ManifestOperationType.Like || op.OperationType == ManifestOperationType.Unlike))
            .OrderBy(op => op.SequenceNumber)
            .LastOrDefault();

        IsCurrentTrackLikedByMe = lastLikeState?.OperationType == ManifestOperationType.Like;
    }

    private static double ParseDouble(string? value, DateTime timestamp)
    {
        if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            return parsed;

        return Math.Max(0, timestamp.ToUniversalTime().TimeOfDay.TotalSeconds);
    }

    public PlaybackResumeState BuildResumeState()
    {
        var contextTracks = AlbumTracks
            .Where(t => !string.IsNullOrWhiteSpace(t.FilePath))
            .Select(t => new PlaybackResumeTrack
            {
                TrackId = t.TrackId,
                Title = t.Title,
                Artist = t.Artist,
                DurationSeconds = t.Duration.TotalSeconds,
                FilePath = t.FilePath,
                TrackNumber = t.TrackNumber,
                PlayCount = t.PlayCount
            })
            .ToList();

        return new PlaybackResumeState
        {
            TrackFilePath = _currentFilePath ?? string.Empty,
            TrackTitle = CurrentTrackTitle,
            Artist = CurrentArtist,
            DurationSeconds = Duration.TotalSeconds,
            PositionSeconds = CurrentPosition.TotalSeconds,
            WasPlaying = IsPlaying,
            SelectedTrackId = SelectedAlbumTrack?.TrackId ?? CurrentTrackId,
            ContextTitle = TrackContextTitle,
            ContextIconPath = TrackContextIconPath,
            ContextTracks = contextTracks
        };
    }

    public void RestoreFromResumeState(PlaybackResumeState? state)
    {
        if (state == null || string.IsNullOrWhiteSpace(state.TrackFilePath) || !File.Exists(state.TrackFilePath))
            return;

        var context = state.ContextTracks
            .Where(t => !string.IsNullOrWhiteSpace(t.FilePath) && File.Exists(t.FilePath))
            .Select(t => new PlaybackTrackListItem
            {
                TrackId = t.TrackId,
                Title = t.Title,
                Artist = t.Artist,
                Duration = TimeSpan.FromSeconds(Math.Max(0, t.DurationSeconds)),
                FilePath = t.FilePath,
                TrackNumber = t.TrackNumber,
                PlayCount = t.PlayCount
            })
            .ToList();

        if (context.Count > 0)
            SetAlbumTrackContext(context, state.SelectedTrackId, state.ContextTitle, state.ContextIconPath);

        var duration = state.DurationSeconds > 0
            ? TimeSpan.FromSeconds(state.DurationSeconds)
            : TimeSpan.FromMinutes(3);

        LoadTrack(
            string.IsNullOrWhiteSpace(state.TrackTitle) ? Path.GetFileNameWithoutExtension(state.TrackFilePath) : state.TrackTitle,
            string.IsNullOrWhiteSpace(state.Artist) ? "Unknown Artist" : state.Artist,
            duration,
            state.TrackFilePath,
            autoPlay: false,
            incrementPlayCount: false);

        var targetPosition = TimeSpan.FromSeconds(Math.Max(0, state.PositionSeconds));
        if (Duration.TotalSeconds > 1)
        {
            var maxPosition = Duration - TimeSpan.FromMilliseconds(250);
            if (maxPosition < TimeSpan.Zero)
                maxPosition = TimeSpan.Zero;

            if (targetPosition > maxPosition)
                targetPosition = maxPosition;
        }

        if (targetPosition > TimeSpan.Zero)
            Seek(targetPosition);

        if (state.WasPlaying)
            Play();
        else
            Pause();
    }

    public void Dispose()
    {
        if (_sync != null)
            _sync.ManifestMerged -= OnManifestMerged;

        _audioService?.Dispose();
    }
}

public sealed class TimelineCommentMarker
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public double TimestampSeconds { get; set; }
    public string Label { get; set; } = string.Empty;
    public string UserDisplayName { get; set; } = string.Empty;
    public string UserIconPath { get; set; } = string.Empty;
    public int TrackVersion { get; set; } = 1;
    /// <summary>Id of the marker this is a reply to, or null/empty for top-level comments.</summary>
    public string? ReplyToId { get; set; }
}

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
