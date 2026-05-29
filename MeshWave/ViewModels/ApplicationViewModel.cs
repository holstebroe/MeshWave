using MeshWave.Mvvm;

namespace MeshWave.ViewModels;

/// <summary>
/// Main application view model.
/// Manages overall application state and navigation.
/// </summary>
public class ApplicationViewModel : ViewModelBase
{
    private string _applicationTitle = "MeshWave";
    private ViewModelBase _currentViewModel;

    public ApplicationViewModel()
    {
        // Initialize with home view model
        _currentViewModel = new HomeViewModel();
    }

    public string ApplicationTitle
    {
        get => _applicationTitle;
        set => SetProperty(ref _applicationTitle, value);
    }

    public ViewModelBase CurrentViewModel
    {
        get => _currentViewModel;
        set => SetProperty(ref _currentViewModel, value);
    }

    public void NavigateToHome()
    {
        CurrentViewModel = new HomeViewModel();
    }

    public void NavigateToLibrary()
    {
        CurrentViewModel = new LibraryViewModel();
    }

    public void NavigateToSettings()
    {
        CurrentViewModel = new SettingsViewModel();
    }
}
