using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using SteamFriendsPatcher.Properties;

namespace SteamFriendsPatcher.Forms
{
    /// <summary>
    ///     Interaction logic for SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow
    {
        public SettingsWindow()
        {
            InitializeComponent();
            LoadCheckBoxStates();
        }

        private void ResetToDefaults_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in LogicalTreeHelper.GetChildren(SettingsGrid))
                switch (item)
                {
                    case CheckBox chkCast:
                        chkCast.IsChecked =
                            bool.Parse(Settings.Default.Properties[chkCast.Name]?.DefaultValue.ToString() ??
                                       throw new InvalidOperationException());
                        break;
                    case TextBox txtCast:
                        txtCast.Text = Settings.Default.Properties[txtCast.Name]?.DefaultValue.ToString() ??
                                       throw new InvalidOperationException();
                        break;
                }
        }

        private void LoadCheckBoxStates()
        {
            Settings.Default.startWithWindows = File.Exists(Program.StartupLink);
            foreach (var item in LogicalTreeHelper.GetChildren(SettingsGrid))
                switch (item)
                {
                    case CheckBox chkCast:
                        chkCast.IsChecked = bool.Parse(Settings.Default[chkCast.Name].ToString());
                        break;
                    case TextBox txtCast:
                        txtCast.Text = Settings.Default[txtCast.Name].ToString();
                        break;
                }
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            var checkForUpdatesSetting = Settings.Default.checkForUpdates;
            foreach (var item in LogicalTreeHelper.GetChildren(SettingsGrid))
                switch (item)
                {
                    case CheckBox chkCast:
                        Settings.Default[chkCast.Name] = chkCast.IsChecked;
                        break;
                    case TextBox txtCast:
                        Settings.Default[txtCast.Name] = txtCast.Text;
                        break;
                }

            Settings.Default.Save();

            if (Settings.Default.startWithWindows && !File.Exists(Program.StartupLink))
                Program.CreateStartUpShortcut();
            else if (!Settings.Default.startWithWindows && File.Exists(Program.StartupLink))
                File.Delete(Program.StartupLink);

            if (!checkForUpdatesSetting && Settings.Default.checkForUpdates)
            {
                Task.Run(Program.UpdateChecker);
                App.ToggleUpdateTimer();
            }

            if (checkForUpdatesSetting && !Settings.Default.checkForUpdates)
                App.ToggleUpdateTimer(false);

            Close();
        }

        private void CancelChanges_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}