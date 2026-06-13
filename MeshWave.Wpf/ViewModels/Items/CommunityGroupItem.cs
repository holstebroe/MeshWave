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
