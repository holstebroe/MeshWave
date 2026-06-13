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

public enum CommunityTab { Feed, Discover, Friends, Following, Groups }
