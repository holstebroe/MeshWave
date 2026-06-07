using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using MeshWave.Wpf.Services;
using MeshWave.Wpf.ViewModels;

namespace MeshWave.Wpf;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var settingsService = new SettingsService();
        DataContext = new ApplicationViewModel(settingsService: settingsService);
    }

    private ApplicationViewModel ViewModel => (ApplicationViewModel)DataContext;

    protected override void OnClosing(CancelEventArgs e)
    {
        // Persist playback state before close-to-tray or exit.
        ViewModel.PersistPlaybackState(force: true);

        // Intercept close: minimise to tray unless the app is explicitly quitting.
        if (Application.Current is App app && !app._IsExiting)
        {
            e.Cancel = true;
            Hide();

            if (!app._TrayNotificationShown)
            {
                app._TrayNotificationShown = true;
                app.ShowTrayNotification(
                    "MeshWave is still running",
                    "The mesh network stays active in the background.\nDouble-click the tray icon to reopen.",
                    ToolTipIcon.Info);
            }
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var playback = ViewModel.Playback;
        switch (e.Key)
        {
            case Key.MediaPlayPause:
                playback.PlayPauseToggleCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.MediaStop:
                playback.StopCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.MediaNextTrack:
                playback.NextTrackCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.MediaPreviousTrack:
                playback.PreviousTrackCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void HomeMenu_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.PersistPlaybackState();
        ViewModel.NavigateToHome();
    }

    private void LibraryMenu_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.PersistPlaybackState();
        ViewModel.NavigateToLibrary();
    }

    private void MyMusicMenu_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.PersistPlaybackState();
        ViewModel.NavigateToMyMusic();
    }

    private void PlaybackMenu_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.PersistPlaybackState();
        ViewModel.NavigateToPlayback();
    }

    private void SettingsMenu_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.PersistPlaybackState();
        ViewModel.NavigateToSettings();
    }

    private void BrowseMenu_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.PersistPlaybackState();
        ViewModel.NavigateToBrowse();
    }

    private void CommunityMenu_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.PersistPlaybackState();
        ViewModel.NavigateToCommunity();
    }

    private void BrandMenu_MinimizeToTray_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.PersistPlaybackState(force: true);
        Hide();
    }

    private void BrandMenu_Quit_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.PersistPlaybackState(force: true);
        if (Application.Current is App app)
            app.ExitApplication();
        else
            Application.Current.Shutdown();
    }
}