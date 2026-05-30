using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MeshWave.ViewModels;

namespace MeshWave.Views
{
    public partial class PlaybackView : UserControl
    {
        private PlaybackViewModel? _boundViewModel;

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
            }
            else if (args.PropertyName == nameof(_boundViewModel.WaveformSamples) ||
                     args.PropertyName == nameof(_boundViewModel.TimelineMarkers))
            {
                DrawWaveform();
            }
        }

        private void DrawWaveform()
        {
            WaveformCanvas.Children.Clear();
            var width = WaveformCanvas.ActualWidth > 0 ? WaveformCanvas.ActualWidth : 800;
            var height = WaveformCanvas.Height;
            var barCount = 100;
            var samples = Array.Empty<float>();

            if (DataContext is PlaybackViewModel vm && vm.WaveformSamples.Length > 0)
            {
                samples = vm.WaveformSamples;
                barCount = samples.Length;
            }

            var barWidth = width / barCount;
            for (int i = 0; i < barCount; i++)
            {
                var amplitude = samples.Length > i ? Math.Clamp(samples[i], 0f, 1f) : 0.2f;
                var barHeight = Math.Max(8, amplitude * (float)height);
                var left = i * barWidth;
                var right = (i + 1) * barWidth;
                var rect = new Rectangle
                {
                    Width = Math.Max(1, Math.Ceiling(right) - Math.Floor(left)),
                    Height = barHeight,
                    Fill = new SolidColorBrush(Color.FromRgb(33, 150, 243)),
                    SnapsToDevicePixels = true
                };
                RenderOptions.SetEdgeMode(rect, EdgeMode.Aliased);
                Canvas.SetLeft(rect, Math.Floor(left));
                Canvas.SetTop(rect, (height - barHeight) / 2);
                WaveformCanvas.Children.Add(rect);
            }

            DrawTimelineMarkers(width, height);
            WaveformCanvas.Children.Add(PlaybackCursor);
            UpdatePlaybackCursor();
        }

        private void DrawTimelineMarkers(double width, double height)
        {
            if (DataContext is not PlaybackViewModel vm || vm.Duration.TotalSeconds <= 0)
            {
                return;
            }

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

                if (!string.IsNullOrWhiteSpace(marker.UserIconPath) && File.Exists(marker.UserIconPath))
                {
                    try
                    {
                        markerIcon.Fill = new ImageBrush(new System.Windows.Media.Imaging.BitmapImage(new Uri(marker.UserIconPath, UriKind.Absolute)))
                        {
                            Stretch = Stretch.UniformToFill
                        };
                    }
                    catch
                    {
                        markerIcon.Fill = Brushes.Orange;
                    }
                }
                else
                {
                    markerIcon.Fill = Brushes.Orange;
                }

                markerIcon.MouseLeftButtonDown += TimelineMarker_MouseLeftButtonDown;

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
                    Text = $"[{TimeSpan.FromSeconds(marker.TimestampSeconds):mm\\:ss}] {marker.UserDisplayName}: {marker.Label}",
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
                Canvas.SetLeft(PlaybackCursor, progress * width);
            }
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
            if (DataContext is PlaybackViewModel vm && !string.IsNullOrWhiteSpace(CommentTextBox.Text))
            {
                vm.AddComment(CommentTextBox.Text, vm.CurrentPosition.TotalSeconds);
                CommentTextBox.Clear();
            }
        }
    }
}
