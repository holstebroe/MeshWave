using System.Windows;
using MeshWave.Wpf.ViewModels;

namespace MeshWave.Wpf.Views;

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