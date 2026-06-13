using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Input;
using MeshWave.Common.Core.Models;
using MeshWave.Synchronizer;
using MeshWave.Wpf.Mvvm;
using MeshWave.Wpf.Services;
using MeshWave.Wpf.Models;

namespace MeshWave.Wpf.ViewModels.Items;

public class BrowseArtistItem : ViewModelBase
{
    private string _avatarIconPath = string.Empty;
    private string _iconPath = string.Empty;
    private bool _isLocal;

    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string AvatarIconPath
    {
        get => _avatarIconPath;
        set => SetProperty(ref _avatarIconPath, value);
    }
    public string IconPath
    {
        get => _iconPath;
        set => SetProperty(ref _iconPath, value);
    }
    public string Bio { get; set; } = string.Empty;
    public int TrackCount { get; set; }
    public int AlbumCount { get; set; }
    public long TotalSize { get; set; }
    public string TotalSizeDisplay { get; set; } = string.Empty;
    public bool IsLocal
    {
        get => _isLocal;
        set => SetProperty(ref _isLocal, value);
    }
}
