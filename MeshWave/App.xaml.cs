using System.Windows;
using MeshWave.Services;
using MeshWave.ViewModels;

namespace MeshWave
{
    public partial class App : Application
    {
        private System.Windows.Forms.NotifyIcon? _trayIcon;
        private bool _isExiting;
        private bool _trayNotificationShown;

        // Accessed by MainWindow.OnClosing
        internal bool _IsExiting => _isExiting;
        internal bool _TrayNotificationShown
        {
            get => _trayNotificationShown;
            set => _trayNotificationShown = value;
        }

        internal void ShowTrayNotification(string title, string text, System.Windows.Forms.ToolTipIcon icon)
        {
            _trayIcon?.ShowBalloonTip(4000, title, text, icon);
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            CommandLineOverrides.Apply(e.Args);

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();

            base.OnStartup(e);
            InitializeTrayIcon();
        }

        private void InitializeTrayIcon()
        {
            // Load tray icon from the embedded ICO file.
            var iconStream = GetResourceStream(new Uri("pack://application:,,,/MeshWaveIcon128.ico"))?.Stream;

            System.Drawing.Icon? icon = null;
            if (iconStream != null)
            {
                try
                {
                    iconStream.Position = 0;
                    icon = new System.Drawing.Icon(iconStream);
                }
                catch
                {
                    // Fall back to system icon if embedded resource is invalid.
                    icon = System.Drawing.SystemIcons.Application;
                }
            }
            else
            {
                icon = System.Drawing.SystemIcons.Application;
            }

            _trayIcon = new System.Windows.Forms.NotifyIcon
            {
                Text = "MeshWave — Mesh is running",
                Visible = true,
                Icon = icon
            };

            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Open MeshWave", null, (_, _) => ShowMainWindow());
            menu.Items.Add("-");

            var nowPlayingItem = new System.Windows.Forms.ToolStripMenuItem("Now Playing");
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
            _isExiting = true;
            _trayIcon?.Dispose();
            _trayIcon = null;

            if (MainWindow?.DataContext is ApplicationViewModel vm)
            {
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
            }

            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _trayIcon?.Dispose();
            base.OnExit(e);
        }
    }
}

