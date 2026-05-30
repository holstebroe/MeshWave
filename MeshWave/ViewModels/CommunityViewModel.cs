using MeshWave.Mvvm;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace MeshWave.ViewModels;

/// <summary>
/// View model for the Community view — user/group discovery, follows, friends and group membership.
/// Actual P2P search will be wired in Milestone D; for now the data model and UI scaffolding is in place.
/// </summary>
public class CommunityViewModel : ViewModelBase
{
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

    public CommunityViewModel()
    {
        SearchCommand = new RelayCommand(_ => Search(), _ => !IsSearching && !string.IsNullOrWhiteSpace(SearchQuery));
        FollowUserCommand = new RelayCommand<CommunityUserItem>(FollowUser, u => u != null && !u.IsFollowing);
        UnfollowUserCommand = new RelayCommand<CommunityUserItem>(UnfollowUser, u => u != null && u.IsFollowing);
        AddFriendCommand = new RelayCommand<CommunityUserItem>(AddFriend, u => u != null && !u.IsFriend);
        RemoveFriendCommand = new RelayCommand<CommunityUserItem>(RemoveFriend, u => u != null && u.IsFriend);
        JoinGroupCommand = new RelayCommand<CommunityGroupItem>(JoinGroup, g => g != null && !g.IsMember);
        LeaveGroupCommand = new RelayCommand<CommunityGroupItem>(LeaveGroup, g => g != null && g.IsMember);
        SetTabCommand = new RelayCommand<string>(tab => ActiveTab = Enum.Parse<CommunityTab>(tab));
        RefreshFeedCommand = new RelayCommand(_ => RefreshFeed());
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
        // TODO (Milestone F): query PeerManifestStore for Create ops from followed peers,
        // map them to ReleaseFeedItem ordered by ReleasedAt descending.
        // For now populate a placeholder so the UI is exercisable.
        ReleaseFeed =
        [
            new ReleaseFeedItem
            {
                ArtistDisplayName = "Peer Artist",
                ArtistAvatarIconPath = string.Empty,
                Title = "Release feed will populate when connected to the mesh.",
                TargetType = "Track",
                ReleasedAt = DateTime.UtcNow
            }
        ];
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
        // TODO (Milestone D): append signed "follow" manifest op
    }

    private void UnfollowUser(CommunityUserItem? user)
    {
        if (user == null) return;
        user.IsFollowing = false;
        Following.Remove(user);
        // TODO (Milestone D): append signed "unfollow" manifest op
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
