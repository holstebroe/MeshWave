using MeshWave.Mvvm;

namespace MeshWave.ViewModels;

/// <summary>
/// View model for browsing community music available on the MeshWave network.
/// </summary>
public class BrowseViewModel : ViewModelBase
{
    public BrowseViewModel()
    {
    }

    private string _statusText = "Connect to the Mesh network to discover community music.";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }
}
