using System;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;

namespace SteamFriendsPatcher
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public System.Windows.Forms.NotifyIcon NotifyIcon { get; private set; }

        public MainWindow()
        {
            InitializeComponent();
            setupTrayIcon();
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            SettingsWindow settings = new SettingsWindow
            {
                Owner = this
            };
            settings.ShowDialog();
        }

        private void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            AboutWindow about = new AboutWindow
            {
                Owner = this
            };
            about.ShowDialog();
        }

        private void setupTrayIcon()
        {
            var contextMenu = new System.Windows.Forms.ContextMenu();
            var showButton = new System.Windows.Forms.MenuItem();
            var exitButton = new System.Windows.Forms.MenuItem();

            NotifyIcon = new System.Windows.Forms.NotifyIcon
            {
                Visible = true,
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location),
                Text = Title,
                ContextMenu = contextMenu
            };

            contextMenu.MenuItems.AddRange(new System.Windows.Forms.MenuItem[] { showButton, exitButton });

            // show button
            showButton.Index = 0;
            showButton.Click += new System.EventHandler(ShowButton_Click);
            showButton.Text = "Show";

            // exit button
            exitButton.Index = 1;
            exitButton.Text = "Exit";
            exitButton.Click += new System.EventHandler(ExitButton_Click);

            // double click
            NotifyIcon.DoubleClick += new System.EventHandler(ShowButton_Click);
        }

        private void ShowButton_Click(object sender, EventArgs e)
        {
            if (!this.IsVisible)
                this.Show();

            if (this.WindowState == WindowState.Minimized)
                this.WindowState = WindowState.Normal;
            this.Activate();
        }

        private static void ExitButton_Click(object sender, EventArgs e)
        {
            Application.Current.Shutdown();
            return;
        }

        private void ToggleScanButton_Click(object sender, RoutedEventArgs e)
        {
            Program.ToggleCacheScanner(!Program.scannerExists);
        }

        private async void ForceCheckButton_Click(object sender, RoutedEventArgs e)
        {
            await Task.Run(() => Program.FindCacheFile(true));
        }

        private void Main_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (Properties.Settings.Default.saveLastWindowSize)
            {
                Properties.Settings.Default.windowWidth = this.Width;
                Properties.Settings.Default.windowHeight = this.Height;
                Properties.Settings.Default.Save();
            }
        }

        private void Main_StateChanged(object sender, EventArgs e)
        {
            if (Properties.Settings.Default.minimizeToTray && this.WindowState == WindowState.Minimized)
            {
                this.Hide();
            }
        }

        private void Main_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            NotifyIcon.Visible = false;
            Program.ToggleCacheScanner(false);
        }

        private async void ClearCacheButton_Click(object sender, RoutedEventArgs e)
        {
            await Task.Run(() => Program.ClearSteamCache());
        }
    }
}