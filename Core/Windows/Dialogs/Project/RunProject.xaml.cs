using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using PZTools.Core.Functions;
using PZTools.Core.Functions.Projects;
using PZTools.Core.Functions.Zomboid;
using PZTools.Core.Models;

namespace PZTools.Core.Windows.Dialogs.Project
{
    public partial class RunProject : Window
    {
        private readonly bool _autoRun;
        private readonly ModProject? _project;
        private readonly List<PlaytestProfile> _profiles = new();
        private PlaytestSessionRunner? _runner;
        private CancellationTokenSource? _runCts;
        private bool _loading;
        private bool _allowClose;
        private readonly ObservableCollection<PlaytestDependency> _dependencyRows = new();
        private List<DiscoveredModDependency> _discoveredDependencies = new();

        public RunProject(bool showWindow = true)
        {
            InitializeComponent();
            _autoRun = !showWindow;
            _project = ProjectEngine.CurrentProject;
            cmbMode.ItemsSource = Enum.GetValues<PlaytestMode>();
            cmbSaveMode.ItemsSource = Enum.GetValues<PlaytestSaveMode>();
            cmbWindowMode.ItemsSource = Enum.GetValues<PlaytestWindowMode>();
            LoadBuilds();
            DependencyGrid.ItemsSource = _dependencyRows;
            LoadProfiles();
            chkShowEveryRun.IsChecked = bool.TryParse(Config.GetVariable(VariableType.user, "showRunWindowBeforeLaunch"), out var showEachTime) && showEachTime;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await DiscoverDependenciesAsync();
            if (_autoRun && _project is not null)
                await RunSelectedProfileAsync();
        }

        private void LoadProfiles()
        {
            if (_project is null)
            {
                StatusText.Text = "Open a project to use Playtest Profiles.";
                btnLaunch.IsEnabled = false;
                return;
            }
            try
            {
                _profiles.AddRange(PlaytestProfileStore.Load(_project));
                cmbProfiles.ItemsSource = _profiles;
                var saved = Config.GetVariable(VariableType.user, $"playtest-profile-{_project.ModInfo.Id}");
                cmbProfiles.SelectedItem = Guid.TryParse(saved, out var id) ? _profiles.FirstOrDefault(x => x.Id == id) : _profiles[0];
                if (cmbProfiles.SelectedItem is null)
                    cmbProfiles.SelectedIndex = 0;
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Playtest Profiles", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void LoadBuilds()
        {
            if (ZomboidGame.GameMode.Equals("Managed", StringComparison.OrdinalIgnoreCase) && Directory.Exists(ZomboidGame.GameDirectory))
            {
                foreach (var directory in Directory.GetDirectories(ZomboidGame.GameDirectory))
                    if (double.TryParse(Path.GetFileName(directory), NumberStyles.Float, CultureInfo.InvariantCulture, out var build))
                        cmbBuilds.Items.Add(build);
            }
            else
                cmbBuilds.Items.Add(ZomboidGame.latestStableBuild);
        }

        private void Profile_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbProfiles.SelectedItem is PlaytestProfile profile)
                LoadProfile(profile);
        }

        private void LoadProfile(PlaytestProfile profile)
        {
            _loading = true;
            txtProfileName.Text = profile.Name;
            cmbMode.SelectedItem = profile.Mode;
            SelectBuild(profile.Build);
            txtLaunchArgs.Text = profile.LaunchArguments;
            cmbWindowMode.SelectedItem = profile.WindowMode;
            txtWindowWidth.Text = profile.WindowWidth.ToString(CultureInfo.InvariantCulture);
            txtWindowHeight.Text = profile.WindowHeight.ToString(CultureInfo.InvariantCulture);
            cmbSaveMode.SelectedItem = profile.SaveMode;
            txtSourceSave.Text = profile.SourceSavePath;
            chkKeepSession.IsChecked = profile.KeepSessionData;
            _dependencyRows.Clear();
            foreach (var dependency in profile.Dependencies)
                _dependencyRows.Add(dependency);
            txtServerInstall.Text = profile.ServerInstallPath;
            txtServerName.Text = profile.ServerName;
            txtServerPort.Text = profile.ServerPort.ToString(CultureInfo.InvariantCulture);
            txtMaxPlayers.Text = profile.MaxPlayers.ToString(CultureInfo.InvariantCulture);
            txtClientCount.Text = profile.ClientCount.ToString(CultureInfo.InvariantCulture);
            txtServerTimeout.Text = profile.ServerStartupTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
            chkNoSteam.IsChecked = profile.NoSteam;
            chkAutoConnect.IsChecked = profile.AutoConnectClients;
            txtServerOptions.Text = profile.AdditionalServerOptions;
            _loading = false;
            UpdateModeState();
            UpdateSaveState();
        }

        private void ReadProfile(PlaytestProfile profile)
        {
            profile.Name = txtProfileName.Text.Trim();
            profile.Mode = cmbMode.SelectedItem is PlaytestMode mode ? mode : PlaytestMode.SinglePlayer;
            profile.Build = cmbBuilds.SelectedItem is double build ? build : ZomboidGame.latestStableBuild;
            profile.LaunchArguments = txtLaunchArgs.Text.Trim();
            profile.WindowMode = cmbWindowMode.SelectedItem is PlaytestWindowMode windowMode ? windowMode : PlaytestWindowMode.Windowed;
            profile.WindowWidth = ParseInt(txtWindowWidth.Text, 1280);
            profile.WindowHeight = ParseInt(txtWindowHeight.Text, 720);
            profile.SaveMode = cmbSaveMode.SelectedItem is PlaytestSaveMode save ? save : PlaytestSaveMode.Empty;
            profile.SourceSavePath = txtSourceSave.Text.Trim();
            profile.KeepSessionData = chkKeepSession.IsChecked == true;
            DependencyGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            DependencyGrid.CommitEdit(DataGridEditingUnit.Row, true);
            profile.Dependencies = _dependencyRows.ToList();
            if (_project is not null)
                ModDependencyService.SynchronizeRequiredDependencies(_project, profile, _discoveredDependencies);
            ReloadDependencyRows(profile);
            profile.ServerInstallPath = txtServerInstall.Text.Trim();
            profile.ServerName = txtServerName.Text.Trim().Replace(' ', '_');
            profile.ServerPort = ParseInt(txtServerPort.Text, 16261);
            profile.MaxPlayers = ParseInt(txtMaxPlayers.Text, 8);
            profile.ClientCount = ParseInt(txtClientCount.Text, 1);
            profile.ServerStartupTimeoutSeconds = ParseInt(txtServerTimeout.Text, 90);
            profile.NoSteam = chkNoSteam.IsChecked == true;
            profile.AutoConnectClients = chkAutoConnect.IsChecked == true;
            profile.AdditionalServerOptions = txtServerOptions.Text;
        }

        private async Task RunSelectedProfileAsync()
        {
            if (_project is null || cmbProfiles.SelectedItem is not PlaytestProfile profile)
                return;
            ReadProfile(profile);
            var errors = PlaytestProfileStore.Validate(profile, _project);
            if (errors.Count > 0)
            {
                MessageBox.Show(string.Join(Environment.NewLine, errors), "Invalid playtest profile", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var clientRoot = ResolveClientBuildRoot(profile.Build);
            var executable = Path.Combine(clientRoot, "ProjectZomboid64.exe");
            if (!Directory.Exists(clientRoot) || !File.Exists(executable))
            {
                var configured = string.IsNullOrWhiteSpace(clientRoot) ? "(not configured)" : clientRoot;
                MessageBox.Show(
                    $"PZTools could not find a valid Project Zomboid executable.\n\nExpected:\n{executable}\n\nResolved game folder:\n{configured}\n\nOpen File > App Options > System and select the folder containing ProjectZomboid64.exe, then try again.",
                    "Project Zomboid executable not found", MessageBoxButton.OK, MessageBoxImage.Warning);
                StatusText.Text = "Select a valid Project Zomboid installation in App Options";
                return;
            }
            var health = await ProjectHealthService.AnalyzeAsync(_project, validateLua: true);
            if (!health.IsReadyToDeploy)
            {
                MessageBox.Show($"Playtest is blocked by {health.ErrorCount} project/conflict error(s).", "Playtest preflight", MessageBoxButton.OK, MessageBoxImage.Warning);
                new ProjectDashboard(_project) { Owner = this }.ShowDialog();
                return;
            }

            PlaytestProfileStore.Save(_project, _profiles);
            Config.StoreVariable(VariableType.user, $"playtest-profile-{_project.ModInfo.Id}", profile.Id.ToString());
            txtOutputLog.Clear();
            btnLaunch.IsEnabled = false;
            btnStopGame.IsEnabled = true;
            StatusText.Text = "Preparing session...";
            _runCts = new CancellationTokenSource();
            _runner = new PlaytestSessionRunner();
            _runner.Output += Runner_Output;
            try
            {
                var result = await _runner.RunAsync(_project, profile, clientRoot, _runCts.Token);
                StatusText.Text = result.Diagnostics.Any(x => x.Severity == DiagnosticSeverity.Error)
                    ? $"Finished with {result.Diagnostics.Count} finding(s)"
                    : result.WasStopped ? "Session stopped" : "Session completed";
                foreach (var diagnostic in result.Diagnostics)
                    AppendOutput($"[{diagnostic.Severity}] {diagnostic.Code}: {diagnostic.Message}");
                if (result.Diagnostics.Count == 0)
                    AppendOutput("Session completed without project-related diagnostics.");
            }
            catch (OperationCanceledException) { StatusText.Text = "Session cancelled"; }
            catch (Exception ex)
            {
                StatusText.Text = "Session failed";
                AppendOutput("[ERROR] " + ex.Message);
                MessageBox.Show(ex.Message, "Playtest session failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (_runner is not null)
                    _runner.Output -= Runner_Output;
                _runner?.Dispose();
                _runner = null;
                _runCts?.Dispose();
                _runCts = null;
                btnLaunch.IsEnabled = true;
                btnStopGame.IsEnabled = false;
            }
        }

        private string ResolveClientBuildRoot(double build)
        {
            if (!ZomboidGame.GameMode.Equals("Managed", StringComparison.OrdinalIgnoreCase))
                return ZomboidGame.GameDirectory;
            var exact = Path.Combine(ZomboidGame.GameDirectory, build.ToString("0.################", CultureInfo.InvariantCulture));
            if (Directory.Exists(exact))
                return exact;
            if (!Directory.Exists(ZomboidGame.GameDirectory))
                return exact;
            return Directory.GetDirectories(ZomboidGame.GameDirectory)
                .FirstOrDefault(x => double.TryParse(Path.GetFileName(x), NumberStyles.Float, CultureInfo.InvariantCulture, out var found) && Math.Truncate(found) == Math.Truncate(build)) ?? exact;
        }

        private void Runner_Output(object? sender, string message) => Dispatcher.BeginInvoke(() => AppendOutput(message));
        private void AppendOutput(string message)
        {
            txtOutputLog.AppendText(message + Environment.NewLine);
            txtOutputLog.ScrollToEnd();
        }

        private void NewProfile_Click(object sender, RoutedEventArgs e)
        {
            if (_project is null)
                return;
            var profile = PlaytestProfileStore.CreateDefault(_project);
            profile.Name = "New playtest " + (_profiles.Count + 1);
            _profiles.Add(profile);
            cmbProfiles.Items.Refresh();
            cmbProfiles.SelectedItem = profile;
        }

        private void DeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            if (_project is null || cmbProfiles.SelectedItem is not PlaytestProfile profile || _profiles.Count <= 1)
                return;
            if (MessageBox.Show($"Delete profile '{profile.Name}'?", "Playtest Profiles", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            var index = _profiles.IndexOf(profile);
            _profiles.Remove(profile);
            cmbProfiles.Items.Refresh();
            cmbProfiles.SelectedIndex = Math.Min(index, _profiles.Count - 1);
            PlaytestProfileStore.Save(_project, _profiles);
        }

        private void SaveProfile_Click(object sender, RoutedEventArgs e)
        {
            if (_project is null || cmbProfiles.SelectedItem is not PlaytestProfile profile)
                return;
            try
            {
                ReadProfile(profile);
                PlaytestProfileStore.Save(_project, _profiles);
                cmbProfiles.Items.Refresh();
                StatusText.Text = "Profile saved";
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Could not save profile", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private async void BtnLaunch_Click(object sender, RoutedEventArgs e) => await RunSelectedProfileAsync();
        private async void btnStopGame_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "Stopping session...";
            _runCts?.Cancel();
            if (_runner is not null)
                await _runner.StopAsync();
        }
        private void BrowseSave_Click(object sender, RoutedEventArgs e) => BrowseFolder(txtSourceSave, "Select a Project Zomboid save folder");
        private void BrowseServer_Click(object sender, RoutedEventArgs e) => BrowseFolder(txtServerInstall, "Select the dedicated server installation folder");

        private static void BrowseFolder(System.Windows.Controls.TextBox target, string description)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = description, UseDescriptionForTitle = true };
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                target.Text = dialog.SelectedPath;
        }

        private void Mode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_loading)
                UpdateModeState();
        }
        private void SaveMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_loading)
                UpdateSaveState();
        }
        private async void Build_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded && !_loading)
                await DiscoverDependenciesAsync();
        }
        private void ShowEveryRun_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized)
                return;
            Config.StoreVariable(VariableType.user, "showRunWindowBeforeLaunch", chkShowEveryRun.IsChecked == true);
        }
        private void UpdateModeState() => ServerPanel.IsEnabled = cmbMode.SelectedItem is PlaytestMode.DedicatedServer;
        private void UpdateSaveState() => txtSourceSave.IsEnabled = cmbSaveMode.SelectedItem is PlaytestSaveMode.FreshClone;

        private void SelectBuild(double build)
        {
            var found = cmbBuilds.Items.Cast<double>().FirstOrDefault(x => Math.Abs(x - build) < 0.0001);
            if (!cmbBuilds.Items.Cast<double>().Any(x => Math.Abs(x - build) < 0.0001))
            {
                cmbBuilds.Items.Add(build);
                found = build;
            }
            cmbBuilds.SelectedItem = found;
        }

        private static int ParseInt(string value, int fallback) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : fallback;

        private async Task DiscoverDependenciesAsync()
        {
            if (_project is null)
                return;
            StatusText.Text = "Discovering installed dependencies...";
            try
            {
                var build = cmbBuilds.SelectedItem is double selected ? selected : _project.Targets.FirstOrDefault(x => x.IsPrimary)?.Build ?? 42;
                _discoveredDependencies = (await Task.Run(() => ModDependencyService.Discover(build))).Where(x =>
                    !x.ModId.Equals(_project.ModInfo.Id, StringComparison.OrdinalIgnoreCase)).ToList();
                cmbDiscoveredDependencies.ItemsSource = _discoveredDependencies;
                cmbDiscoveredDependencies.SelectedIndex = _discoveredDependencies.Count > 0 ? 0 : -1;
                if (cmbProfiles.SelectedItem is PlaytestProfile profile)
                {
                    ModDependencyService.SynchronizeRequiredDependencies(_project, profile, _discoveredDependencies);
                    ReloadDependencyRows(profile);
                }
                StatusText.Text = $"Ready - {_discoveredDependencies.Count} installed mod definition(s) found";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Dependency discovery failed";
                AppendOutput("[WARNING] Dependency discovery failed: " + ex.Message);
            }
        }

        private void ReloadDependencyRows(PlaytestProfile profile)
        {
            _dependencyRows.Clear();
            foreach (var dependency in profile.Dependencies)
                _dependencyRows.Add(dependency);
        }

        private void AddDiscoveredDependency_Click(object sender, RoutedEventArgs e)
        {
            if (cmbDiscoveredDependencies.SelectedItem is not DiscoveredModDependency selected)
                return;
            var existing = _dependencyRows.FirstOrDefault(x => x.ModId.Equals(selected.ModId, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                existing = new PlaytestDependency
                {
                    ModId = selected.ModId,
                    WorkshopId = selected.WorkshopId,
                    SourcePath = selected.SourcePath,
                    Enabled = true
                };
                _dependencyRows.Add(existing);
            }
            else
            {
                existing.Enabled = true;
                if (string.IsNullOrWhiteSpace(existing.WorkshopId)) existing.WorkshopId = selected.WorkshopId;
                if (string.IsNullOrWhiteSpace(existing.SourcePath)) existing.SourcePath = selected.SourcePath;
                DependencyGrid.Items.Refresh();
            }
            DependencyGrid.SelectedItem = existing;
            DependencyGrid.ScrollIntoView(existing);
        }

        private void SyncRequiredDependencies_Click(object sender, RoutedEventArgs e)
        {
            if (_project is null || cmbProfiles.SelectedItem is not PlaytestProfile profile)
                return;
            profile.Dependencies = _dependencyRows.ToList();
            ModDependencyService.SynchronizeRequiredDependencies(_project, profile, _discoveredDependencies);
            ReloadDependencyRows(profile);
            StatusText.Text = "Required dependencies synchronized with Project Settings";
        }

        private void BrowseDependencySource_Click(object sender, RoutedEventArgs e)
        {
            if (DependencyGrid.SelectedItem is not PlaytestDependency dependency)
                return;
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = $"Select the loadable root for {dependency.ModId}",
                UseDescriptionForTitle = true
            };
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                dependency.SourcePath = dialog.SelectedPath;
                DependencyGrid.Items.Refresh();
            }
        }

        private void RemoveDependency_Click(object sender, RoutedEventArgs e)
        {
            if (DependencyGrid.SelectedItem is not PlaytestDependency dependency)
                return;
            if (dependency.IsProjectRequired)
            {
                MessageBox.Show("This dependency is required by Project Settings. Remove that requirement there before removing it from a playtest profile.",
                    "Required dependency", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            _dependencyRows.Remove(dependency);
        }

        private void MoveDependencyUp_Click(object sender, RoutedEventArgs e) => MoveDependency(-1);
        private void MoveDependencyDown_Click(object sender, RoutedEventArgs e) => MoveDependency(1);

        private void MoveDependency(int offset)
        {
            if (DependencyGrid.SelectedItem is not PlaytestDependency dependency)
                return;
            var oldIndex = _dependencyRows.IndexOf(dependency);
            var newIndex = oldIndex + offset;
            if (oldIndex < 0 || newIndex < 0 || newIndex >= _dependencyRows.Count)
                return;
            _dependencyRows.Move(oldIndex, newIndex);
            DependencyGrid.SelectedItem = dependency;
        }

        private async void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            if (_runner is not null)
                await _runner.StopAsync();
            _allowClose = true;
            Close();
        }
        private void Window_Closing(object? sender, CancelEventArgs e)
        {
            if (!_allowClose && _runner is not null)
            {
                MessageBox.Show("Stop the active playtest session before closing the Playtest Lab.", "Playtest Lab", MessageBoxButton.OK, MessageBoxImage.Information);
                e.Cancel = true;
            }
        }
        private void Window_Closed(object? sender, EventArgs e)
        {
            _runCts?.Cancel();
            _runner?.Dispose();
        }
    }
}
