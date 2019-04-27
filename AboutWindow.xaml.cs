using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace SteamFriendsPatcher
{
    /// <summary>
    /// Interaction logic for AboutWindow.xaml
    /// </summary>
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            this.programName.Content += $"v{ThisAssembly.AssemblyInformationalVersion}";
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri));
            e.Handled = true;
        }
    }
}