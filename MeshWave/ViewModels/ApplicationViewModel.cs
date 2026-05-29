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
    private readonly PlaybackViewModel _playbackViewModel;

    public ApplicationViewModel()
    {
        _playbackViewModel = new PlaybackViewModel();
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
        CurrentViewModel = new LibraryViewModel(this, isMyMusicLibrary: false);
    }

    public void NavigateToMyMusic()
    {
        CurrentViewModel = new LibraryViewModel(this, isMyMusicLibrary: true);
    }

    public void NavigateToSettings()
    {
        CurrentViewModel = new SettingsViewModel();
    }

    public void NavigateToPlayback()
    {
        CurrentViewModel = _playbackViewModel;
    }

    public void PlayTrack(string trackTitle, string artist, TimeSpan duration, string filePath)
    {
        _playbackViewModel.Stop();
        _playbackViewModel.LoadTrack(trackTitle, artist, duration, filePath);
        CurrentViewModel = _playbackViewModel;
    }
}
