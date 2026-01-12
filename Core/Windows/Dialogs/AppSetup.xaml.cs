using PZTools.Core.Functions;
using PZTools.Core.Functions.Decompile;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;

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
            if (result == MessageBoxResult.Yes)
            {
                DialogResult = false;
                this.Close();
            }
        }

        private async void FinishButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtAppInstallPath.Text) || !Directory.Exists(txtAppInstallPath.Text))
            {
                MessageBox.Show("Please select a valid installation folder for PZ Tools.", "Invalid Path", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

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

            AppPaths.SetCurrentDirectory(txtAppInstallPath.Text);

            string installDir = txtAppInstallPath.Text;
            bool managed = rdoManagedGame.IsChecked == true;
            string existingGameDir = txtExistingGamePath.Text;
            bool createDesktopShortcut = chkDesktopShortcut.IsChecked == true;
            bool createStartMenuShortcut = chkStartMenuShortcut.IsChecked == true;
            bool decompileGameFiles = chkDecompileGame.IsChecked == true;
            string steamUsername = string.Empty;

            if (managed)
            {
                var dlg = new SteamLogin { Owner = this };

                if (dlg.ShowDialog() != true)
                {
                    MessageBox.Show("Steam username is required for managed installations. Setup will be cancelled.", "Setup Cancelled", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                steamUsername = dlg.Username;
            }

            ShowSetupOverlay("Starting setup...", false);

            bool installResult = false;

            await Task.Run(async () =>
            {
                installResult = await StartSetupTasks(installDir, managed, existingGameDir, createDesktopShortcut, createStartMenuShortcut, decompileGameFiles, steamUsername);
            });

            if (installResult)
            {
                SaveSettings();

                HideSetupOverlay();
                this.DialogResult = true;
                this.Close();
            }
            else
            {
                MessageBox.Show("Setup encountered errors. Please check the logs for details.", "Setup Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                HideSetupOverlay();
            }
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

        private async Task<bool> StartSetupTasks(string installDir, bool managed, string existingGameDir, bool createDesktopShortcut, bool createStartMenuShortcut, bool decompileGameFiles, string steamUsername)
        {
            string destExe = Path.Combine(installDir, "PZTools.exe");

            UpdateSetupStatus("Setting up application files...", 5);
            File.Copy(AppPaths.CurrentFilePath, destExe, true);
            Directory.CreateDirectory(Path.Combine(installDir, "Configs"));

            string zomboidRoot = Path.Combine(installDir, "Zomboid");
            if (managed || decompileGameFiles) Directory.CreateDirectory(zomboidRoot);

            if (createDesktopShortcut)
            {
                UpdateSetupStatus("Creating shortcuts...", 10);

                string shortcutPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    "PZTools.lnk"
                );
                WindowsHelpers.CreateShortcut(shortcutPath, destExe, "Project Zomboid Tools");
                await Console.Log($"Desktop shortcut created at {shortcutPath}");
            }

            if (createStartMenuShortcut)
            {
                UpdateSetupStatus("Creating shortcuts...", 15);
                string startMenuDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                    "PZTools"
                );
                Directory.CreateDirectory(startMenuDir);
                string shortcutPath = Path.Combine(startMenuDir, "PZTools.lnk");
                WindowsHelpers.CreateShortcut(shortcutPath, destExe, "Project Zomboid Tools");
                await Console.Log($"Start menu shortcut created at {shortcutPath}");
            }

            if (managed)
            {
                UpdateSetupStatus("Setting up managed Project Zomboid installations...", 20);
                string steamCmdDir = Path.Combine(installDir, "SteamCMD");
                Directory.CreateDirectory(steamCmdDir);

                string steamCmdExe = Path.Combine(steamCmdDir, "steamcmd.exe");

                if (!File.Exists(steamCmdExe))
                {
                    UpdateSetupStatus("Downloading SteamCMD...", 25);
                    using (var client = new HttpClient())
                    {
                        var steamCmdZipUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip";
                        var zipBytes = await client.GetByteArrayAsync(steamCmdZipUrl);
                        var zipPath = Path.Combine(steamCmdDir, "steamcmd.zip");
                        await File.WriteAllBytesAsync(zipPath, zipBytes);

                        System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, steamCmdDir, true);
                        File.Delete(zipPath);

                        await Console.Log("SteamCMD downloaded and extracted.");

                    }
                }

                UpdateSetupStatus("Setting up SteamCMD...", 30);
                var setupSteamCMD = new ProcessStartInfo
                {
                    FileName = steamCmdExe,
                    WorkingDirectory = steamCmdDir,
                    UseShellExecute = true,
                    Arguments = "+quit"
                };

                using var setupProc = Process.Start(setupSteamCMD);
                setupProc.WaitForExit();


                var zomboidBranches = new Dictionary<string, string>
                {
                    { "42.13.1", "42.13.1" },
                    { "legacy_42_12", "legacy_42_12" },
                    { "public", "public" }
                };

                UpdateSetupStatus("Installing Project Zomboid versions...", 35);
                bool installFailed = false;

                foreach (var branch in zomboidBranches)
                {
                    UpdateSetupStatus($"Installing Project Zomboid {branch.Key}... \r\nThis will be done by SteamCMD, it will ask for your password.\r\n(PZTools won't save this).\r\n\r\nThis will take some time as this must download all the game files and workshop mods subscribed, progress can be tracked within the opened console window.", 40);
                    string branchName = branch.Key;
                    string betaName = branch.Value;

                    string installDirVersion = Path.Combine(zomboidRoot, branchName);
                    Directory.CreateDirectory(installDirVersion);

                    await Console.Log($"Installing Project Zomboid [{branchName}]...");

                    var installZomboid = new ProcessStartInfo
                    {
                        FileName = steamCmdExe,
                        WorkingDirectory = steamCmdDir,
                        UseShellExecute = true,
                        Arguments =
                            $"+login {steamUsername} " +
                            $"+force_install_dir \"{installDirVersion}\" " +
                            $"+app_update 108600 -beta {betaName} validate " +
                            $"+quit"
                    };

                    using var installZombProc = Process.Start(installZomboid);
                    installZombProc.WaitForExit();

                    bool result = File.Exists(Path.Combine(installDirVersion, "projectzomboid.jar"));

                    if (!result) installFailed = true;

                    await Console.Log($"Finished installing: [{branchName}] (result={result}");
                }

                if (installFailed)
                {
                    await Console.Log("One or more Project Zomboid versions failed to install. Please check the logs for details.", Console.LogLevel.Error);
                    MessageBox.Show("One or more Project Zomboid versions failed to install. Please check the logs for details.", "Installation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                await Console.Log("All Project Zomboid versions installed.");
            }

            if (decompileGameFiles)
            {
                UpdateSetupStatus("Checking CFR decompiler...", 60);

                string? javaExe = JavaDecompiler.FindJavaExecutable();
                if (javaExe == null)
                {
                    await Console.Log("Java not found on system!", Console.LogLevel.Error);
                    return false;
                }

                try
                {
                    await JavaDecompiler.EnsureCfrInstalledAsync();
                }
                catch (Exception ex)
                {
                    await Console.Log("Failed to install CFR: " + ex.Message, Console.LogLevel.Error);
                    return false;
                }

                UpdateSetupStatus("Decompiling source files... (this can take a while)", 65);
                string sourcePath = Path.Combine(zomboidRoot, "Source");
                Directory.CreateDirectory(sourcePath);

                if (managed)
                {
                    foreach (var dir in Directory.GetDirectories(zomboidRoot))
                    {
                        string existingJarPath = Path.Combine(dir, "projectzomboid.jar");
                        if (File.Exists(existingJarPath))
                        {
                            string versionDir = Path.Combine(sourcePath, Path.GetFileName(dir));
                            File.Copy(existingJarPath, Path.Combine(versionDir, $"projectzomboid.jar"), true);
                            await Console.Log($"Source files copied from managed installation: {dir}");

                            string managedJar = Path.Combine(versionDir, "projectzomboid.jar");
                            string cfrPath = JavaDecompiler.CfrJarPath;

                            await JavaDecompiler.DecompileJarAsync(
                                javaExe,
                                cfrPath,
                                managedJar,
                                versionDir,
                                onOutput: msg => UpdateSetupStatus($"Decompiler: {msg}"),
                                onError: msg => UpdateSetupStatus($"Decompiler: {msg}")
                            );

                            File.Delete(managedJar);

                            if (!Directory.Exists(Path.Combine(versionDir, "zombie")))
                            {
                                MessageBox.Show("Decompilation failed! Source files not found after decompilation.", "Decompilation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                                return false;
                            }
                        }
                    }
                }
                else
                {
                    string existingJarPath = Path.Combine(existingGameDir, "projectzomboid.jar");

                    File.Copy(existingJarPath, Path.Combine(sourcePath, "projectzomboid.jar"), true);
                    await Console.Log("Source files copied from existing installation.");

                    string managedJar = Path.Combine(sourcePath, "projectzomboid.jar");
                    string cfrPath = JavaDecompiler.CfrJarPath;

                    await JavaDecompiler.DecompileJarAsync(
                        javaExe,
                        cfrPath,
                        managedJar,
                        sourcePath,
                        onOutput: msg => UpdateSetupStatus($"Decompiler: {msg}"),
                        onError: msg => UpdateSetupStatus($"Decompiler: {msg}")
                    );

                    File.Delete(managedJar);

                    if (!Directory.Exists(Path.Combine(sourcePath, "zombie")))
                    {
                        MessageBox.Show("Decompilation failed! Source files not found after decompilation.", "Decompilation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return false;
                    }
                }
            }

            UpdateSetupStatus("Setup complete!", 100);
            return true;
        }

        private void ShowSetupOverlay(string status, bool indeterminate = true)
        {
            SetupOverlay.Visibility = Visibility.Visible;
            SetupStatusText.Text = status;

            SetupProgressBar.IsIndeterminate = indeterminate;
            SetupProgressBar.Value = 0;

            IsEnabled = false;
        }

        private void UpdateSetupStatus(string status, double? progress = null)
        {
            Dispatcher.Invoke(async () =>
            {
                SetupStatusText.Text = status;
                await Console.Log(status);

                if (progress.HasValue)
                {
                    SetupProgressBar.IsIndeterminate = false;
                    SetupProgressBar.Value = progress.Value;
                }
            });
        }

        private void HideSetupOverlay()
        {
            SetupOverlay.Visibility = Visibility.Collapsed;
            IsEnabled = true;
        }
    }
}
