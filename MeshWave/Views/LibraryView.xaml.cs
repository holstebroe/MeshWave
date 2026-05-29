using System.Windows.Controls;

namespace MeshWave.Views
{
    public partial class LibraryView : UserControl
    {
        public LibraryView()
        {
            InitializeComponent();
        }

        private void SelectFolder_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "Select Folder...",
                Filter = "Folders|*.*"
            };
            if (dialog.ShowDialog() == true)
            {
                var folder = System.IO.Path.GetDirectoryName(dialog.FileName);
                if (DataContext is MeshWave.ViewModels.LibraryViewModel vm && folder != null)
                {
                    vm.LoadLibrary(folder);
                }
            }
        }

        private void OnTrackDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is ListBox listBox && listBox.SelectedItem is string trackTitle)
            {
                if (DataContext is MeshWave.ViewModels.LibraryViewModel vm)
                {
                    var track = vm.GetTrackByTitle(trackTitle);
                    if (track != null)
                    {
                        var mainWindow = System.Windows.Application.Current.MainWindow as MainWindow;
                        if (mainWindow != null)
                        {
                            var appVm = mainWindow.DataContext as MeshWave.ViewModels.ApplicationViewModel;
                            var playbackVm = new MeshWave.ViewModels.PlaybackViewModel();
                            playbackVm.LoadTrack(track.Title, track.Description ?? "Unknown Artist", track.Duration, track.FileHash);
                            if (appVm != null)
                            {
                                appVm.CurrentViewModel = playbackVm;
                            }
                        }
                    }
                }
            }
        }

        private void OnAlbumDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is ListBox listBox && listBox.SelectedItem is string albumTitle)
            {
                if (DataContext is MeshWave.ViewModels.LibraryViewModel vm)
                {
                    var album = vm.GetAlbumByTitle(albumTitle);
                    if (album != null)
                    {
                        // TODO: Play first track of album
                        // For now, just navigate to playback with album info
                        var mainWindow = System.Windows.Application.Current.MainWindow as MainWindow;
                        if (mainWindow != null)
                        {
                            var appVm = mainWindow.DataContext as MeshWave.ViewModels.ApplicationViewModel;
                            var playbackVm = new MeshWave.ViewModels.PlaybackViewModel();
                            playbackVm.LoadTrack(album.Title, "Album", System.TimeSpan.Zero);
                            if (appVm != null)
                            {
                                appVm.CurrentViewModel = playbackVm;
                            }
                        }
                    }
                }
            }
        }
    }
}
