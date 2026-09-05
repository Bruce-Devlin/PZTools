using System.IO;
using System.Windows;
using PZTools.Core.Functions;
using PZTools.Core.Functions.Decompile;
using PZTools.Core.Functions.Steam;

namespace PZTools.Core.Windows.Dialogs
{
    /// <summary>
    /// Interaction logic for AppSetup.xaml
    /// </summary>
    public partial class AppSetup : Window
    {
        private CancellationTokenSource cancelSetupSource = new CancellationTokenSource();
        private Task<bool>? _setupTask;
        private bool _isFinishing;
        private bool _allowClose;
        private string? _stagingDirectory;
        private string? _managedGameStagingDirectory;
        private string? _partiallyInstalledAppDirectory;
        private string? _partiallyInstalledGameDirectory;
        private readonly string _originalWorkingDirectory = AppPaths.CurrentDirectoryPath;

        public AppSetup()
        {
            InitializeComponent();
            this.FreeDragThisWindow();
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
            string? path = WindowsHelpers.OpenFolderBrowser("Select installation folder for PZ Tools (this should be a new folder)");
            if (!string.IsNullOrEmpty(path))
            {
                if (Directory.GetFiles(path).Length > 0)
                {
                    MessageBox.Show("The selected folder is not empty. Please select an empty folder for installation.", "Invalid Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                txtAppInstallPath.Text = path;
                UpdateGameInstallOption();
            }
        }

        private void BrowseGameInstallButton_Click(object sender, RoutedEventArgs e)
        {
            string? path = WindowsHelpers.OpenFolderBrowser("Select existing Project Zomboid installation");
            if (!string.IsNullOrEmpty(path))
            {
                txtExistingGamePath.Text = path;
            }
        }

        private void BrowseManagedGamePath_Click(object sender, RoutedEventArgs e)
        {
            string? path = WindowsHelpers.OpenFolderBrowser("Select folder for PZ Tools managed installations");
            if (!string.IsNullOrEmpty(path))
            {
                txtManagedGamePath.Text = path;
            }
        }
        #endregion

        #region Buttons
        private async void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                            "Are you sure you want to cancel the setup? The application will exit.",
                            "Cancel Setup",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            await RequestCancelAndCloseAsync();
        }

        private async void FinishButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isFinishing)
                return;

            if (string.IsNullOrWhiteSpace(txtAppInstallPath.Text) || !Directory.Exists(txtAppInstallPath.Text))
            {
                MessageBox.Show("Please select a valid installation folder for PZ Tools.", "Invalid Path",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (rdoExistingGame.IsChecked == true)
            {
                if (string.IsNullOrWhiteSpace(txtExistingGamePath.Text) || !Directory.Exists(txtExistingGamePath.Text))
                {
                    MessageBox.Show("Please select a valid Project Zomboid installation folder.", "Invalid Path",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            else if (rdoManagedGame.IsChecked == true)
            {
                if (string.IsNullOrWhiteSpace(txtManagedGamePath.Text))
                {
                    MessageBox.Show("Please select a valid folder for managed installations.", "Invalid Path",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            string finalInstallDir = Path.GetFullPath(txtAppInstallPath.Text.Trim());
            string? installParent = Path.GetDirectoryName(finalInstallDir);
            if (string.IsNullOrWhiteSpace(installParent))
            {
                MessageBox.Show("Please select a valid installation folder.", "Invalid Path",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (Directory.EnumerateFileSystemEntries(txtAppInstallPath.Text).Any())
            {
                MessageBox.Show("The PZTools installation folder must be empty.", "Folder Not Empty",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool managed = rdoManagedGame.IsChecked == true;
            string existingGameDir = txtExistingGamePath.Text;
            bool createDesktopShortcut = chkDesktopShortcut.IsChecked == true;
            bool createStartMenuShortcut = chkStartMenuShortcut.IsChecked == true;
            bool decompileGameFiles = chkDecompileGame.IsChecked == true;
            string steamUsername = string.Empty;

            if (managed)
            {
                var managedPath = Path.GetFullPath(txtManagedGamePath.Text.Trim());
                if (Directory.Exists(managedPath) && Directory.EnumerateFileSystemEntries(managedPath).Any())
                {
                    MessageBox.Show("The managed game folder must be empty for a new setup.", "Folder Not Empty",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (!File.Exists(Path.Combine(txtExistingGamePath.Text, "ProjectZomboid64.exe")))
                {
                    MessageBox.Show("The selected folder does not contain ProjectZomboid64.exe.", "Invalid Game Folder",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var dlg = new SteamLogin { Owner = this };
                if (dlg.ShowDialog() != true)
                {
                    MessageBox.Show("Steam username is required for managed installations. Setup will be cancelled.",
                        "Setup Cancelled", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                steamUsername = dlg.Username;
            }

            _isFinishing = true;
            Directory.CreateDirectory(installParent);
            string installDir = Path.Combine(installParent, $".PZTools-install-{Guid.NewGuid():N}");
            Directory.CreateDirectory(installDir);
            _stagingDirectory = installDir;
            AppPaths.SetCurrentDirectory(installDir);

            string managedGameInstallDir = Path.Combine(installDir, "Zomboid");
            if (managed)
            {
                var finalManagedDir = Path.GetFullPath(txtManagedGamePath.Text.Trim());
                var relativeManagedDir = Path.GetRelativePath(finalInstallDir, finalManagedDir);
                var isInsideInstall = relativeManagedDir != ".." &&
                    !relativeManagedDir.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                    !Path.IsPathRooted(relativeManagedDir);

                if (isInsideInstall)
                {
                    managedGameInstallDir = Path.Combine(installDir, relativeManagedDir);
                }
                else
                {
                    var managedParent = Path.GetDirectoryName(finalManagedDir)
                        ?? throw new InvalidOperationException("The managed game path is invalid.");
                    Directory.CreateDirectory(managedParent);
                    managedGameInstallDir = Path.Combine(managedParent, $".PZTools-games-{Guid.NewGuid():N}");
                    Directory.CreateDirectory(managedGameInstallDir);
                    _managedGameStagingDirectory = managedGameInstallDir;
                }
            }

            ShowSetupOverlay("Starting setup...", false);

            _setupTask = StartSetupTasks(
                installDir, managedGameInstallDir, managed, existingGameDir,
                decompileGameFiles, steamUsername,
                cancelSetupSource.Token);

            bool installResult;
            try
            {
                installResult = await _setupTask;
            }
            catch (OperationCanceledException)
            {
                installResult = false;
            }
            catch (Exception ex)
            {
                await Console.Log("Setup failed: " + ex.Message, Console.LogLevel.Warning);
                installResult = false;
            }

            if (!installResult)
            {
                AppPaths.SetCurrentDirectory(_originalWorkingDirectory);
                HideSetupOverlay();
                MessageBox.Show(
                    "Setup could not be completed. The partial installation has been removed; review PZTools.log and try again.",
                    "Setup Incomplete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                CleanupFailedInstall();
                _isFinishing = false;
                return;
            }

            try
            {
                AppPaths.SetCurrentDirectory(_originalWorkingDirectory);
                WindowsHelpers.MoveDirectorySmart(installDir, finalInstallDir);
                _stagingDirectory = null;
                _partiallyInstalledAppDirectory = finalInstallDir;
                if (_managedGameStagingDirectory != null)
                {
                    var finalManagedDirectory = Path.GetFullPath(txtManagedGamePath.Text.Trim());
                    WindowsHelpers.MoveDirectorySmart(_managedGameStagingDirectory, finalManagedDirectory);
                    _managedGameStagingDirectory = null;
                    _partiallyInstalledGameDirectory = finalManagedDirectory;
                }
                AppPaths.SetCurrentDirectory(finalInstallDir);
                SaveSettings();
                CreateRequestedShortcuts(finalInstallDir, createDesktopShortcut, createStartMenuShortcut);
                _partiallyInstalledAppDirectory = null;
                _partiallyInstalledGameDirectory = null;
                HideSetupOverlay();
                _allowClose = true;
                DialogResult = true;
            }
            catch (Exception ex)
            {
                AppPaths.SetCurrentDirectory(_originalWorkingDirectory);
                HideSetupOverlay();
                CleanupFailedInstall();
                _isFinishing = false;
                await Console.Log($"Could not finalise setup: {ex.Message}", Console.LogLevel.Error);
            }
        }
        #endregion

        private void SaveSettings()
        {
            Functions.Config.SetAppSetting("AppInstallPath", txtAppInstallPath.Text);
            Functions.Config.SetAppSetting("GameMode", rdoExistingGame.IsChecked == true ? "Existing" : "Managed");

            if (rdoExistingGame.IsChecked == true)
                Functions.Config.SetAppSetting("ExistingGamePath", txtExistingGamePath.Text);
            else
                Functions.Config.SetAppSetting("ManagedGamePath", txtManagedGamePath.Text);
        }

        private async Task<bool> StartSetupTasks(
            string installDir,
            string managedGameInstallDir,
            bool managed,
            string existingGameDir,
            bool decompileGameFiles,
            string steamUsername,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            UpdateSetupStatus("Setting up application files...", 5);
            CopyApplicationFiles(AppContext.BaseDirectory, installDir, ct);
            Directory.CreateDirectory(Path.Combine(installDir, "Configs"));
            Directory.CreateDirectory(Path.Combine(installDir, "Projects"));

            string zomboidRoot = managed ? managedGameInstallDir : Path.Combine(installDir, "Zomboid");
            if (managed || decompileGameFiles)
                Directory.CreateDirectory(zomboidRoot);

            ct.ThrowIfCancellationRequested();

            if (managed)
            {
                UpdateSetupStatus("Setting up managed Project Zomboid installations...", 20);
                string steamCmdDir = Path.Combine(installDir, "SteamCMD");

                var steamInstaller = new SteamInstaller();
                steamInstaller.SteamMessage += (_, msg) =>
                {
                    Dispatcher.Invoke(() => UpdateSetupStatus(msg, 30));
                };

                await steamInstaller.SetupSteamCmdAsync(steamCmdDir, ct);

                var zomboidBranches = new Dictionary<string, string>
                {
                    { "42.13.1", "42.13.1" },
                    { "legacy_42_12", "legacy_42_12" },
                    { "public", "public" }
                };

                UpdateSetupStatus("Installing Project Zomboid versions...", 35);

                foreach (var branch in zomboidBranches)
                {
                    ct.ThrowIfCancellationRequested();

                    UpdateSetupStatus(
                        $"Installing Project Zomboid {branch.Key}...\r\n" +
                        "This will be done by SteamCMD, it will ask for your password.\r\n" +
                        "(PZTools won't save this).\r\n\r\n" +
                        "Progress can be tracked within the opened console window.",
                        40);

                    string installDirVersion = Path.Combine(zomboidRoot, branch.Key);

                    var ok = await steamInstaller.InstallAppAsync(
                        appId: "108600",
                        installDirectory: installDirVersion,
                        username: steamUsername,
                        beta: branch.Value,
                        cancellationToken: ct);

                    await Console.Log($"Finished installing: [{branch.Key}] (result={ok})");

                    if (!ok)
                        return false;
                }

                await Console.Log("All Project Zomboid versions installed.");
            }

            if (decompileGameFiles)
            {
                ct.ThrowIfCancellationRequested();

                UpdateSetupStatus("Checking CFR decompiler...", 60);

                string? javaExe = JavaDecompiler.FindJavaExecutable();
                if (javaExe == null)
                {
                    await Console.Log("Java not found on system!", Console.LogLevel.Error);
                    return false;
                }

                UpdateSetupStatus("Decompiling source files... (this can take a while)", 65);

                JavaDecompilerHelpers.OnDecompilerMessage += (_, status) => UpdateSetupStatus(status);

                try
                {
                    if (managed)
                    {
                        foreach (var dir in Directory.GetDirectories(zomboidRoot))
                        {
                            ct.ThrowIfCancellationRequested();

                            string existingJarPath = Path.Combine(dir, "projectzomboid.jar");
                            if (File.Exists(existingJarPath))
                            {
                                if (!await JavaDecompilerHelpers.DecompileGame(dir, Path.GetFileName(dir), ct))
                                    return false;
                            }
                        }
                    }
                    else
                    {
                        if (!await JavaDecompilerHelpers.DecompileGame(existingGameDir, cancellationToken: ct))
                            return false;
                    }
                }
                finally
                {
                    JavaDecompilerHelpers.ClearDecompilerMessageEvents();
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

        private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_allowClose)
                return;

            var result = MessageBox.Show(
                "Are you sure you want to exit and cancel the setup? The application will exit.",
                "Exit Setup",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.No)
            {
                e.Cancel = true;
                return;
            }

            e.Cancel = true;
            cancelSetupSource.Cancel();
            await RequestCancelAndCloseAsync();
        }

        private void CleanupFailedInstall()
        {
            var staging = _stagingDirectory;
            if (!string.IsNullOrWhiteSpace(staging) && Directory.Exists(staging))
            {
                var fullStaging = Path.GetFullPath(staging);
                var name = Path.GetFileName(fullStaging);
                if (!name.StartsWith(".PZTools-install-", StringComparison.Ordinal))
                    throw new InvalidOperationException("Refusing to remove an unexpected setup path.");

                WindowsHelpers.DeleteDirectoryRobust(fullStaging);
                _stagingDirectory = null;
            }

            if (_managedGameStagingDirectory != null && Directory.Exists(_managedGameStagingDirectory))
            {
                var managedStagingName = Path.GetFileName(_managedGameStagingDirectory);
                if (managedStagingName.StartsWith(".PZTools-games-", StringComparison.Ordinal))
                    WindowsHelpers.DeleteDirectoryRobust(_managedGameStagingDirectory);
                _managedGameStagingDirectory = null;
            }

            if (_partiallyInstalledAppDirectory != null && Directory.Exists(_partiallyInstalledAppDirectory))
                WindowsHelpers.DeleteDirectoryRobust(_partiallyInstalledAppDirectory);
            if (_partiallyInstalledGameDirectory != null && Directory.Exists(_partiallyInstalledGameDirectory))
                WindowsHelpers.DeleteDirectoryRobust(_partiallyInstalledGameDirectory);
            _partiallyInstalledAppDirectory = null;
            _partiallyInstalledGameDirectory = null;
        }

        private async Task RequestCancelAndCloseAsync()
        {
            if (!cancelSetupSource.IsCancellationRequested)
            {
                cancelSetupSource.Cancel();
                UpdateSetupStatus("Cancelling setup...", null);
            }

            await WaitSetupTaskOrTimeoutAsync(TimeSpan.FromSeconds(3));
            AppPaths.SetCurrentDirectory(_originalWorkingDirectory);
            CleanupFailedInstall();
            _allowClose = true;
            DialogResult = false;
        }

        private async Task WaitSetupTaskOrTimeoutAsync(TimeSpan timeout)
        {
            var t = _setupTask;
            if (t is null)
                return;

            try
            {
                var completed = await Task.WhenAny(t, Task.Delay(timeout));
                if (completed == t)
                    await t;
            }
            catch
            {
            }
        }

        private static void CopyApplicationFiles(string sourceDirectory, string destinationDirectory, CancellationToken ct)
        {
            var sourceRoot = Path.GetFullPath(sourceDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var destinationRoot = Path.GetFullPath(destinationDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            foreach (var directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                var fullDirectory = Path.GetFullPath(directory);
                if (fullDirectory.StartsWith(destinationRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    continue;
                Directory.CreateDirectory(Path.Combine(destinationRoot, Path.GetRelativePath(sourceRoot, fullDirectory)));
            }

            foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                var fullFile = Path.GetFullPath(file);
                if (fullFile.StartsWith(destinationRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    continue;
                File.Copy(fullFile, Path.Combine(destinationRoot, Path.GetRelativePath(sourceRoot, fullFile)), overwrite: true);
            }
        }

        private static void CreateRequestedShortcuts(string installDirectory, bool desktop, bool startMenu)
        {
            var executable = Path.Combine(installDirectory, "PZTools.exe");
            if (desktop)
            {
                WindowsHelpers.CreateShortcut(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "PZTools.lnk"),
                    executable, "Project Zomboid Tools");
            }

            if (startMenu)
            {
                WindowsHelpers.CreateShortcut(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "PZTools", "PZTools.lnk"),
                    executable, "Project Zomboid Tools");
            }
        }
    }
}
