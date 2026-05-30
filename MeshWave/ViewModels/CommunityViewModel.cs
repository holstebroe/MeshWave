using MeshWave.Common.Core.Models;
using MeshWave.LibraryManager;
using MeshWave.Mvvm;
using MeshWave.Services;
using MeshWave.Synchronizer;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Input;

namespace MeshWave.ViewModels;

/// <summary>
/// View model for the Community view — user/group discovery, follows, friends and group membership.
/// </summary>
public class CommunityViewModel : ViewModelBase
{
    private readonly SyncOrchestrator? _sync;
    private readonly SettingsService _settingsService = new();

    private string _searchQuery = string.Empty;
    private string _searchStatus = string.Empty;
    private bool _isSearching;
    private CommunityTab _activeTab = CommunityTab.Feed;
    private ObservableCollection<CommunityUserItem> _searchResults = [];
    private ObservableCollection<CommunityGroupItem> _groupResults = [];
    private ObservableCollection<CommunityUserItem> _friends = [];
    private ObservableCollection<CommunityUserItem> _following = [];
    private ObservableCollection<CommunityGroupItem> _myGroups = [];
    private ObservableCollection<ReleaseFeedItem> _releaseFeed = [];
    private int _newReleaseCount;
    private readonly Dictionary<string, int> _lastFeedReleaseSequenceByPeer = new(StringComparer.OrdinalIgnoreCase);

    public CommunityViewModel(SyncOrchestrator? sync = null)
    {
        _sync = sync;

        SearchCommand = new RelayCommand(_ => Search(), _ => !IsSearching && !string.IsNullOrWhiteSpace(SearchQuery));
        FollowUserCommand = new RelayCommand<CommunityUserItem>(FollowUser, u => u != null && !u.IsFollowing);
        UnfollowUserCommand = new RelayCommand<CommunityUserItem>(UnfollowUser, u => u != null && u.IsFollowing);
        AddFriendCommand = new RelayCommand<CommunityUserItem>(AddFriend, u => u != null && !u.IsFriend);
        RemoveFriendCommand = new RelayCommand<CommunityUserItem>(RemoveFriend, u => u != null && u.IsFriend);
        JoinGroupCommand = new RelayCommand<CommunityGroupItem>(JoinGroup, g => g != null && !g.IsMember);
        LeaveGroupCommand = new RelayCommand<CommunityGroupItem>(LeaveGroup, g => g != null && g.IsMember);
        SetTabCommand = new RelayCommand<string>(tab =>
        {
            ActiveTab = Enum.Parse<CommunityTab>(tab);
            if (ActiveTab == CommunityTab.Feed)
                NewReleaseCount = 0;   // clear badge when user opens the Feed tab
        });
        RefreshFeedCommand = new RelayCommand(_ => RefreshFeed());
        AddToLibraryCommand = new RelayCommand<ReleaseFeedItem>(AddToLibrary, r => r != null && !string.IsNullOrWhiteSpace(r.ContentHash));

        if (_sync != null)
        {
            _sync.ManifestMerged += OnManifestMerged;
            RefreshFeed();
        }
    }

    public ICommand SearchCommand { get; }
    public ICommand FollowUserCommand { get; }
    public ICommand UnfollowUserCommand { get; }
    public ICommand AddFriendCommand { get; }
    public ICommand RemoveFriendCommand { get; }
    public ICommand JoinGroupCommand { get; }
    public ICommand LeaveGroupCommand { get; }
    public ICommand SetTabCommand { get; }
    public ICommand RefreshFeedCommand { get; }
    public ICommand AddToLibraryCommand { get; }

    /// <summary>Count of new releases from followed peers since the Feed tab was last viewed.</summary>
    public int NewReleaseCount
    {
        get => _newReleaseCount;
        private set
        {
            SetProperty(ref _newReleaseCount, value);
            OnPropertyChanged(nameof(HasNewReleases));
        }
    }

    /// <summary>True when there is at least one unseen release — drives the badge dot in the nav.</summary>
    public bool HasNewReleases => _newReleaseCount > 0;

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            SetProperty(ref _searchQuery, value);
            OnPropertyChanged(nameof(CanSearch));
        }
    }

    public string SearchStatus
    {
        get => _searchStatus;
        private set => SetProperty(ref _searchStatus, value);
    }

    public bool IsSearching
    {
        get => _isSearching;
        private set
        {
            SetProperty(ref _isSearching, value);
            OnPropertyChanged(nameof(CanSearch));
        }
    }

    public bool CanSearch => !IsSearching && !string.IsNullOrWhiteSpace(SearchQuery);

    public CommunityTab ActiveTab
    {
        get => _activeTab;
        set
        {
            SetProperty(ref _activeTab, value);
            OnPropertyChanged(nameof(IsTabFeed));
            OnPropertyChanged(nameof(IsTabDiscover));
            OnPropertyChanged(nameof(IsTabFriends));
            OnPropertyChanged(nameof(IsTabFollowing));
            OnPropertyChanged(nameof(IsTabGroups));
        }
    }

    public bool IsTabFeed     => ActiveTab == CommunityTab.Feed;
    public bool IsTabDiscover  => ActiveTab == CommunityTab.Discover;
    public bool IsTabFriends   => ActiveTab == CommunityTab.Friends;
    public bool IsTabFollowing => ActiveTab == CommunityTab.Following;
    public bool IsTabGroups    => ActiveTab == CommunityTab.Groups;

    public ObservableCollection<CommunityUserItem> SearchResults
    {
        get => _searchResults;
        private set => SetProperty(ref _searchResults, value);
    }

    public ObservableCollection<CommunityGroupItem> GroupResults
    {
        get => _groupResults;
        private set => SetProperty(ref _groupResults, value);
    }

    public ObservableCollection<CommunityUserItem> Friends
    {
        get => _friends;
        private set => SetProperty(ref _friends, value);
    }

    public ObservableCollection<CommunityUserItem> Following
    {
        get => _following;
        private set => SetProperty(ref _following, value);
    }

    public ObservableCollection<CommunityGroupItem> MyGroups
    {
        get => _myGroups;
        private set => SetProperty(ref _myGroups, value);
    }

    public ObservableCollection<ReleaseFeedItem> ReleaseFeed
    {
        get => _releaseFeed;
        private set => SetProperty(ref _releaseFeed, value);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Release feed
    // ──────────────────────────────────────────────────────────────────────

    private void RefreshFeed()
    {
        if (_sync == null)
        {
            ReleaseFeed = [];
            SearchStatus = "Connect to the mesh to load releases.";
            return;
        }

        var followedIds = Following.Select(u => u.UserId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (followedIds.Count == 0)
        {
            ReleaseFeed = [];
            SearchStatus = "Follow artists to see releases in your feed.";
            return;
        }

        var releaseItems = new List<ReleaseFeedItem>();

        foreach (var manifest in _sync.PeerManifests.Where(m => followedIds.Contains(m.UserId)))
        {
            var profile = Following.FirstOrDefault(u => string.Equals(u.UserId, manifest.UserId, StringComparison.OrdinalIgnoreCase));
            var createOps = manifest.Operations
                .Where(op => op.OperationType == ManifestOperationType.Create)
                .OrderByDescending(op => op.SequenceNumber)
                .ToList();

            var maxSequence = createOps.Count > 0 ? createOps[0].SequenceNumber : 0;
            _lastFeedReleaseSequenceByPeer[manifest.UserId] = Math.Max(_lastFeedReleaseSequenceByPeer.GetValueOrDefault(manifest.UserId, 0), maxSequence);

            releaseItems.AddRange(createOps.Select(op => new ReleaseFeedItem
            {
                ArtistUserId = manifest.UserId,
                ArtistDisplayName = profile?.DisplayName ?? manifest.UserId,
                ArtistAvatarIconPath = profile?.AvatarIconPath ?? string.Empty,
                Title = op.Metadata.TryGetValue("title", out var title)
                    ? title
                    : op.Metadata.TryGetValue("name", out var name)
                        ? name
                        : $"{op.TargetType} release",
                TargetType = op.TargetType,
                TargetId = op.TargetId,
                ContentHash = op.ContentHash,
                ReleasedAt = op.Metadata.TryGetValue("releasedAt", out var releasedAt)
                    && DateTime.TryParse(releasedAt, out var parsedRelease)
                    ? parsedRelease
                    : op.Timestamp
            }));
        }

        ReleaseFeed = new ObservableCollection<ReleaseFeedItem>(releaseItems
            .OrderByDescending(r => r.ReleasedAt)
            .ThenByDescending(r => r.Title, StringComparer.OrdinalIgnoreCase));

        SearchStatus = ReleaseFeed.Count == 0
            ? "No releases found yet from followed artists."
            : $"Loaded {ReleaseFeed.Count} release{(ReleaseFeed.Count == 1 ? string.Empty : "s")} from followed artists.";
    }

    // ──────────────────────────────────────────────────────────────────────
    // Search
    // ──────────────────────────────────────────────────────────────────────

    private void Search()
    {
        // TODO (Milestone D): query P2P manifest store for peers matching SearchQuery
        // For now show a placeholder status so the UI is exercisable.
        IsSearching = true;
        SearchStatus = "Searching… (P2P search not yet connected)";
        SearchResults = [];
        GroupResults = [];

        // Simulate async: reset after brief delay using a background task
        _ = Task.Delay(600).ContinueWith(_ =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                IsSearching = false;
                SearchStatus = $"No results for \"{SearchQuery}\" — peer search will be available once connected to the mesh.";
            });
        });
    }

    // ──────────────────────────────────────────────────────────────────────
    // Follow / Friend / Group actions
    // ──────────────────────────────────────────────────────────────────────

    private void FollowUser(CommunityUserItem? user)
    {
        if (user == null) return;
        user.IsFollowing = true;
        if (!Following.Contains(user))
            Following.Add(user);
        _sync?.RecordFollow(user.UserId);
        RefreshFeed();
    }

    private void UnfollowUser(CommunityUserItem? user)
    {
        if (user == null) return;
        user.IsFollowing = false;
        Following.Remove(user);
        _sync?.RecordUnfollow(user.UserId);
        RefreshFeed();
    }

    private void AddFriend(CommunityUserItem? user)
    {
        if (user == null) return;
        user.IsFriend = true;
        if (!Friends.Contains(user))
            Friends.Add(user);
        // TODO (Milestone D): append signed bilateral "friend-request" manifest op
    }

    private void RemoveFriend(CommunityUserItem? user)
    {
        if (user == null) return;
        user.IsFriend = false;
        Friends.Remove(user);
        // TODO (Milestone D): append signed "unfriend" manifest op
    }

    private void JoinGroup(CommunityGroupItem? group)
    {
        if (group == null) return;
        group.IsMember = true;
        if (!MyGroups.Contains(group))
            MyGroups.Add(group);
        // TODO (Milestone D): append signed "join-group" manifest op
    }

    private void LeaveGroup(CommunityGroupItem? group)
    {
        if (group == null) return;
        group.IsMember = false;
        MyGroups.Remove(group);
        // TODO (Milestone D): append signed "leave-group" manifest op
    }

    // ──────────────────────────────────────────────────────────────────────
    // Follow notifications
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called when a peer manifest is merged. Checks for new Create ops from followed peers
    /// and increments the badge counter when the Feed tab is not currently visible.
    /// </summary>
    private void OnManifestMerged(object? sender, ManifestMergedEventArgs e)
    {
        if (_sync == null) return;
        var followedIds = Following.Select(u => u.UserId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!followedIds.Contains(e.UserId)) return;

        var manifest = _sync.GetPeerManifest(e.UserId);
        if (manifest == null) return;

        var latestCreateSequence = manifest.Operations
            .Where(op => op.OperationType == ManifestOperationType.Create)
            .Select(op => op.SequenceNumber)
            .DefaultIfEmpty(0)
            .Max();

        var lastKnown = _lastFeedReleaseSequenceByPeer.GetValueOrDefault(e.UserId, 0);
        if (latestCreateSequence > lastKnown)
        {
            _lastFeedReleaseSequenceByPeer[e.UserId] = latestCreateSequence;
            if (ActiveTab != CommunityTab.Feed)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() => NewReleaseCount++);
            }
        }

        System.Windows.Application.Current.Dispatcher.Invoke(RefreshFeed);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Add to Library
    // ──────────────────────────────────────────────────────────────────────

    private async void AddToLibrary(ReleaseFeedItem? item)
    {
        if (item == null)
            return;

        if (_sync == null)
        {
            SearchStatus = "Add to Library requires an active P2P session.";
            return;
        }

        if (string.IsNullOrWhiteSpace(item.ContentHash))
        {
            SearchStatus = $"Cannot add \"{item.Title}\" yet: no content hash is available from the announcing peer.";
            return;
        }

        SearchStatus = $"Requesting \"{item.Title}\" from the mesh...";

        try
        {
            var bytes = await _sync.RequestContentAsync(item.ArtistUserId, item.ContentHash);
            if (bytes == null || bytes.Length == 0)
            {
                SearchStatus = $"Add to Library failed for \"{item.Title}\": peer did not return content.";
                return;
            }

            _settingsService.EnsureFoldersExist();
            var settings = _settingsService.LoadSettings();
            var otherMusicFolder = _settingsService.GetOtherMusicFolder();
            Directory.CreateDirectory(otherMusicFolder);

            var safeArtist = SanitizeForPath(item.ArtistDisplayName, "Unknown Artist");
            var safeAlbum = SanitizeForPath("Community", "Community");
            var extension = ResolveFileExtension(bytes, item.Title);
            var tempFile = Path.Combine(Path.GetTempPath(), $"MeshWave_{Guid.NewGuid():N}{extension}");
            File.WriteAllBytes(tempFile, bytes);

            var imported = LocalLibraryManager.ImportSingleFileToOrganizedStructure(tempFile, settings.BaseFolder != string.Empty
                ? _settingsService.GetOtherMusicFolder()
                : otherMusicFolder,
                settings.SupportedExtensions);

            if (!imported)
            {
                var fallbackName = SanitizeForPath(item.Title, "Imported Track");
                var fallbackPath = Path.Combine(otherMusicFolder, safeArtist, safeAlbum, $"{fallbackName}{extension}");
                var fallbackFolder = Path.GetDirectoryName(fallbackPath);
                if (!string.IsNullOrWhiteSpace(fallbackFolder))
                    Directory.CreateDirectory(fallbackFolder);
                File.WriteAllBytes(fallbackPath, bytes);
                SearchStatus = $"Added \"{item.Title}\" to Other Music (raw file fallback).";
            }
            else
            {
                SearchStatus = $"Added \"{item.Title}\" to Other Music.";
            }

            try { File.Delete(tempFile); } catch { }
        }
        catch (Exception ex)
        {
            SearchStatus = $"Add to Library failed for \"{item.Title}\": {ex.Message}";
        }
    }

    private static string ResolveFileExtension(byte[] bytes, string title)
    {
        if (bytes.Length >= 12)
        {
            if (bytes[0] == 0x49 && bytes[1] == 0x44 && bytes[2] == 0x33)
                return ".mp3";
            if (bytes[0] == 0x66 && bytes[1] == 0x4C && bytes[2] == 0x61 && bytes[3] == 0x43)
                return ".flac";
            if (bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46)
                return ".wav";
            if (bytes[4] == 0x66 && bytes[5] == 0x74 && bytes[6] == 0x79 && bytes[7] == 0x70)
                return ".m4a";
            if (bytes[0] == 0x4F && bytes[1] == 0x67 && bytes[2] == 0x67 && bytes[3] == 0x53)
                return ".ogg";
        }

        var normalized = title.ToLowerInvariant();
        if (normalized.EndsWith(".mp3")) return ".mp3";
        if (normalized.EndsWith(".flac")) return ".flac";
        if (normalized.EndsWith(".wav")) return ".wav";
        if (normalized.EndsWith(".ogg")) return ".ogg";
        if (normalized.EndsWith(".m4a")) return ".m4a";

        return ".mp3";
    }

    private static string SanitizeForPath(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (!invalid.Contains(c))
                sb.Append(c);
        }

        var result = sb.ToString().Trim();
        return string.IsNullOrWhiteSpace(result) ? fallback : result;
    }
}

public enum CommunityTab { Feed, Discover, Friends, Following, Groups }

public class CommunityUserItem : ViewModelBase
{
    private bool _isFollowing;
    private bool _isFriend;

    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string AvatarIconPath { get; set; } = string.Empty;
    public int TrackCount { get; set; }
    public int FollowerCount { get; set; }

    // Artist profile fields
    public bool IsArtist { get; set; }
    public string Bio { get; set; } = string.Empty;
    public string Website { get; set; } = string.Empty;
    public string BannerImagePath { get; set; } = string.Empty;

    public bool IsFollowing
    {
        get => _isFollowing;
        set => SetProperty(ref _isFollowing, value);
    }

    public bool IsFriend
    {
        get => _isFriend;
        set => SetProperty(ref _isFriend, value);
    }
}

/// <summary>A single item in the release feed — a Create op from a followed/discovered peer.</summary>
public class ReleaseFeedItem
{
    public string ArtistUserId { get; set; } = string.Empty;
    public string ArtistDisplayName { get; set; } = string.Empty;
    public string ArtistAvatarIconPath { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;   // "Track" or "Album"
    public string TargetId { get; set; } = string.Empty;
    public string? ContentHash { get; set; }
    public DateTime ReleasedAt { get; set; }
    public string ReleasedAtDisplay => ReleasedAt.ToLocalTime().ToString("MMM d, yyyy");
}

public class CommunityGroupItem : ViewModelBase
{
    private bool _isMember;

    public string GroupId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int MemberCount { get; set; }

    public bool IsMember
    {
        get => _isMember;
        set => SetProperty(ref _isMember, value);
    }
}
