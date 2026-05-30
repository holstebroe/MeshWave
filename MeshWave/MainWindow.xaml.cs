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
