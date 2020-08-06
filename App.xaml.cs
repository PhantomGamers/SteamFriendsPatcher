using SteamFriendsPatcher.Forms;
using SteamFriendsPatcher.Properties;

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;

using Timer = System.Timers.Timer;

namespace SteamFriendsPatcher
{
    /// <summary>
    ///     Interaction logic for App.xaml
    /// </summary>
    public partial class App
    {
        private static readonly Mutex SingleInstance = new Mutex(true, Assembly.GetExecutingAssembly().GetName().Name);
        public static MainWindow MainWindowRef { get; private set; }
        // ReSharper disable once RedundantNameQualifier
        public static Timer UpdateTimer { get; private set; }

        public static bool UpdateTimerActive { get; private set; }

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
                if (Settings.Default.saveLastWindowSize)
                {
                    MainWindowRef.Width = Settings.Default.windowWidth;
                    MainWindowRef.Height = Settings.Default.windowHeight;
                }

                MainWindowRef.WindowState = WindowState.Minimized;
                var workingArea = SystemParameters.WorkArea;
                MainWindowRef.Left = (workingArea.Width - Settings.Default.windowWidth) / 2 + workingArea.Left;
                MainWindowRef.Top = (workingArea.Height - Settings.Default.windowHeight) / 2 + workingArea.Top;

                MainWindowRef.Show();

                if (Settings.Default.minimizeToTray && Settings.Default.startMinimized)
                {
                    MainWindowRef.NotifyIcon.Visible = Settings.Default.showTrayIconHidden;
                    MainWindowRef.Hide();
                }
                else
                {
                    MainWindowRef.NotifyIcon.Visible = Settings.Default.showTrayIconWindow;
                }

                if (!Settings.Default.startMinimized) MainWindowRef.WindowState = WindowState.Normal;

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

        private void OnExit(object sender, ExitEventArgs e)
        {
            MainWindowRef.OnExit();
        }

        private static void Setup()
        {
            if (Settings.Default.checkForUpdates)
            {
                Task.Run(Program.UpdateChecker);
                ToggleUpdateTimer();
            }

            if (Settings.Default.forceScanOnStartup) Program.FindCacheFile();

            if (Settings.Default.autoScanOnStartup) FileWatcher.ToggleCacheScanner(true);

            if (Settings.Default.runSteamOnStartup && Process.GetProcessesByName("Steam").FirstOrDefault() == null)
                Process.Start(Program.steamDir + "\\Steam.exe", Settings.Default.steamLaunchArgs);
        }

        public static void ShowMain()
        {
            if (!MainWindowRef.IsVisible)
                MainWindowRef.Show();

            MainWindowRef.NotifyIcon.Visible = Settings.Default.showTrayIconWindow;

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

            UpdateTimer = new Timer

            {
                Interval = TimeSpan.FromDays(1).TotalMilliseconds
            };

            UpdateTimer.Elapsed += UpdateTimer_Elapsed;
            UpdateTimer.Enabled = true;
            UpdateTimerActive = true;
        }

        private static void UpdateTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            Program.UpdateChecker();
        }

        private static void PerformUpgrade()
        {
            if (!Settings.Default.upgradeRequired) return;
            Settings.Default.Upgrade();

            if (Settings.Default.upgradeVer == 0)
                if (File.Exists(Utilities.StartupLinkOld))
                {
                    File.Delete(Utilities.StartupLinkOld);
                    Utilities.CreateStartUpShortcut();
                }

            Settings.Default.upgradeVer = 1;
            Settings.Default.upgradeRequired = false;
            Settings.Default.Save();
        }
    }
}