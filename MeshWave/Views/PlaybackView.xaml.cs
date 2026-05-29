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
        public PlaybackView()
        {
            InitializeComponent();
            Loaded += PlaybackView_Loaded;
        }

        private void PlaybackView_Loaded(object sender, RoutedEventArgs e)
        {
            DrawWaveform();
            if (DataContext is PlaybackViewModel vm)
            {
                vm.PropertyChanged += (s, args) =>
                {
                    if (args.PropertyName == nameof(vm.CurrentPosition))
                    {
                        UpdatePlaybackCursor();
                    }
                };
            }
        }

        private void DrawWaveform()
        {
            // TODO: Generate actual waveform from audio file
            // For now, draw a placeholder waveform
            WaveformCanvas.Children.Clear();
            var random = new Random();
            var width = WaveformCanvas.ActualWidth > 0 ? WaveformCanvas.ActualWidth : 800;
            var height = WaveformCanvas.Height;
            var barCount = 100;
            var barWidth = width / barCount;

            for (int i = 0; i < barCount; i++)
            {
                var barHeight = random.Next(20, (int)height);
                var rect = new Rectangle
                {
                    Width = barWidth - 2,
                    Height = barHeight,
                    Fill = new SolidColorBrush(Color.FromRgb(33, 150, 243))
                };
                Canvas.SetLeft(rect, i * barWidth);
                Canvas.SetTop(rect, (height - barHeight) / 2);
                WaveformCanvas.Children.Add(rect);
            }

            // Re-add cursor on top
            WaveformCanvas.Children.Add(PlaybackCursor);
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
