using System.Windows;
using System.Windows.Controls;
using MeshWave.ViewModels;

namespace MeshWave.Views
{
    public partial class HomeView : UserControl
    {
        public HomeView()
        {
            InitializeComponent();
        }

        private ApplicationViewModel? GetAppViewModel()
        {
            return Application.Current.MainWindow?.DataContext as ApplicationViewModel;
        }

        private void MyMusicPanel_Click(object sender, RoutedEventArgs e)
        {
            GetAppViewModel()?.NavigateToMyMusic();
        }

        private void LibraryPanel_Click(object sender, RoutedEventArgs e)
        {
            GetAppViewModel()?.NavigateToLibrary();
        }

        private void PlaybackPanel_Click(object sender, RoutedEventArgs e)
        {
            GetAppViewModel()?.NavigateToPlayback();
        }
    }
}
