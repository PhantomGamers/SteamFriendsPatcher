using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace SteamFriendsPatcher
{
    /// <summary>
    /// Interaction logic for SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            LoadCheckBoxStates();
        }

        private void ResetToDefaults_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in LogicalTreeHelper.GetChildren(settingsGrid))
            {
                if (item is CheckBox chkCast)
                    chkCast.IsChecked = bool.Parse(Properties.Settings.Default.Properties[chkCast.Name].DefaultValue.ToString());

                if (item is TextBox txtCast)
                    txtCast.Text = Properties.Settings.Default.Properties[txtCast.Name].DefaultValue.ToString();
            }
        }

        private void LoadCheckBoxStates()
        {
            Properties.Settings.Default.startWithWindows = File.Exists(Program.startupLink);
            foreach (var item in LogicalTreeHelper.GetChildren(settingsGrid))
            {
                if (item is CheckBox chkCast)
                    chkCast.IsChecked = bool.Parse(Properties.Settings.Default[chkCast.Name].ToString());

                if (item is TextBox txtCast)
                    txtCast.Text = Properties.Settings.Default[txtCast.Name].ToString();
            }
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in LogicalTreeHelper.GetChildren(settingsGrid))
            {
                if (item is CheckBox chkCast)
                {
                    Properties.Settings.Default[chkCast.Name] = chkCast.IsChecked;
                    Debug.WriteLine(chkCast.Name + "=" + Properties.Settings.Default[chkCast.Name].ToString());
                }

                if (item is TextBox txtCast)
                    Properties.Settings.Default[txtCast.Name] = txtCast.Text;
            }

            Properties.Settings.Default.Save();

            if (Properties.Settings.Default.startWithWindows && !File.Exists(Program.startupLink))
                Program.CreateStartUpShortcut();
            else if (!Properties.Settings.Default.startWithWindows && File.Exists(Program.startupLink))
                File.Delete(Program.startupLink);

            this.Close();
        }

        private void cancelChanges_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}