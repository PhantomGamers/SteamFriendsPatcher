using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Threading;

namespace SteamFriendsPatcher
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static MainWindow MainWindowRef { get; private set; }
        public static System.Timers.Timer UpdateTimer { get; private set; }

        public static bool UpdateTimerActive { get; private set; } = false;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            Mutex singleInstance = new Mutex(true, Assembly.GetExecutingAssembly().GetName().Name, out bool isNewInstance);
            if (!isNewInstance)
            {
                Current.Shutdown(1);
            }
            else
            {
                MainWindowRef = new MainWindow();
                PerformUpgrade();
                string ver = ThisAssembly.AssemblyInformationalVersion;
                MainWindowRef.Title += $"v{(ver.Substring(0, ver.IndexOf('+') > -1 ? ver.IndexOf('+') : ver.Length))}";
                Task.Run(() => Setup());
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

        private static void Setup()
        {
            if (SteamFriendsPatcher.Properties.Settings.Default.checkForUpdates)
            {
                Task.Run(() => Program.UpdateChecker());
                ToggleUpdateTimer();
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

        public static void ToggleUpdateTimer(bool status = true)
        {
            if (!UpdateTimerActive && !status)
                return;

            if (!status)
            {
                UpdateTimer.Enabled = false;
                return;
            }

            UpdateTimer = new System.Timers.Timer

            {
                Interval = System.TimeSpan.FromDays(1).TotalMilliseconds
            };

            UpdateTimer.Elapsed += UpdateTimer_Elapsed;
            UpdateTimer.Enabled = true;
            UpdateTimerActive = true;
        }

        private static void UpdateTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            Program.UpdateChecker();
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