using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using MeshWave.Wpf.Services;
using MeshWave.Wpf.ViewModels;

namespace MeshWave.Wpf.Views;

public partial class PlayerBarView
{
    private PlaybackViewModel? _boundViewModel;

    public PlayerBarView()
    {
        InitializeComponent();
        DataContextChanged += PlayerBarView_DataContextChanged;
    }

    private void PlayerBarView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_boundViewModel != null) _boundViewModel.PropertyChanged -= ViewModelOnPropertyChanged;

        _boundViewModel = DataContext as PlaybackViewModel;

        if (_boundViewModel != null) _boundViewModel.PropertyChanged += ViewModelOnPropertyChanged;

        DrawWaveform();
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_boundViewModel == null) return;

        if (args.PropertyName == nameof(_boundViewModel.CurrentPosition))
            UpdatePlaybackProgress();
        else if (args.PropertyName == nameof(_boundViewModel.WaveformSamples)) DrawWaveform();
    }

    private void WaveformContainer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        DrawWaveform();
    }

    private void DrawWaveform()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(DrawWaveform);
            return;
        }

        var width = WaveformContainer.ActualWidth;
        var height = WaveformContainer.ActualHeight;

        if (width <= 0 || height <= 0)
            return;

        var samples = Array.Empty<float>();

        if (_boundViewModel != null && _boundViewModel.WaveformSamples.Length > 0) samples = _boundViewModel.WaveformSamples;

        // Cloudy gives us the "alternation bars" style
        var geometry = WaveformRenderer.Render(samples, width, height, WaveformStyle.Cloudy);

        WaveformPathDim.Data = geometry;
        WaveformPathBright.Data = geometry;

        // Create a rainbow linear gradient brush (mint green -> yellow/orange)
        var rainbowBrush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0)
        };
        rainbowBrush.GradientStops.Add(new GradientStop(Color.FromRgb(30, 45, 200), 0.0));
        rainbowBrush.GradientStops.Add(new GradientStop(Color.FromRgb(85, 185, 150), 0.2));   // Mint Green
        rainbowBrush.GradientStops.Add(new GradientStop(Color.FromRgb(250, 10, 120), 0.5));  // Transition
        rainbowBrush.GradientStops.Add(new GradientStop(Color.FromRgb(210, 165, 90), 0.8));   // Yellow/Orange
        rainbowBrush.GradientStops.Add(new GradientStop(Color.FromRgb(240, 225, 20), 1.0));   // Yellow/Orange

        WaveformPathDim.Fill = rainbowBrush;
        WaveformPathBright.Fill = rainbowBrush;

        UpdatePlaybackProgress();
    }

    private void UpdatePlaybackProgress()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(UpdatePlaybackProgress);
            return;
        }

        if (_boundViewModel != null && _boundViewModel.Duration.TotalSeconds > 0)
        {
            var progress = _boundViewModel.CurrentPosition.TotalSeconds / _boundViewModel.Duration.TotalSeconds;
            var width = WaveformContainer.ActualWidth;

            // Use Max(0, ...) in case the percentage is slightly out of bounds, but Math.Clamp is safer
            PlayedWaveformCanvas.Width = Math.Clamp(progress * width, 0, width);
        }
        else
        {
            PlayedWaveformCanvas.Width = 0;
        }
    }
}