using MeshWave.Mvvm;

namespace MeshWave.ViewModels;

/// <summary>
/// View model for application settings and configuration.
/// </summary>
public class SettingsViewModel : ViewModelBase
{
    private string _storageFolder = string.Empty;
    private string _username = string.Empty;
    private bool _isInitialized = false;

    public string StorageFolder
    {
        get => _storageFolder;
        set => SetProperty(ref _storageFolder, value);
    }

    public string Username
    {
        get => _username;
        set => SetProperty(ref _username, value);
    }

    public bool IsInitialized
    {
        get => _isInitialized;
        set => SetProperty(ref _isInitialized, value);
    }

    public void BrowseStorageFolder()
    {
        // TODO: Implement folder browser
    }

    public void GenerateKeypair()
    {
        // TODO: Implement keypair generation
    }

    public void SaveSettings()
    {
        // TODO: Implement settings persistence
    }
}
