using System.Diagnostics;
using System.Windows.Navigation;

namespace SteamFriendsPatcher.Forms
{
    /// <summary>
    /// Interaction logic for AboutWindow.xaml
    /// </summary>
    public partial class AboutWindow
    {
        public AboutWindow()
        {
            InitializeComponent();
            ProgramName.Content += $"v{ThisAssembly.AssemblyInformationalVersion}";
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri));
            e.Handled = true;
        }
    }
}