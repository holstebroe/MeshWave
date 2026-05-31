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
    private readonly Dictionary<string, int> _trackLikes = new(StringComparer.OrdinalIgnoreCase);
    private string _discoverHintText = "Search for users by name or peer id.";

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

            if (ActiveTab == CommunityTab.Discover)
                RefreshDiscoverResults(SearchQuery);
        });
        RefreshFeedCommand = new RelayCommand(_ => RefreshFeed());
        AddToLibraryCommand = new RelayCommand<ReleaseFeedItem>(AddToLibrary, r => r != null && !string.IsNullOrWhiteSpace(r.ContentHash));
        ToggleLikeCommand = new RelayCommand<ReleaseFeedItem>(ToggleLike, item => item != null);

        if (_sync != null)
        {
            _sync.ManifestMerged += OnManifestMerged;
            _sync.PeerCountChanged += OnPeerCountChanged;
            RebuildFollowFriendLists();
            RefreshFeed();
            RefreshDiscoverResults();
        }
        else
        {
            UpdateDiscoverHint(0, false, string.Empty);
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
    public ICommand ToggleLikeCommand { get; }

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

    public string DiscoverHintText
    {
        get => _discoverHintText;
        private set => SetProperty(ref _discoverHintText, value);
    }

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

        RebuildLikesIndex();

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
                    : op.Timestamp,
                LikeCount = _trackLikes.GetValueOrDefault(op.TargetId, 0),
                IsLikedByMe = IsLocallyLiked(op.TargetId)
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
        IsSearching = true;
        SearchStatus = "Searching peers…";
        RefreshDiscoverResults(SearchQuery);
        IsSearching = false;
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
        _sync?.RecordFriendAdd(user.UserId);
    }

    private void RemoveFriend(CommunityUserItem? user)
    {
        if (user == null) return;
        user.IsFriend = false;
        Friends.Remove(user);
        _sync?.RecordFriendRemove(user.UserId);
    }

    private void JoinGroup(CommunityGroupItem? group)
    {
        if (group == null) return;
        group.IsMember = true;
        if (!MyGroups.Contains(group))
            MyGroups.Add(group);
        _sync?.RecordGroupJoin(group.GroupId);
    }

    private void LeaveGroup(CommunityGroupItem? group)
    {
        if (group == null) return;
        group.IsMember = false;
        MyGroups.Remove(group);
        _sync?.RecordGroupLeave(group.GroupId);
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
        if (!followedIds.Contains(e.UserId))
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => RefreshDiscoverResults(SearchQuery));
            return;
        }

        var manifest = _sync.GetPeerManifest(e.UserId);
        if (manifest == null)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => RefreshDiscoverResults(SearchQuery));
            return;
        }

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

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            RefreshFeed();
            RefreshDiscoverResults(SearchQuery);
        });
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
                var report = _sync.LastConnectionAttemptReport;
                if (report != null)
                {
                    SearchStatus = $"Add to Library failed for \"{item.Title}\". {report.BuildUserFacingSummary()}";
                }
                else
                {
                    SearchStatus = $"Add to Library failed for \"{item.Title}\": peer did not return content.";
                }
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

    private void ToggleLike(ReleaseFeedItem? item)
    {
        if (item == null || _sync == null)
            return;

        if (item.IsLikedByMe)
        {
            _sync.RecordUnlike(item.TargetId);
            item.IsLikedByMe = false;
            item.LikeCount = Math.Max(0, item.LikeCount - 1);
            SearchStatus = $"Removed like from \"{item.Title}\".";
        }
        else
        {
            _sync.RecordLike(item.TargetId);
            item.IsLikedByMe = true;
            item.LikeCount++;
            SearchStatus = $"Liked \"{item.Title}\".";
        }

        _trackLikes[item.TargetId] = item.LikeCount;
    }

    /// <summary>
    /// Rebuilds the Following and Friends lists from the persisted local manifest operations.
    /// Called once on startup so the lists survive restarts.
    /// </summary>
    private void RebuildFollowFriendLists()
    {
        var local = _sync?.LocalManifest;
        if (local == null) return;

        // Compute the latest follow/unfollow state per user
        var followStates = local.Operations
            .Where(op => op.TargetType == "User"
                && (op.OperationType == ManifestOperationType.Follow || op.OperationType == ManifestOperationType.Unfollow))
            .GroupBy(op => op.TargetId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(op => op.SequenceNumber).First().OperationType,
                StringComparer.OrdinalIgnoreCase);

        // Compute the latest friend add/remove state per user
        var friendStates = local.Operations
            .Where(op => op.TargetType == "User"
                && (op.OperationType == ManifestOperationType.FriendAdd || op.OperationType == ManifestOperationType.FriendRemove))
            .GroupBy(op => op.TargetId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(op => op.SequenceNumber).First().OperationType,
                StringComparer.OrdinalIgnoreCase);

        var liveUserIds = _sync!.GetPeers()
            .Select(p => p.UserId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Following = new ObservableCollection<CommunityUserItem>(
            followStates
                .Where(kv => kv.Value == ManifestOperationType.Follow)
                .Select(kv => BuildUserItem(kv.Key, liveUserIds)));

        Friends = new ObservableCollection<CommunityUserItem>(
            friendStates
                .Where(kv => kv.Value == ManifestOperationType.FriendAdd)
                .Select(kv => BuildUserItem(kv.Key, liveUserIds)));
    }

    private CommunityUserItem BuildUserItem(string userId, HashSet<string> liveUserIds)
    {
        var manifest = _sync?.PeerManifests.FirstOrDefault(m =>
            string.Equals(m.UserId, userId, StringComparison.OrdinalIgnoreCase));
        var profileOp = manifest?.Operations
            .Where(op => op.OperationType == ManifestOperationType.Profile)
            .OrderByDescending(op => op.SequenceNumber)
            .FirstOrDefault();

        return new CommunityUserItem
        {
            UserId = userId,
            DisplayName = profileOp?.Metadata.GetValueOrDefault("displayName") ?? userId,
            AvatarIconPath = profileOp?.Metadata.GetValueOrDefault("iconPath") ?? string.Empty,
            IsArtist = bool.TryParse(profileOp?.Metadata.GetValueOrDefault("isArtist"), out var ia) && ia,
            Bio = profileOp?.Metadata.GetValueOrDefault("bio") ?? string.Empty,
            Website = profileOp?.Metadata.GetValueOrDefault("website") ?? string.Empty,
            IsFollowing = true,
            IsFriend = Friends.Any(f => string.Equals(f.UserId, userId, StringComparison.OrdinalIgnoreCase)),
            IsOnline = liveUserIds.Contains(userId)
        };
    }

    private void RebuildLikesIndex()
    {
        _trackLikes.Clear();

        foreach (var manifest in _sync?.PeerManifests ?? [])
        {
            var latestByTrack = manifest.Operations
                .Where(op => op.OperationType == ManifestOperationType.Like || op.OperationType == ManifestOperationType.Unlike)
                .GroupBy(op => op.TargetId)
                .Select(g => g.OrderByDescending(op => op.SequenceNumber).First());

            foreach (var op in latestByTrack)
            {
                if (op.OperationType == ManifestOperationType.Like)
                    _trackLikes[op.TargetId] = _trackLikes.GetValueOrDefault(op.TargetId, 0) + 1;
            }
        }
    }

    private bool IsLocallyLiked(string targetId)
    {
        var local = _sync?.LocalManifest;
        if (local == null)
            return false;

        var last = local.Operations
            .Where(op => string.Equals(op.TargetId, targetId, StringComparison.OrdinalIgnoreCase)
                      && (op.OperationType == ManifestOperationType.Like || op.OperationType == ManifestOperationType.Unlike))
            .OrderByDescending(op => op.SequenceNumber)
            .FirstOrDefault();

        return last?.OperationType == ManifestOperationType.Like;
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

    private void OnPeerCountChanged(object? sender, EventArgs e)
    {
        if (System.Windows.Application.Current?.Dispatcher == null)
            return;

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            RefreshDiscoverResults(SearchQuery);
            UpdateOnlineStatus();
        });
    }

    private void UpdateOnlineStatus()
    {
        if (_sync == null) return;
        var liveUserIds = _sync.GetPeers()
            .Select(p => p.UserId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var u in Following)
            u.IsOnline = liveUserIds.Contains(u.UserId);
        foreach (var u in Friends)
            u.IsOnline = liveUserIds.Contains(u.UserId);
    }

    private void RefreshDiscoverResults(string? query = null)
    {
        if (_sync == null || !_sync.IsRunning)
        {
            SearchResults = [];
            GroupResults = [];
            UpdateDiscoverHint(0, false, query ?? string.Empty);
            return;
        }

        var filter = (query ?? string.Empty).Trim();
        var localUserId = _sync.LocalManifest?.UserId ?? string.Empty;

        var peersByUserId = _sync.GetPeers()
            .Where(p => !p.UserId.StartsWith("bootstrap:", StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(p.UserId, localUserId, StringComparison.OrdinalIgnoreCase))
            .GroupBy(p => p.UserId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.LastSeen).First(), StringComparer.OrdinalIgnoreCase);

        var manifests = _sync.PeerManifests
            .GroupBy(m => m.UserId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        foreach (var manifest in manifests)
        {
            if (!peersByUserId.ContainsKey(manifest.UserId))
            {
                peersByUserId[manifest.UserId] = new PeerInfo
                {
                    UserId = manifest.UserId,
                    DisplayName = manifest.UserId,
                    Address = string.Empty,
                    Port = 0,
                    LastSeen = DateTime.UtcNow
                };
            }
        }

        var users = new List<CommunityUserItem>();
        foreach (var peer in peersByUserId.Values.OrderByDescending(p => p.LastSeen).Take(50))
        {
            var manifest = manifests.FirstOrDefault(m => string.Equals(m.UserId, peer.UserId, StringComparison.OrdinalIgnoreCase));
            var profileOp = manifest?.Operations
                .Where(op => op.OperationType == ManifestOperationType.Profile)
                .OrderByDescending(op => op.SequenceNumber)
                .FirstOrDefault();

            var displayName = profileOp?.Metadata.GetValueOrDefault("displayName")
                              ?? (!string.IsNullOrWhiteSpace(peer.DisplayName) ? peer.DisplayName : peer.UserId);
            var isArtist = bool.TryParse(profileOp?.Metadata.GetValueOrDefault("isArtist"), out var parsedIsArtist) && parsedIsArtist;
            var bio = profileOp?.Metadata.GetValueOrDefault("bio") ?? string.Empty;
            var website = profileOp?.Metadata.GetValueOrDefault("website") ?? string.Empty;

            var trackCount = manifest?.Operations.Count(op =>
                op.OperationType == ManifestOperationType.Create
                && string.Equals(op.TargetType, "Track", StringComparison.OrdinalIgnoreCase)) ?? 0;

            // Count followers: scan all peer manifests + local manifest
            var allManifests = _sync.PeerManifests.Concat(
                _sync.LocalManifest != null ? [_sync.LocalManifest] : []);

            var followerCount = allManifests.Count(pm =>
            {
                var lastFollowState = pm.Operations
                    .Where(op => string.Equals(op.TargetType, "User", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(op.TargetId, peer.UserId, StringComparison.OrdinalIgnoreCase)
                        && (op.OperationType == ManifestOperationType.Follow || op.OperationType == ManifestOperationType.Unfollow))
                    .OrderByDescending(op => op.SequenceNumber)
                    .FirstOrDefault();

                return lastFollowState?.OperationType == ManifestOperationType.Follow;
            });

            users.Add(new CommunityUserItem
            {
                UserId = peer.UserId,
                DisplayName = displayName,
                AvatarIconPath = profileOp?.Metadata.GetValueOrDefault("iconPath") ?? string.Empty,
                TrackCount = trackCount,
                FollowerCount = followerCount,
                IsArtist = isArtist,
                Bio = bio,
                Website = website,
                IsFollowing = Following.Any(f => string.Equals(f.UserId, peer.UserId, StringComparison.OrdinalIgnoreCase)),
                IsFriend = Friends.Any(f => string.Equals(f.UserId, peer.UserId, StringComparison.OrdinalIgnoreCase))
            });
        }

        if (!string.IsNullOrWhiteSpace(filter))
        {
            users = users
                .Where(u =>
                    u.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || u.UserId.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || u.Bio.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        SearchResults = new ObservableCollection<CommunityUserItem>(users);
        GroupResults = [];

        SearchStatus = string.IsNullOrWhiteSpace(filter)
            ? $"Showing {users.Count} recent peer{(users.Count == 1 ? string.Empty : "s")}."
            : users.Count == 0
                ? $"No users matched \"{filter}\"."
                : $"Found {users.Count} user result{(users.Count == 1 ? string.Empty : "s")} for \"{filter}\".";

        UpdateDiscoverHint(users.Count, true, filter);
    }

    private void UpdateDiscoverHint(int resultCount, bool isConnected, string query)
    {
        if (!isConnected)
        {
            DiscoverHintText = "Connect to the mesh first to enable live peer discovery.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            DiscoverHintText = resultCount == 0
                ? $"No users matched \"{query}\"."
                : $"Showing {resultCount} result{(resultCount == 1 ? string.Empty : "s")} for \"{query}\".";
            return;
        }

        DiscoverHintText = resultCount == 0
            ? "Connected, but no peer profiles have been discovered yet."
            : "Showing the most recently connected users from your mesh.";
    }
}

public enum CommunityTab { Feed, Discover, Friends, Following, Groups }

public class CommunityUserItem : ViewModelBase
{
    private bool _isFollowing;
    private bool _isFriend;
    private bool _isOnline;

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

    /// <summary>True when this peer is currently reachable in the routing table.</summary>
    public bool IsOnline
    {
        get => _isOnline;
        set
        {
            SetProperty(ref _isOnline, value);
            OnPropertyChanged(nameof(OnlineStatusText));
            OnPropertyChanged(nameof(OnlineStatusColor));
        }
    }

    public string OnlineStatusText => _isOnline ? "Online" : "Offline";
    public string OnlineStatusColor => _isOnline ? "#4CAF50" : "#888888";
}

/// <summary>A single item in the release feed — a Create op from a followed/discovered peer.</summary>
public class ReleaseFeedItem : ViewModelBase
{
    private int _likeCount;
    private bool _isLikedByMe;

    public string ArtistUserId { get; set; } = string.Empty;
    public string ArtistDisplayName { get; set; } = string.Empty;
    public string ArtistAvatarIconPath { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;   // "Track" or "Album"
    public string TargetId { get; set; } = string.Empty;
    public string? ContentHash { get; set; }
    public DateTime ReleasedAt { get; set; }
    public string ReleasedAtDisplay => ReleasedAt.ToLocalTime().ToString("MMM d, yyyy");

    public int LikeCount
    {
        get => _likeCount;
        set => SetProperty(ref _likeCount, value);
    }

    public bool IsLikedByMe
    {
        get => _isLikedByMe;
        set => SetProperty(ref _isLikedByMe, value);
    }
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
