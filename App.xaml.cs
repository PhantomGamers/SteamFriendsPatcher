using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace SteamFriendsPatcher
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private Mutex singleInstance;
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            bool isNewInstance = false;
            singleInstance = new Mutex(true, Assembly.GetExecutingAssembly().GetName().Name, out isNewInstance);
            if (!isNewInstance)
            {
                Application.Current.Shutdown(1);
            }
            else
            {
                new MainWindow();

                Version ver = Assembly.GetEntryAssembly().GetName().Version;
                MainWindow.Title = $"Steam Friends Patcher v{ver.Major}.{ver.Minor}{(ver.Build > 0 ? ("." + ver.Build) : string.Empty)}";

                if (SteamFriendsPatcher.Properties.Settings.Default.saveLastWindowSize)
                {
                    MainWindow.Width = SteamFriendsPatcher.Properties.Settings.Default.windowWidth;
                    MainWindow.Height = SteamFriendsPatcher.Properties.Settings.Default.windowHeight;
                }

                if (SteamFriendsPatcher.Properties.Settings.Default.startMinimized)
                {
                    MainWindow.WindowState = WindowState.Minimized;
                    if (SteamFriendsPatcher.Properties.Settings.Default.minimizeToTray)
                    {
                        return;
                    }
                }
                MainWindow.Show();
            }
        }
    }
}
