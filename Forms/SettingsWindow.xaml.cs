using SteamFriendsPatcher.Properties;

using System;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace SteamFriendsPatcher.Forms
{
    /// <summary>
    ///     Interaction logic for SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow
    {
        public ObservableCollection<ComboBoxItem> LibraryCssList { get; set; } = new ObservableCollection<ComboBoxItem>() { new ComboBoxItem { Content = "5.css" }, new ComboBoxItem { Content = "6.css" } };
        public ComboBoxItem SelectedLibraryCss { get; set; } = new ComboBoxItem { Content = FileWatcher.libraryRootCss };

        public SettingsWindow()
        {
            InitializeComponent();
            DataContext = this;
            LoadCheckBoxStates();
        }

        private void ResetToDefaults_Click(object sender, RoutedEventArgs e)
        {
            foreach (object item in LogicalTreeHelper.GetChildren(SettingsGrid))
            {
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
        }

        private void LoadCheckBoxStates()
        {
            Settings.Default.Reload();
            Settings.Default.startWithWindows = File.Exists(Utilities.StartupLink);
            foreach (object item in LogicalTreeHelper.GetChildren(SettingsGrid))
            {
                switch (item)
                {
                    case CheckBox chkCast:
                        chkCast.IsChecked = bool.Parse(Settings.Default[chkCast.Name].ToString());
                        break;
                    case TextBox txtCast:
                        txtCast.Text = Settings.Default[txtCast.Name].ToString();
                        break;
                    case ComboBox cmbxCast:
                        if (cmbxCast.Name == "steamLocale")
                        {
                            switch (Settings.Default[cmbxCast.Name].ToString())
                            {
                                case "":
                                    cmbxCast.SelectedIndex = 0;
                                    break;
                                case "CN":
                                    cmbxCast.SelectedIndex = 1;
                                    break;
                                default:
                                    cmbxCast.SelectedIndex = 2;
                                    break;

                            }
                        }
                        break;
                }
            }

            LibraryCssList.Clear();
            foreach (string s in Directory.EnumerateFiles(Path.Combine(Program.LibraryUIDir, "css"), "*.css", SearchOption.TopDirectoryOnly).Select(Path.GetFileName))
            {
                var sItem = new ComboBoxItem { Content = s };
                LibraryCssList.Add(sItem);
                if (s == FileWatcher.libraryRootCss)
                {
                    SelectedLibraryCss = sItem;
                }
            }

        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            Settings.Default.Reload();
            bool checkForUpdatesSetting = Settings.Default.checkForUpdates;
            foreach (object item in LogicalTreeHelper.GetChildren(SettingsGrid))
            {
                switch (item)
                {
                    case CheckBox chkCast:
                        Settings.Default[chkCast.Name] = chkCast.IsChecked;
                        break;
                    case TextBox txtCast:
                        Settings.Default[txtCast.Name] = txtCast.Text;
                        break;
                    case ComboBox cmbxCast:
                        if (cmbxCast.Name == "steamLocale")
                        {
                            switch (cmbxCast.SelectedIndex)
                            {
                                case 0:
                                    Settings.Default[cmbxCast.Name] = string.Empty;
                                    break;
                                case 1:
                                    Settings.Default[cmbxCast.Name] = "CN";
                                    break;
                            }
                        }
                        break;
                }
            }

            Settings.Default.steamLocaleArgs = Settings.Default.steamLocaleArgs; // what was this for???
            Settings.Default.libraryRootCss = (string)SelectedLibraryCss.Content;

            Settings.Default.Save();

            if (Settings.Default.startWithWindows && !File.Exists(Utilities.StartupLink))
            {
                Utilities.CreateStartUpShortcut();
            }
            else if (!Settings.Default.startWithWindows && File.Exists(Utilities.StartupLink))
            {
                File.Delete(Utilities.StartupLink);
            }

            if (!checkForUpdatesSetting && Settings.Default.checkForUpdates)
            {
                _ = Task.Run(Program.UpdateChecker);
                App.ToggleUpdateTimer();
            }

            if (checkForUpdatesSetting && !Settings.Default.checkForUpdates)
            {
                App.ToggleUpdateTimer(false);
            }

            FileWatcher.libraryRootCss = (string)SelectedLibraryCss.Content;

            if (Settings.Default.libraryRootCss.Length >= 5)
            {
                FileWatcher.libraryRootCss = Settings.Default.libraryRootCss;
            }

            App.MainWindowRef.NotifyIcon.Visible = Settings.Default.showTrayIconWindow;

            Close();
        }

        private void CancelChanges_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OpenConfigPath_Click(object sender, RoutedEventArgs e)
        {
            _ = Process.Start(Path.GetDirectoryName(ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.PerUserRoamingAndLocal).FilePath));
        }
    }
}