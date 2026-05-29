using System.Windows.Controls;

namespace MeshWave.Views
{
    public partial class LibraryView : UserControl
    {
        public LibraryView()
        {
            InitializeComponent();
        }

        private async void ImportMyMusic_Click(object sender, System.Windows.RoutedEventArgs e)
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
                var sourceFolder = System.IO.Path.GetDirectoryName(dialog.FileName);
                if (DataContext is MeshWave.ViewModels.LibraryViewModel vm && sourceFolder != null)
                {
                    await vm.ImportMyMusicAsync(sourceFolder);
                }
            }
        }

        private void OnTrackDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is ListBox listBox && listBox.SelectedItem is MeshWave.ViewModels.LibraryTrackItem trackItem)
            {
                if (DataContext is MeshWave.ViewModels.LibraryViewModel vm)
                {
                    vm.PlayTrackById(trackItem.TrackId);
                }
            }
        }

        private void OnAlbumDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is ListBox listBox && listBox.SelectedItem is MeshWave.ViewModels.LibraryAlbumItem albumItem)
            {
                if (DataContext is MeshWave.ViewModels.LibraryViewModel vm)
                {
                    var album = vm.GetAlbumById(albumItem.AlbumId);
                    if (album != null)
                    {
                        // TODO: optionally play first track in selected album
                    }
                }
            }
        }
    }
}
