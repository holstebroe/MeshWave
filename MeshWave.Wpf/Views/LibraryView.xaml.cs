using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using MeshWave.Wpf.ViewModels;
using Binding = System.Windows.Data.Binding;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace MeshWave.Wpf.Views;

public partial class LibraryView : UserControl
{
    public LibraryView()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            if (DataContext is LibraryViewModel vm)
            {
                vm.OpenMetadataEditorRequested -= Vm_OpenMetadataEditorRequested;
                vm.OpenMetadataEditorRequested += Vm_OpenMetadataEditorRequested;
            }
        };
    }

    private void Vm_OpenMetadataEditorRequested(object? sender, string trackFilePath)
    {
        var editorVm = new MyMusicMetadataEditorViewModel();
        editorVm.LoadTrack(trackFilePath);

        var view = new MyMusicMetadataEditorView
        {
            DataContext = editorVm,
            Margin = new Thickness(8)
        };

        var window = new Window
        {
            Title = "Edit Local Music Metadata",
            Width = 700,
            Height = 620,
            Content = view,
            Owner = Application.Current.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        editorVm.RequestClose += (_, _) => window.Close();

        window.ShowDialog();
        if (DataContext is LibraryViewModel libraryVm) libraryVm.LoadFromConfiguredBaseFolder();

        // Also refresh ApplicationViewModel if available to update player bar
        if (Application.Current.MainWindow.DataContext is ApplicationViewModel appVm) appVm.Playback.RefreshCurrentTrackMetadata();
    }

    private async void ImportMyMusic_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            CheckFileExists = false,
            CheckPathExists = true,
            FileName = "Select Folder...",
            Filter = "Folders|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            var sourceFolder = Path.GetDirectoryName(dialog.FileName);
            if (DataContext is LibraryViewModel vm && sourceFolder != null)
            {
                var progressWindow = new Window
                {
                    Title = "Import Progress",
                    Width = 520,
                    Height = 240,
                    Owner = Application.Current.MainWindow,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    ResizeMode = ResizeMode.NoResize,
                    Background = (Brush)Application.Current.Resources["BackgroundColor"]
                };

                var progressRoot = new Border
                {
                    Margin = new Thickness(14),
                    Padding = new Thickness(14),
                    CornerRadius = new CornerRadius(8),
                    Background = (Brush)Application.Current.Resources["SurfaceColor"],
                    DataContext = vm
                };

                var stack = new StackPanel();
                stack.Children.Add(new TextBlock { Text = "Import status", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) });

                var statusText = new TextBlock();
                statusText.SetBinding(TextBlock.TextProperty, new Binding("ImportStatusMessage"));
                stack.Children.Add(statusText);

                var currentFileText = new TextBlock { Margin = new Thickness(0, 4, 0, 4), TextTrimming = TextTrimming.CharacterEllipsis };
                currentFileText.SetBinding(TextBlock.TextProperty, new Binding("ImportCurrentFile"));
                stack.Children.Add(currentFileText);

                var counts = new TextBlock();
                counts.SetBinding(TextBlock.TextProperty, new Binding("ImportImportedFiles") { StringFormat = "Imported: {0}" });
                stack.Children.Add(counts);

                var remaining = new TextBlock();
                remaining.SetBinding(TextBlock.TextProperty, new Binding("ImportRemainingFiles") { StringFormat = "Remaining: {0}" });
                stack.Children.Add(remaining);

                var progress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 12, Margin = new Thickness(0, 8, 0, 10) };
                progress.SetBinding(ProgressBar.ValueProperty, new Binding("ImportProgressPercent") { Mode = BindingMode.OneWay });
                stack.Children.Add(progress);

                var cancelButton = new Button
                {
                    Content = "Cancel Import",
                    Width = 140,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Foreground = Brushes.White,
                    Background = (Brush)Application.Current.Resources["PrimaryColor"]
                };
                cancelButton.SetBinding(Button.CommandProperty, new Binding("CancelImportCommand"));
                cancelButton.SetBinding(IsEnabledProperty, new Binding("CanCancelImport"));
                stack.Children.Add(cancelButton);

                progressRoot.Child = stack;
                progressWindow.Content = progressRoot;
                progressWindow.Show();

                try
                {
                    await vm.ImportMyMusicAsync(sourceFolder);
                }
                finally
                {
                    progressWindow.Close();
                }
            }
        }
    }

    private void OnTrackDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox listBox || listBox.SelectedItem is not LibraryTrackItem trackItem) return;

        if (DataContext is not LibraryViewModel vm) return;

        // Local Music double-click opens metadata editor; playback is single-click via ▶ button.
        if (vm.CanImportMyMusic)
        {
            vm.RequestOpenMetadataEditor(trackItem.FilePath);
            return;
        }

        vm.PlayTrackById(trackItem.TrackId);
    }

    private void OnAlbumDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox listBox || listBox.SelectedItem is not LibraryAlbumItem albumItem) return;

        if (DataContext is not LibraryViewModel vm || !vm.CanImportMyMusic) return;

        var trackInAlbum = vm.Tracks.FirstOrDefault();
        if (trackInAlbum == null || string.IsNullOrWhiteSpace(trackInAlbum.FilePath)) return;

        var albumFolder = Path.GetDirectoryName(trackInAlbum.FilePath) ?? string.Empty;
        var editorVm = new MyMusicMetadataEditorViewModel();
        editorVm.LoadAlbum(albumFolder);

        var view = new MyMusicMetadataEditorView
        {
            DataContext = editorVm,
            Margin = new Thickness(8)
        };

        var window = new Window
        {
            Title = $"Edit Album Metadata - {albumItem.Name}",
            Width = 700,
            Height = 680,
            Content = view,
            Owner = Application.Current.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        editorVm.RequestClose += (_, _) => window.Close();

        window.ShowDialog();

        vm.LoadFromConfiguredBaseFolder();
    }

    private void EditMetadata_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not LibraryViewModel vm || !vm.CanImportMyMusic) return;

        if (sender is MenuItem menuItem && menuItem.Parent is ContextMenu contextMenu && contextMenu.PlacementTarget is ListBox listBox)
            if (listBox.SelectedItem is LibraryTrackItem trackItem)
                vm.RequestOpenMetadataEditor(trackItem.FilePath);
    }

    private void AlbumPlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element &&
            element.DataContext is LibraryAlbumItem album &&
            DataContext is LibraryViewModel vm)
        {
            vm.SelectedAlbum = album;
            var firstTrack = vm.Tracks.FirstOrDefault();
            if (firstTrack != null)
                vm.PlayTrackById(firstTrack.TrackId);
        }
    }

    private void TrackPlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element &&
            element.DataContext is LibraryTrackItem trackItem &&
            DataContext is LibraryViewModel vm)
            vm.PlayTrackById(trackItem.TrackId);
    }

    private void ImportMyMusicFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Audio Files|*.mp3;*.flac;*.wav;*.ogg;*.m4a|All Files|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() == true && DataContext is LibraryViewModel vm) vm.ImportMyMusicFile(dialog.FileName);
    }
}