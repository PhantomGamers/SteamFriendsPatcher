using System;
using System.IO;
using System.Reflection;
using System.Windows;

namespace SteamFriendsPatcher
{
    /// <summary>
    /// Interaction logic for SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow : Window
    {
        // current settings window
        private static SettingsWindow settingsWindow = null;

        private static bool firstLoad = false;

        public SettingsWindow()
        {
            InitializeComponent();
            // when creating a new settings window, track it in the static var.
            settingsWindow = this;

            /*
            Console.WriteLine(Properties.Settings.Default.runOnStartup);
            Console.WriteLine(Properties.Settings.Default.startInTray);
            Console.WriteLine(Properties.Settings.Default.checkForUpdates);
            Console.WriteLine(Properties.Settings.Default.autoScanOnStartup);
            */

            // set checkbox states to user settings or defaults
            LoadCheckBoxStates();
        }

        public static SettingsWindow GetSettingsWindow()
        {
            return settingsWindow;
        }

        private void RunOnStartup_Changed(object sender, RoutedEventArgs e)
        {
            if (firstLoad)
            {
                Properties.Settings.Default.runOnStartup = runOnStartup.IsChecked.GetValueOrDefault(false);
                Properties.Settings.Default.Save();

                if (runOnStartup.IsChecked.GetValueOrDefault(false))
                {
                    Program.CreateStartUpShortcut();
                }
                else
                {
                    if (File.Exists(Program.startupLink))
                    {
                        File.Delete(Program.startupLink);
                    }
                }
            }
        }

        private void StartMinimized_Changed(object sender, RoutedEventArgs e)
        {
            if (firstLoad)
            {
                Properties.Settings.Default.startMinimized = startMinimized.IsChecked.GetValueOrDefault(false);
                Properties.Settings.Default.Save();
            }
        }

        private void MinimizeToTray_Changed(object sender, RoutedEventArgs e)
        {
            if (firstLoad)
            {
                Properties.Settings.Default.minimizeToTray = minimizeToTray.IsChecked.GetValueOrDefault(false);
                Properties.Settings.Default.Save();
            }
        }

        private void CheckForUpdates_Changed(object sender, RoutedEventArgs e)
        {
            if (firstLoad)
            {
                Properties.Settings.Default.checkForUpdates = checkForUpdates.IsChecked.GetValueOrDefault(false);
                Properties.Settings.Default.Save();
            }
        }

        private void AutoScanOnStartup_Changed(object sender, RoutedEventArgs e)
        {
            if (firstLoad)
            {
                Properties.Settings.Default.autoScanOnStartup = autoScanOnStartup.IsChecked.GetValueOrDefault(false);
                Properties.Settings.Default.Save();
            }
        }

        private void ForceScanOnStartup_Changed(object sender, RoutedEventArgs e)
        {
            if (firstLoad)
            {
                Properties.Settings.Default.forceScanOnStartup = forceScanOnStartup.IsChecked.GetValueOrDefault(false);
                Properties.Settings.Default.Save();
            }
        }

        private void SaveLastWindowSize_Changed(object sender, RoutedEventArgs e)
        {
            if (firstLoad)
            {
                Properties.Settings.Default.saveLastWindowSize = saveLastWindowSize.IsChecked.GetValueOrDefault(false);
                Properties.Settings.Default.Save();
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            settingsWindow = null;
            firstLoad = false;
        }

        private void ResetToDefaults_Click(object sender, RoutedEventArgs e)
        {
            firstLoad = false;
            Properties.Settings.Default.Reset();
            LoadCheckBoxStates();
        }

        private void LoadCheckBoxStates()
        {
            Properties.Settings.Default.runOnStartup = File.Exists(Program.startupLink);
            runOnStartup.IsChecked = Properties.Settings.Default.runOnStartup;
            startMinimized.IsChecked = Properties.Settings.Default.startMinimized;
            minimizeToTray.IsChecked = Properties.Settings.Default.minimizeToTray;
            checkForUpdates.IsChecked = Properties.Settings.Default.checkForUpdates;
            autoScanOnStartup.IsChecked = Properties.Settings.Default.autoScanOnStartup;
            forceScanOnStartup.IsChecked = Properties.Settings.Default.forceScanOnStartup;
            saveLastWindowSize.IsChecked = Properties.Settings.Default.saveLastWindowSize;
            firstLoad = true;
        }
    }
}