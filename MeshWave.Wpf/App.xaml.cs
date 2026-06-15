using System.Windows;
using MeshWave.Common.Core;
using MeshWave.Wpf.Services;
using MeshWave.Wpf.ViewModels;
using System.IO;

namespace MeshWave.Wpf;

public partial class App : Application
{
    private NotifyIcon? _trayIcon;

    // Accessed by MainWindow.OnClosing
    internal bool _IsExiting { get; private set; }

    internal bool _TrayNotificationShown { get; set; }

    internal void ShowTrayNotification(string title, string text, ToolTipIcon icon)
    {
        _trayIcon?.ShowBalloonTip(4000, title, text, icon);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        var environment = new MeshWaveEnvironment();
        CommandLineOverrides.Apply(e.Args, environment);

        var mockScenario = Environment.GetEnvironmentVariable(MeshWaveEnvironment.MockScenarioEnvironmentVariable);
        IMeshWaveEnvironment activeEnvironment = environment;

        if (!string.IsNullOrWhiteSpace(mockScenario))
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"mw_mock_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            activeEnvironment = new MeshWave.TestUtilities.DummyEnvironment(tempDir);
        }

        var settingsService = new SettingsService(activeEnvironment.GetAppDataRoot());
        var settings = settingsService.LoadSettings();
        LoggingConfiguration.Configure(activeEnvironment, settings.Logging);

        var appViewModel = new ApplicationViewModel(activeEnvironment, settingsService, new UserProfileService(activeEnvironment.GetAppDataRoot()));
        if (!string.IsNullOrWhiteSpace(mockScenario))
        {
            appViewModel.IsMockMode = true;
        }

        var mainWindow = new MainWindow
        {
            DataContext = appViewModel
        };
        MainWindow = mainWindow;
        mainWindow.Show();

        if (!string.IsNullOrWhiteSpace(mockScenario))
        {
            _ = MockScenarioRunner.ApplyScenarioAsync(mockScenario, appViewModel);
        }

        base.OnStartup(e);
        InitializeTrayIcon();
    }

    private void InitializeTrayIcon()
    {
        // Load tray icon from the embedded ICO file.
        var iconStream = GetResourceStream(new Uri("pack://application:,,,/MeshWaveIcon128.ico"))?.Stream;

        Icon? icon = null;
        if (iconStream != null)
            try
            {
                iconStream.Position = 0;
                icon = new Icon(iconStream);
            }
            catch
            {
                // Fall back to system icon if embedded resource is invalid.
                icon = SystemIcons.Application;
            }
        else
            icon = SystemIcons.Application;

        _trayIcon = new NotifyIcon
        {
            Text = "MeshWave — Mesh is running",
            Visible = true,
            Icon = icon
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open MeshWave", null, (_, _) => ShowMainWindow());
        menu.Items.Add("-");

        var nowPlayingItem = new ToolStripMenuItem("Now Playing");
        nowPlayingItem.Click += (_, _) =>
        {
            ShowMainWindow();
            if (MainWindow?.DataContext is ApplicationViewModel vm)
                vm.NavigateToPlayback();
        };
        menu.Items.Add(nowPlayingItem);

        menu.Items.Add("-");
        menu.Items.Add("Quit", null, (_, _) => ExitApplication());

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        if (MainWindow == null) return;
        MainWindow.Show();
        MainWindow.WindowState = WindowState.Normal;
        MainWindow.Activate();

        if (MainWindow.DataContext is ApplicationViewModel vm)
            vm.PersistPlaybackState();
    }

    internal async void ExitApplication()
    {
        _IsExiting = true;
        _trayIcon?.Dispose();
        _trayIcon = null;

        if (MainWindow?.DataContext is ApplicationViewModel vm)
            try
            {
                var shutdownTask = vm.ShutdownAsync();
                var completed = await Task.WhenAny(shutdownTask, Task.Delay(2000));
                if (completed == shutdownTask)
                    await shutdownTask;
            }
            catch
            {
                // best effort shutdown path for tray quit
            }

        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}