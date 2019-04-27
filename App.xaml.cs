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
                string ver = ThisAssembly.AssemblyInformationalVersion;
                MainWindow.Title += $"v{ver.Substring(0, ver.IndexOf('+') > -1 ? ver.IndexOf('+') : ver.Length)}";
                Task.Run(() => SteamFriendsPatcher.MainWindow.SetupTask());
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