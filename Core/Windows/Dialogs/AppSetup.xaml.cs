using PZTools.Core.Functions;
using System.IO;
using System.Windows;
using Application = System.Windows.Forms.Application;

namespace PZTools.Core.Windows.Dialogs
{
    /// <summary>
    /// Interaction logic for AppSetup.xaml
    /// </summary>
    public partial class AppSetup : Window
    {
        public AppSetup()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {

        }

        #region Radio Button Logic
        private void GameInstallOption_Checked(object sender, RoutedEventArgs e)
        {
            if (this.IsLoaded)
                UpdateGameInstallOption();
        }

        private void UpdateGameInstallOption()
        {
            if (rdoExistingGame.IsChecked == true)
            {
                txtExistingGamePath.IsEnabled = true;
                txtManagedGamePath.IsEnabled = false;
                changeMangedBtn.Visibility = Visibility.Collapsed;
                browseExistingBtn.Visibility = Visibility.Visible;
            }
            else if (rdoManagedGame.IsChecked == true)
            {
                if (txtAppInstallPath.Text.NotNullOrEmpty()) 
                    txtManagedGamePath.Text = Path.Combine(txtAppInstallPath.Text, "Zomboid");

                txtExistingGamePath.IsEnabled = false;
                txtManagedGamePath.IsEnabled = true;
                changeMangedBtn.Visibility = Visibility.Visible;
                browseExistingBtn.Visibility = Visibility.Collapsed;
            }
        }
        #endregion

        #region Browse Buttons
        private void BrowseAppInstallButton_Click(object sender, RoutedEventArgs e)
        {
            string path = OpenFolderBrowser("Select installation folder for PZ Tools (this should be a new folder)");
            if (!string.IsNullOrEmpty(path))
            {
                if (Directory.GetFiles(path).Length > 0) { MessageBox.Show("The selected folder is not empty. Please select an empty folder for installation.", "Invalid Folder", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
                txtAppInstallPath.Text = path;
            }
        }

        private void BrowseGameInstallButton_Click(object sender, RoutedEventArgs e)
        {
            string path = OpenFolderBrowser("Select existing Project Zomboid installation");
            if (!string.IsNullOrEmpty(path))
            {
                txtExistingGamePath.Text = path;
            }
        }

        private void BrowseManagedGamePath_Click(object sender, RoutedEventArgs e)
        {
            string path = OpenFolderBrowser("Select folder for PZ Tools managed installations");
            if (!string.IsNullOrEmpty(path))
            {
                txtManagedGamePath.Text = path;
            }
        }

        private string OpenFolderBrowser(string description)
        {
            using (var Dialogs = new FolderBrowserDialog())
            {
                Dialogs.Description = description;
                Dialogs.ShowNewFolderButton = true;
                if (Dialogs.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    return Dialogs.SelectedPath;
                }
            }
            return null;
        }
        #endregion

        #region Buttons
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Are you sure you want to cancel the setup? The application will exit.", "Cancel Setup", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes) {
                DialogResult = false;
                this.Close();
            }
        }

        private async void FinishButton_Click(object sender, RoutedEventArgs e)
        {
            // Validate app install path
            if (string.IsNullOrWhiteSpace(txtAppInstallPath.Text) || !Directory.Exists(txtAppInstallPath.Text))
            {
                MessageBox.Show("Please select a valid installation folder for PZ Tools.", "Invalid Path", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Validate game installation path based on mode
            if (rdoExistingGame.IsChecked == true)
            {
                if (string.IsNullOrWhiteSpace(txtExistingGamePath.Text) || !Directory.Exists(txtExistingGamePath.Text))
                {
                    MessageBox.Show("Please select a valid Project Zomboid installation folder.", "Invalid Path", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            else if (rdoManagedGame.IsChecked == true)
            {
                if (string.IsNullOrWhiteSpace(txtManagedGamePath.Text))
                {
                    MessageBox.Show("Please select a valid folder for managed installations.", "Invalid Path", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            App.SetCurrentDirectory(txtAppInstallPath.Text);

            SetupStatus.Text = "Setting up PZTools, please wait...";
            await StartSetupTasks();

            // Close setup
            this.DialogResult = true;
            Application.Exit();
        }
        #endregion

        private void SaveSettings()
        {
            Functions.Config.StoreVariable(Functions.VariableType.system, "AppInstallPath", txtAppInstallPath.Text);
            Functions.Config.StoreVariable(Functions.VariableType.system, "GameMode", rdoExistingGame.IsChecked == true ? "existing" : "managed");

            if (rdoExistingGame.IsChecked == true)
                Functions.Config.StoreVariable(Functions.VariableType.system, "ExistingGamePath", txtExistingGamePath.Text);
            else 
                Functions.Config.StoreVariable(Functions.VariableType.system, "ManagedGamePath", txtManagedGamePath.Text);
        }

        private async Task StartSetupTasks()
        {
            string instalDir = txtAppInstallPath.Text;
            string destExe = Path.Combine(instalDir, Path.GetFileName(App.currentFilePath));

            File.Copy(App.currentFilePath, destExe, true);
            Directory.CreateDirectory(Path.Combine(instalDir, "Configs"));
            Directory.CreateDirectory(Path.Combine(instalDir, "Logs"));

            SaveSettings();

            if ((bool)chkDesktopShortcut.IsChecked)
            {
                string shortcutPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    "PZTools.lnk"
                );
                WindowsHelpers.CreateShortcut(shortcutPath, destExe, "Project Zomboid Tools");
                await Console.Log($"Desktop shortcut created at {shortcutPath}");
            }

            if ((bool)chkStartMenuShortcut.IsChecked)
            {
                string startMenuDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                    "PZTools"
                );
                Directory.CreateDirectory(startMenuDir);
                string shortcutPath = Path.Combine(startMenuDir, "PZTools.lnk");
                WindowsHelpers.CreateShortcut(shortcutPath, destExe, "Project Zomboid Tools");
                await Console.Log($"Start menu shortcut created at {shortcutPath}");
            }

            if ((bool)rdoManagedGame.IsChecked)
            {
                Directory.CreateDirectory(Path.Combine(instalDir, "SteamCMD"));
                Directory.CreateDirectory(Path.Combine(instalDir, "Zomboid"));
            }
        }
    }
}
