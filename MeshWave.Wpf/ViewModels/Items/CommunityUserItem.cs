using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows.Input;
using MeshWave.Common.Core.Models;
using MeshWave.Common.Core.P2P;
using MeshWave.LibraryManager;
using MeshWave.Synchronizer;
using MeshWave.Wpf.Mvvm;
using MeshWave.Wpf.Services;

namespace MeshWave.Wpf.ViewModels.Items;

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
