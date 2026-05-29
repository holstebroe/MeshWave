using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

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
    }
}