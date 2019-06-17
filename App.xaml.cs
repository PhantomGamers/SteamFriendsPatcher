using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using SteamFriendsPatcher.Forms;

namespace SteamFriendsPatcher
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App
    {
        public static MainWindow MainWindowRef { get; private set; }
        public static System.Timers.Timer UpdateTimer { get; private set; }

        public static bool UpdateTimerActive { get; private set; }

        private static bool FirstShown { get; set; }

        private static readonly Mutex SingleInstance = new Mutex(true, Assembly.GetExecutingAssembly().GetName().Name);

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            if (SingleInstance.WaitOne(TimeSpan.Zero, true))
            {
                MainWindowRef = new MainWindow();

                PerformUpgrade();
                const string ver = ThisAssembly.AssemblyInformationalVersion;
                MainWindowRef.Title += $"v{ver.Substring(0, ver.IndexOf('+') > -1 ? ver.IndexOf('+') : ver.Length)}";
                Task.Run(Setup);
                if (SteamFriendsPatcher.Properties.Settings.Default.saveLastWindowSize)
                {
                    MainWindowRef.Width = SteamFriendsPatcher.Properties.Settings.Default.windowWidth;
                    MainWindowRef.Height = SteamFriendsPatcher.Properties.Settings.Default.windowHeight;
                }

                MainWindowRef.WindowState = WindowState.Minimized;
                MainWindowRef.Show();

                if (SteamFriendsPatcher.Properties.Settings.Default.minimizeToTray && SteamFriendsPatcher.Properties.Settings.Default.startMinimized)
                {
                    MainWindowRef.Hide();
                }

                if (!SteamFriendsPatcher.Properties.Settings.Default.minimizeToTray && SteamFriendsPatcher.Properties.Settings.Default.startMinimized)
                {
                    var workingArea = SystemParameters.WorkArea;
                    MainWindowRef.Left = (workingArea.Width - SteamFriendsPatcher.Properties.Settings.Default.windowWidth) / 2 + workingArea.Left;
                    MainWindowRef.Top = (workingArea.Height - SteamFriendsPatcher.Properties.Settings.Default.windowHeight) / 2 + workingArea.Top;
                    FirstShown = true;
                }

                if (!SteamFriendsPatcher.Properties.Settings.Default.startMinimized)
                {
                    MainWindowRef.WindowState = WindowState.Normal;
                }

                SingleInstance.ReleaseMutex();
            }
            else
            {
                NativeMethods.PostMessage(
                    (IntPtr)NativeMethods.HwndBroadcast,
                    NativeMethods.WmShowme,
                    IntPtr.Zero,
                    IntPtr.Zero);
                Current.Shutdown(1);
            }
        }

        private static void Setup()
        {
            if (SteamFriendsPatcher.Properties.Settings.Default.checkForUpdates)
            {
                Task.Run(Program.UpdateChecker);
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

            if (SteamFriendsPatcher.Properties.Settings.Default.runSteamOnStartup && Process.GetProcessesByName("Steam").FirstOrDefault() == null)
            {
                Process.Start(Program.steamDir + "\\Steam.exe", SteamFriendsPatcher.Properties.Settings.Default.steamLaunchArgs);
            }
        }

        public static void ShowMain()
        {
            if (!FirstShown)
            {
                var workingArea = SystemParameters.WorkArea;
                MainWindowRef.Left = (workingArea.Width - SteamFriendsPatcher.Properties.Settings.Default.windowWidth) / 2 + workingArea.Left;
                MainWindowRef.Top = (workingArea.Height - SteamFriendsPatcher.Properties.Settings.Default.windowHeight) / 2 + workingArea.Top;
                FirstShown = true;
            }

            if (!MainWindowRef.IsVisible)
                MainWindowRef.Show();

            if (MainWindowRef.WindowState == WindowState.Minimized)
                MainWindowRef.WindowState = WindowState.Normal;
            MainWindowRef.Activate();
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
                Interval = TimeSpan.FromDays(1).TotalMilliseconds
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
            if (!SteamFriendsPatcher.Properties.Settings.Default.upgradeRequired) return;
            SteamFriendsPatcher.Properties.Settings.Default.Upgrade();

            if (SteamFriendsPatcher.Properties.Settings.Default.upgradeVer == 0)
            {
                if (File.Exists(Program.StartupLinkOld))
                {
                    File.Delete(Program.StartupLinkOld);
                    Program.CreateStartUpShortcut();
                }
            }

            SteamFriendsPatcher.Properties.Settings.Default.upgradeVer = 1;
            SteamFriendsPatcher.Properties.Settings.Default.upgradeRequired = false;
            SteamFriendsPatcher.Properties.Settings.Default.Save();
        }
    }
}