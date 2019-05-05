using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace SteamFriendsPatcher
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static MainWindow MainWindowRef { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            bool isNewInstance = false;
            Mutex singleInstance = new Mutex(true, Assembly.GetExecutingAssembly().GetName().Name, out isNewInstance);
            if (!isNewInstance)
            {
                Application.Current.Shutdown(1);
            }
            else
            {
                MainWindowRef = new MainWindow();
                PerformUpgrade();
                string ver = ThisAssembly.AssemblyInformationalVersion;
                MainWindowRef.Title += $"v{(ver.Substring(0, ver.IndexOf('+') > -1 ? ver.IndexOf('+') : ver.Length))}";
                Task.Run(() => SetupTask());
                if (SteamFriendsPatcher.Properties.Settings.Default.saveLastWindowSize)
                {
                    MainWindowRef.Width = SteamFriendsPatcher.Properties.Settings.Default.windowWidth;
                    MainWindowRef.Height = SteamFriendsPatcher.Properties.Settings.Default.windowHeight;
                }

                if (SteamFriendsPatcher.Properties.Settings.Default.startMinimized)
                {
                    MainWindowRef.WindowState = WindowState.Minimized;
                    if (SteamFriendsPatcher.Properties.Settings.Default.minimizeToTray)
                    {
                        return;
                    }
                }
                MainWindowRef.Show();
            }
        }

        private static void SetupTask()
        {
            if (SteamFriendsPatcher.Properties.Settings.Default.checkForUpdates)
            {
                Task.Run(() => Program.UpdateChecker());
            }

            if (SteamFriendsPatcher.Properties.Settings.Default.forceScanOnStartup)
            {
                Program.FindCacheFile();
            }

            if (SteamFriendsPatcher.Properties.Settings.Default.autoScanOnStartup)
            {
                Program.ToggleCacheScanner(true);
            }

            if (SteamFriendsPatcher.Properties.Settings.Default.runSteamOnStartup && Process.GetProcessesByName("Steam").Length == 0)
            {
                Process.Start(Program.steamDir + "\\Steam.exe", SteamFriendsPatcher.Properties.Settings.Default.steamLaunchArgs);
            }
        }

        private static void PerformUpgrade()
        {
            if (SteamFriendsPatcher.Properties.Settings.Default.upgradeRequired)
            {
                SteamFriendsPatcher.Properties.Settings.Default.Upgrade();

                if (SteamFriendsPatcher.Properties.Settings.Default.upgradeVer == 0)
                {
                    if (File.Exists(Program.startupLinkOld))
                    {
                        File.Delete(Program.startupLinkOld);
                        Program.CreateStartUpShortcut();
                    }
                }

                SteamFriendsPatcher.Properties.Settings.Default.upgradeVer = 1;
                SteamFriendsPatcher.Properties.Settings.Default.upgradeRequired = false;
                SteamFriendsPatcher.Properties.Settings.Default.Save();
            }
        }
    }
}