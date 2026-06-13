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
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
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
