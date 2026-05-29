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
                var rect = new Rectangle
                {
                    Width = Math.Max(1, barWidth - 1),
                    Height = barHeight,
                    Fill = new SolidColorBrush(Color.FromRgb(33, 150, 243))
                };
                Canvas.SetLeft(rect, i * barWidth);
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

                var icon = new Ellipse
                {
                    Width = 12,
                    Height = 12,
                    Fill = Brushes.Orange,
                    Stroke = Brushes.White,
                    StrokeThickness = 1,
                    ToolTip = $"{marker.UserDisplayName}: {marker.Label}"
                };

                Canvas.SetLeft(icon, x - 6);
                Canvas.SetTop(icon, 4);
                WaveformCanvas.Children.Add(icon);
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
