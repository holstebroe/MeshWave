using System.Windows;
using System.Windows.Input;

namespace MeshWave
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new ViewModels.ApplicationViewModel();
        }

        private ViewModels.ApplicationViewModel ViewModel => (ViewModels.ApplicationViewModel)DataContext;

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
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
                        System.Windows.Forms.ToolTipIcon.Info);
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
            ViewModel.NavigateToHome();
        }

        private void LibraryMenu_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.NavigateToLibrary();
        }

        private void MyMusicMenu_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.NavigateToMyMusic();
        }

        private void PlaybackMenu_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.NavigateToPlayback();
        }

        private void SettingsMenu_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.NavigateToSettings();
        }

        private void BrowseMenu_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.NavigateToBrowse();
        }

        private void CommunityMenu_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.NavigateToCommunity();
        }
    }
}
