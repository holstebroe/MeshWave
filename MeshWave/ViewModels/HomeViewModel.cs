using MeshWave.Mvvm;

namespace MeshWave.ViewModels;

/// <summary>
/// View model for the home/dashboard page.
/// </summary>
public class HomeViewModel : ViewModelBase
{
    private string _statusMessage = "Welcome to MeshWave";

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }
}
