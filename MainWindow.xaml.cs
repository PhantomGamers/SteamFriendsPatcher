using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace SteamFriendsPatcher
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        TextBoxOutputter outputter;
        private System.Windows.Forms.NotifyIcon notifyIcon;
        public static RichTextBox outputRTB;
        public static Button scanButton;
        public static Button forceScanButton;
        public static MainWindow mainWindow;

        public MainWindow()
        {
            InitializeComponent();
            mainWindow = this;
            outputRTB = this.output;
            scanButton = this.toggleScanButton;
            forceScanButton = this.forceCheckButton;
            outputter = new TextBoxOutputter(output);
            Console.SetOut(outputter);
            if (Properties.Settings.Default.saveLastWindowSize)
            {
                mainWindow.Width = Properties.Settings.Default.windowWidth;
                mainWindow.Height = Properties.Settings.Default.windowHeight;
            }

            if (Properties.Settings.Default.startMinimized)
            {
                mainWindow.WindowState = WindowState.Minimized;
                if (Properties.Settings.Default.minimizeToTray)
                {
                    mainWindow.Hide();
                }
            }
            Task task = new Task(() => { setupTask(); });
            task.Start();
            setupTrayIcon();
        }

        private static void setupTask()
        {
            if (Properties.Settings.Default.checkForUpdates)
            {
                Program.StartCheckForUpdateTask();
            }

            if (Properties.Settings.Default.forceScanOnStartup)
            {
                Program.FindCacheFile();
            }

            if (Properties.Settings.Default.autoScanOnStartup)
            {
                Program.StartScannerTask();
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            SettingsWindow settingsWindow = SettingsWindow.GetSettingsWindow();
            if(settingsWindow == null)
            {
                settingsWindow = new SettingsWindow
                {
                    Owner = this
                };
                settingsWindow.Show();
            }
            else
            {
                settingsWindow.Activate();
            }
        }

        private void setupTrayIcon()
        {
            var contextMenu = new System.Windows.Forms.ContextMenu();
            var showButton = new System.Windows.Forms.MenuItem();
            var exitButton = new System.Windows.Forms.MenuItem();

            this.notifyIcon = new System.Windows.Forms.NotifyIcon
            {
                Visible = true,
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location),
                Text = Title,
                ContextMenu = contextMenu
            };

            contextMenu.MenuItems.AddRange(new System.Windows.Forms.MenuItem[] {showButton, exitButton});

            // show button
            showButton.Index = 0;
            showButton.Click += new System.EventHandler(ShowButton_Click);
            showButton.Text = "Show";

            // exit button
            exitButton.Index = 1;
            exitButton.Text = "Exit";
            exitButton.Click += new System.EventHandler(ExitButton_Click);

            // double click
            this.notifyIcon.DoubleClick += new System.EventHandler(ShowButton_Click);
        }

        private void ShowButton_Click(object sender, EventArgs e)
        {
            if(this.WindowState == WindowState.Minimized)
            {
                this.Show();
                this.WindowState = WindowState.Normal;
            }
            this.Activate();
        }

        private static void ExitButton_Click(object sender, EventArgs e)
        {
            Application.Current.Shutdown();
            return;
        }

        private void ToggleScanButton_Click(object sender, RoutedEventArgs e)
        {
            if (!Program.scannerActive)
            {
                Program.StartScannerTask();
            }
            else
            {
                Program.scannerActive = false;
            }
        }

        private void ForceCheckButton_Click(object sender, RoutedEventArgs e)
        {
            Task task = new Task(() => { TriggerForceCheck(); });
            task.Start();
        }

        private static void TriggerForceCheck()
        {
            Program.GetLatestFriendsCSS(true);
            Program.FindCacheFile();
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
            notifyIcon.Visible = false;
            Program.scannerActive = false;
        }
    }
}