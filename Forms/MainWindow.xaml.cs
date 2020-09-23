using SteamFriendsPatcher.Properties;

using System;
using System.ComponentModel;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;

using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using ContextMenu = System.Windows.Forms.ContextMenu;
using MenuItem = System.Windows.Forms.MenuItem;

namespace SteamFriendsPatcher.Forms
{
    /// <summary>
    ///     Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        public MainWindow()
        {
            Closing += MainWindow_Closing;
            SizeChanged += MainWindow_SizeChanged;
            StateChanged += MainWindow_StateChanged;
            SourceInitialized += MainWindow_SourceInitialized;
            InitializeComponent();
            SetupTrayIcon();
        }

        public NotifyIcon NotifyIcon { get; private set; }

        private static bool IsShuttingDown { get; set; }

        private void MainWindow_SourceInitialized(object sender, EventArgs e)
        {
            var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source?.AddHook(WndProc);
        }

        private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WmShowme) App.ShowMain();

            return IntPtr.Zero;
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (!Settings.Default.closeToTray)
            {
                Application.Current.Shutdown();
                return;
            }
            else if (!IsShuttingDown)
            {
                e.Cancel = true;
                GoToTray();
            }
        }

        public void OnExit()
        {
            NotifyIcon.Visible = false;
            FileWatcher.ToggleCacheScanner(false);
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!Settings.Default.saveLastWindowSize) return;
            Settings.Default.windowWidth = Width;
            Settings.Default.windowHeight = Height;
            Settings.Default.Save();
        }

        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            if (!Settings.Default.minimizeToTray || WindowState != WindowState.Minimized) return;
            GoToTray();
        }

        private void GoToTray()
        {
            Hide();
            NotifyIcon.Visible = Settings.Default.showTrayIconHidden;

            if (!Settings.Default.showNotificationsInTray) return;
            NotifyIcon.BalloonTipText = FileWatcher.scannerExists
                ? "Scanning in background..."
                : "Minimized to tray, scanner not running.";
            NotifyIcon.ShowBalloonTip((int)TimeSpan.FromSeconds(10).TotalMilliseconds);
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var settings = new SettingsWindow
            {
                Owner = this
            };
            settings.ShowDialog();
        }

        private void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            var about = new AboutWindow
            {
                Owner = this
            };
            about.ShowDialog();
        }

        private void SetupTrayIcon()
        {
            var contextMenu = new ContextMenu();
            var showButton = new MenuItem();
            var exitButton = new MenuItem();

            NotifyIcon = new NotifyIcon
            {
                Visible = false,
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location),
                Text = Title,
                ContextMenu = contextMenu
            };

            contextMenu.MenuItems.AddRange(new[] { showButton, exitButton });

            // show button
            showButton.Index = 0;
            showButton.Click += ShowButton_Click;
            showButton.Text = @"Show";

            // exit button
            exitButton.Index = 1;
            exitButton.Text = @"Exit";
            exitButton.Click += ExitButton_Click;

            // double click
            NotifyIcon.DoubleClick += ShowButton_Click;

            NotifyIcon.BalloonTipTitle = @"Steam Friends Patcher";
        }

        private static void ShowButton_Click(object sender, EventArgs e)
        {
            App.ShowMain();
        }

        private static void ExitButton_Click(object sender, EventArgs e)
        {
            Application.Current.Shutdown();
            IsShuttingDown = true;
        }

        private async void ToggleScanButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleButtons(false);
            await Task.Run(() => FileWatcher.ToggleCacheScanner(!FileWatcher.scannerExists)).ConfigureAwait(false);
        }

        private async void ForceCheckButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleButtons(false);
            await Task.Run(() => Program.FindCacheFile(true)).ConfigureAwait(false);
        }

        private async void ClearCacheButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleButtons(false);
            await Task.Run(Program.ClearSteamCache).ConfigureAwait(false);
        }

        public void ToggleButtons(bool status)
        {
            Dispatcher.Invoke(() =>
            {
                foreach (var item in LogicalTreeHelper.GetChildren(MainGrid))
                    if (item is Button button)
                        if (button.Name != "AboutButton" && button.Name != "SettingsButton")
                        {
                            button.IsEnabled = status;
                            button.Visibility = status ? Visibility.Visible : Visibility.Hidden;
                            if (button.Name == "ToggleScanButton")
                                button.Content = FileWatcher.scannerExists ? "Stop Scanning" : "Start Scanning";
                        }
            });
        }
    }
}