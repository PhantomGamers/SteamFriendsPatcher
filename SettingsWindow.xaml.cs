using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

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
        private readonly string startupLink = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                                    @"Microsoft\Windows\Start Menu\Programs\Startup",
                                                    Assembly.GetExecutingAssembly().GetName().Name + ".url");

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
                    using (StreamWriter writer = new StreamWriter(startupLink))
                    {
                        string app = System.Reflection.Assembly.GetExecutingAssembly().Location;
                        writer.WriteLine("[InternetShortcut]");
                        writer.WriteLine("URL=file:///" + app);
                        writer.WriteLine("IconIndex=0");
                        string icon = app.Replace('\\', '/');
                        writer.WriteLine("IconFile=" + icon);
                        writer.Flush();
                    }
                }
                else
                {
                    if (File.Exists(startupLink))
                    {
                        File.Delete(startupLink);
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
            Properties.Settings.Default.runOnStartup = File.Exists(startupLink);
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