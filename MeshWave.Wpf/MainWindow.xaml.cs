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
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ApplicationViewModel oldVm)
        {
            oldVm.Playback.PropertyChanged -= Playback_PropertyChanged;
        }

        if (e.NewValue is ApplicationViewModel newVm)
        {
            newVm.Playback.PropertyChanged += Playback_PropertyChanged;
        }
    }

    private void Playback_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlaybackViewModel.DynamicAccentColor) && sender is PlaybackViewModel vm)
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (Application.Current == null) return;

                if (Application.Current.Resources["AccentColor"] is System.Windows.Media.SolidColorBrush accentBrush &&
                    Application.Current.Resources["PrimaryColor"] is System.Windows.Media.SolidColorBrush primaryBrush)
                {
                    if (accentBrush.IsFrozen || primaryBrush.IsFrozen)
                    {
                        var newAccentBrush = new System.Windows.Media.SolidColorBrush(vm.DynamicAccentColor);
                        Application.Current.Resources["AccentColor"] = newAccentBrush;

                        var newPrimaryBrush = new System.Windows.Media.SolidColorBrush(vm.DynamicAccentColor);
                        Application.Current.Resources["PrimaryColor"] = newPrimaryBrush;
                    }
                    else
                    {
                        var animation = new System.Windows.Media.Animation.ColorAnimation
                        {
                            To = vm.DynamicAccentColor,
                            Duration = TimeSpan.FromSeconds(0.5)
                        };
                        accentBrush.BeginAnimation(System.Windows.Media.SolidColorBrush.ColorProperty, animation);
                        primaryBrush.BeginAnimation(System.Windows.Media.SolidColorBrush.ColorProperty, animation);
                    }
                }
                else
                {
                    var newBrush = new System.Windows.Media.SolidColorBrush(vm.DynamicAccentColor);
                    if (Application.Current.Resources.Contains("AccentColor"))
                        Application.Current.Resources["AccentColor"] = newBrush;
                    if (Application.Current.Resources.Contains("PrimaryColor"))
                        Application.Current.Resources["PrimaryColor"] = newBrush;
                }
            });
        }
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

    private void DownloadsMenu_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.PersistPlaybackState();
        ViewModel.NavigateToDownloads();
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