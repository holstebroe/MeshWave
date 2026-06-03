using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MeshWave.Common.Core.Models;
using MeshWave.Common.Core.Storage;
using MeshWave.Services;
using MeshWave.ViewModels;

namespace MeshWave.Views
{
    public partial class PlaybackView : UserControl
    {
        private PlaybackViewModel? _boundViewModel;
        // Timestamp captured at first keystroke in the comment box
        private double? _commentStartTimestamp;

        public PlaybackView()
        {
            InitializeComponent();
            Loaded += PlaybackView_Loaded;
            DataContextChanged += PlaybackView_DataContextChanged;
            SizeChanged += PlaybackView_SizeChanged;
        }

        private void PlaybackView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DrawWaveform();
        }

        private void PlaybackView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_boundViewModel != null)
            {
                _boundViewModel.PropertyChanged -= ViewModelOnPropertyChanged;
            }

            _boundViewModel = DataContext as PlaybackViewModel;
            if (_boundViewModel != null)
            {
                _boundViewModel.PropertyChanged += ViewModelOnPropertyChanged;
            }

            DrawWaveform();
        }

        private void PlaybackView_Loaded(object sender, RoutedEventArgs e)
        {
            DrawWaveform();
        }

        private void ViewModelOnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (_boundViewModel == null)
            {
                return;
            }

            if (args.PropertyName == nameof(_boundViewModel.CurrentPosition))
            {
                UpdatePlaybackCursor();
                if (SeekPreviewOverlay.Visibility == Visibility.Visible)
                {
                    var mouseX = Mouse.GetPosition(WaveformCanvas).X;
                    UpdateSeekPreview(Canvas.GetLeft(PlaybackCursor), mouseX);
                }
            }
            else if (args.PropertyName == nameof(_boundViewModel.WaveformSamples) ||
                     args.PropertyName == nameof(_boundViewModel.TimelineMarkers) ||
                     args.PropertyName == nameof(_boundViewModel.WaveformStyle))
            {
                DrawWaveform();
            }
        }

        private void DrawWaveform()
        {
            var width  = WaveformCanvas.ActualWidth > 0 ? WaveformCanvas.ActualWidth : 800;
            var height = WaveformCanvas.Height;

            var samples = Array.Empty<float>();
            var style   = WaveformStyle.Filled;

            if (DataContext is PlaybackViewModel vm && vm.WaveformSamples.Length > 0)
            {
                samples = vm.WaveformSamples;
                style   = vm.WaveformStyle;
            }

            WaveformPath.Data = WaveformRenderer.Render(samples, width, height, style);
            UpdateWaveformBrush(style);

            // Timeline markers on top of bars (but below overlay/cursor)
            DrawTimelineMarkers(width, height);

            UpdatePlaybackCursor();
        }

        private void UpdateWaveformBrush(WaveformStyle style)
        {
            switch (style)
            {
                case WaveformStyle.Filled:
                    WaveformPath.Fill = (Brush)FindResource("PrimaryColor");
                    WaveformPath.Opacity = 1.0;
                    break;
                case WaveformStyle.Cloudy:
                    var cloudyBrush = new LinearGradientBrush
                    {
                        StartPoint = new Point(0, 0),
                        EndPoint = new Point(0, 1)
                    };
                    cloudyBrush.GradientStops.Add(new GradientStop(Color.FromRgb(64, 128, 192), 0.45));
                    cloudyBrush.GradientStops.Add(new GradientStop(Color.FromArgb(102, 72, 160, 176), 0.55));
                    WaveformPath.Fill = cloudyBrush;
                    WaveformPath.Opacity = 1.0;
                    break;
                case WaveformStyle.Mirror:
                    var mirrorBrush = new LinearGradientBrush
                    {
                        StartPoint = new Point(0, 0),
                        EndPoint = new Point(0, 1)
                    };
                    mirrorBrush.GradientStops.Add(new GradientStop(Color.FromRgb(64, 128, 192), 0.0));
                    mirrorBrush.GradientStops.Add(new GradientStop(Color.FromRgb(64, 128, 192), 0.5));
                    mirrorBrush.GradientStops.Add(new GradientStop(Color.FromArgb(120, 64, 128, 192), 0.5));
                    mirrorBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 64, 128, 192), 1.0));
                    WaveformPath.Fill = mirrorBrush;
                    WaveformPath.Opacity = 1.0;
                    break;
                case WaveformStyle.Neon:
                    WaveformPath.Fill = new SolidColorBrush(Color.FromRgb(0, 255, 255));
                    WaveformPath.Opacity = 0.8;
                    break;
                case WaveformStyle.Smooth:
                    var smoothBrush = new LinearGradientBrush
                    {
                        StartPoint = new Point(0, 0),
                        EndPoint = new Point(0, 1)
                    };
                    smoothBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 100, 200, 255), 0.0));
                    smoothBrush.GradientStops.Add(new GradientStop(Color.FromRgb(80, 180, 255), 0.45));
                    smoothBrush.GradientStops.Add(new GradientStop(Color.FromRgb(80, 180, 255), 0.55));
                    smoothBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 100, 200, 255), 1.0));
                    WaveformPath.Fill = smoothBrush;
                    WaveformPath.Opacity = 1.0;
                    break;
            }
        }

        private void DrawTimelineMarkers(double width, double height)
        {
            // Clear existing markers (ellipses) before redrawing.
            // We keep the first 4 fixed children: WaveformPath, PlayedOverlay, SeekPreviewOverlay, PlaybackCursor.
            while (WaveformCanvas.Children.Count > 4)
            {
                WaveformCanvas.Children.RemoveAt(4);
            }

            if (DataContext is not PlaybackViewModel vm || vm.Duration.TotalSeconds <= 0)
            {
                return;
            }

            var appVm = Application.Current.MainWindow.DataContext as ApplicationViewModel;
            var userRepo = appVm?.SyncOrchestrator?.UserRepository;
            var userIconConverter = new Converters.UserIconConverter();

            foreach (var marker in vm.TimelineMarkers)
            {
                var progress = Math.Clamp(marker.TimestampSeconds / vm.Duration.TotalSeconds, 0.0, 1.0);
                var x = progress * width;

                var markerIcon = new Ellipse
                {
                    Width = 14,
                    Height = 14,
                    Stroke = Brushes.White,
                    StrokeThickness = 1,
                    Cursor = Cursors.Hand,
                    Tag = marker.TimestampSeconds
                };

                var iconPath = userRepo?.GetUserIconPath(marker.UserId);
                var iconSource = userIconConverter.Convert(iconPath, typeof(System.Windows.Media.ImageSource), null, System.Globalization.CultureInfo.CurrentCulture) as System.Windows.Media.ImageSource;

                if (iconSource != null)
                {
                    markerIcon.Fill = new ImageBrush(iconSource)
                    {
                        Stretch = Stretch.UniformToFill
                    };
                }
                else
                {
                    markerIcon.Fill = Brushes.Orange;
                }

                markerIcon.MouseLeftButtonDown += TimelineMarker_MouseLeftButtonDown;

                var displayName = userRepo?.GetDisplayName(marker.UserId) ?? marker.UserId;

                var tooltipBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(32, 32, 32)),
                    BorderBrush = Brushes.White,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8)
                };
                tooltipBorder.Child = new TextBlock
                {
                    Text = $"[{TimeSpan.FromSeconds(marker.TimestampSeconds):mm\\:ss}] (v{(marker.TrackVersion <= 0 ? 1 : marker.TrackVersion)}) {displayName}: {marker.Comment}",
                    Foreground = Brushes.White,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 280
                };
                markerIcon.ToolTip = tooltipBorder;

                Canvas.SetLeft(markerIcon, x - 7);
                Canvas.SetTop(markerIcon, 3);
                WaveformCanvas.Children.Add(markerIcon);
            }
        }

        private void TimelineMarker_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element &&
                element.Tag is double timestampSeconds &&
                DataContext is PlaybackViewModel vm)
            {
                vm.Seek(TimeSpan.FromSeconds(timestampSeconds));
                e.Handled = true;
            }
        }

        private void UpdatePlaybackCursor()
        {
            if (DataContext is PlaybackViewModel vm && vm.Duration.TotalSeconds > 0)
            {
                var progress = vm.CurrentPosition.TotalSeconds / vm.Duration.TotalSeconds;
                var width = WaveformCanvas.ActualWidth > 0 ? WaveformCanvas.ActualWidth : 800;
                var cursorX = progress * width;
                Canvas.SetLeft(PlaybackCursor, cursorX);

                // Update the played-region semi-transparent overlay
                PlayedOverlay.Width = Math.Max(0, cursorX);

                // Keep the seek-preview anchored to the current cursor if it is visible
                if (SeekPreviewOverlay.Visibility == Visibility.Visible)
                    UpdateSeekPreview(cursorX, Canvas.GetLeft(SeekPreviewOverlay) + SeekPreviewOverlay.Width);
            }
        }

        private void UpdateSeekPreview(double cursorX, double mouseX)
        {
            var left  = Math.Min(cursorX, mouseX);
            var right = Math.Max(cursorX, mouseX);
            Canvas.SetLeft(SeekPreviewOverlay, left);
            SeekPreviewOverlay.Width = right - left;
        }

        private void WaveformCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (DataContext is not PlaybackViewModel vm || vm.Duration.TotalSeconds <= 0)
                return;

            var mouseX = e.GetPosition(WaveformCanvas).X;
            var canvasW = WaveformCanvas.ActualWidth > 0 ? WaveformCanvas.ActualWidth : 800;
            var cursorX = (vm.CurrentPosition.TotalSeconds / vm.Duration.TotalSeconds) * canvasW;

            SeekPreviewOverlay.Visibility = Visibility.Visible;
            UpdateSeekPreview(cursorX, mouseX);
        }

        private void WaveformCanvas_MouseLeave(object sender, MouseEventArgs e)
        {
            SeekPreviewOverlay.Visibility = Visibility.Collapsed;
            SeekPreviewOverlay.Width = 0;
        }

        private void WaveformCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is PlaybackViewModel vm && vm.Duration.TotalSeconds > 0)
            {
                var position = e.GetPosition(WaveformCanvas);
                var width = WaveformCanvas.ActualWidth;
                var progress = position.X / width;
                var newPosition = TimeSpan.FromSeconds(vm.Duration.TotalSeconds * progress);
                vm.CurrentPosition = newPosition;
            }
        }

        private void AddComment_Click(object sender, RoutedEventArgs e)
        {
            SubmitComment();
        }

        private void CommentTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Capture the playback position on the very first character entered
            if (_commentStartTimestamp == null && CommentTextBox.Text.Length > 0)
            {
                _commentStartTimestamp = (DataContext as PlaybackViewModel)?.CurrentPosition.TotalSeconds;
            }
            else if (CommentTextBox.Text.Length == 0)
            {
                _commentStartTimestamp = null;
            }
        }

        private void CommentTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                SubmitComment();
                e.Handled = true;
            }
        }

        private void SubmitComment()
        {
            if (DataContext is PlaybackViewModel vm && !string.IsNullOrWhiteSpace(CommentTextBox.Text))
            {
                vm.AddComment(CommentTextBox.Text, _commentStartTimestamp);
                CommentTextBox.Clear();
                _commentStartTimestamp = null;
            }
        }

        private void AlbumTrackListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListView listView && listView.SelectedItem is PlaybackTrackListItem item && DataContext is PlaybackViewModel vm)
            {
                vm.PlayAlbumTrackCommand.Execute(item);
                e.Handled = true;
            }
        }

        private void EditCurrentTrack_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not PlaybackViewModel vm || !vm.IsOwnedTrack)
                return;

            var item = vm.SelectedAlbumTrack;
            if (item == null || string.IsNullOrWhiteSpace(item.FilePath))
                return;

            var editorVm = new MyMusicMetadataEditorViewModel();
            editorVm.LoadTrack(item.FilePath);

            var view = new MyMusicMetadataEditorView
            {
                DataContext = editorVm,
                Margin = new Thickness(8)
            };

            var window = new Window
            {
                Title = "Edit Local Music Metadata",
                Width = 500,
                Height = 620,
                Content = view,
                Owner = Application.Current.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            editorVm.RequestClose += (_, _) => window.Close();

            window.ShowDialog();
        }

        private void LikeCurrentTrack_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is PlaybackViewModel vm)
                vm.ToggleLikeCurrentTrack();
        }
    }
}
