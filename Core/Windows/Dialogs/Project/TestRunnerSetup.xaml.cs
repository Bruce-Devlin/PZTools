using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using PZTools.Core.Functions.Projects;
using PZTools.Core.Functions.Tester;
using PZTools.Core.Functions.Zomboid;
using PZTools.Core.Models;

namespace PZTools.Core.Windows.Dialogs.Project;

public partial class TestRunnerSetup : Window
{
    private readonly ModProject project;
    private readonly PlaytestProfile? basis;
    private PlaytestProfile profile = null!;
    private CancellationTokenSource? running;
    private bool loading = true;
    private bool closeRequested;
    private int revision;
    private string? artifactDirectory;
    public PlaytestProfile? SavedProfile { get; private set; }

    public TestRunnerSetup(ModProject project, PlaytestProfile? basis = null)
    {
        InitializeComponent(); this.project = project; this.basis = basis;
        var builds = project.Targets.Select(t => t.Build).Append(basis?.Build ?? ZomboidGame.latestStableBuild).Distinct().OrderDescending().ToArray();
        Builds.ItemsSource = builds;
        Builds.SelectedItem = basis?.Build ?? builds[0];
        if (basis?.Mode == PlaytestMode.DedicatedServer) Multiplayer.IsChecked = true;
        else SinglePlayer.IsChecked = true;
        loading = false;
        ResetProfile();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try { Saves.ItemsSource = await Task.Run(() => PlaytestSetupService.DiscoverSaves(ZomboidGame.GameUserDirectory)); }
        catch (Exception ex) { Progress.Text = "Save discovery: " + ex.Message; }
        await CheckAsync();
    }

    private void ResetProfile()
    {
        loading = true;
        profile = PlaytestSetupService.Create(project, Multiplayer.IsChecked == true ? PlaytestMode.DedicatedServer : PlaytestMode.SinglePlayer, basis);
        profile.Build = (double)Builds.SelectedItem;
        var mp = profile.Mode == PlaytestMode.DedicatedServer;
        CharacterTitle.Text = mp ? "2   Start your local multiplayer session" : "2   Give tests a character to start with";
        CharacterHelp.Text = mp
            ? "We prepare a private local server, two windowed clients and separate test accounts. Create a character in each window, enter the world, then quit both clients normally. We save the session as a reusable starting point."
            : "Choose an existing world, or create a dedicated test character once. Each test run opens a fresh copy, leaving the original save unchanged.";
        SavePanel.Visibility = mp ? Visibility.Collapsed : Visibility.Visible;
        NewWorld.Visibility = mp ? Visibility.Collapsed : Visibility.Visible;
        ServerOptions.Visibility = mp ? Visibility.Visible : Visibility.Collapsed;
        Capture.Content = mp ? "Launch server + two clients" : "Create a test character";
        CaptureHelp.Text = mp ? "No server configuration files or Steam account switching. Keep both characters alive and quit to desktop to finish capture."
            : "In the game: Solo > create a world and character > enter the world > quit to desktop. PZTools finds the saved world for you.";
        Connection.Text = $"Local machine only · 127.0.0.1:{profile.ServerPort} · PZTools1 and PZTools2 · automatic login";
        ServerPath.Text = "";
        Saves.SelectedItem = null; SavePath.Text = "No world selected. Use a detected save, browse, or create one below.";
        Progress.Text = "";
        loading = false;
    }

    private async void Mode_Changed(object sender, RoutedEventArgs e)
    {
        if (loading) return;
        ResetProfile(); await CheckAsync();
    }
    private async void Build_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (loading || profile is null) return;
        profile.Build = (double)Builds.SelectedItem; await CheckAsync();
    }
    private async void Save_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (loading || Saves.SelectedItem is not PlaytestSaveCandidate save) return;
        SelectSave(save.Path); await CheckAsync();
    }
    private void SelectSave(string path)
    {
        profile.SourceSavePath = path; profile.SaveMode = PlaytestSaveMode.FreshClone;
        SavePath.Text = path;
        Capture.Content = "Open a copy and verify character";
        CaptureHelp.Text = "PZTools opens a copy and asks the game to check it. If the character is dead or the build is incompatible, choose another world or create a new test character.";
    }
    private async void BrowseSave_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Select one saved world (inside Zomboid > Saves > game mode)", UseDescriptionForTitle = true };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) { SelectSave(dialog.SelectedPath); await CheckAsync(); }
    }
    private void BrowseServer_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Select the matching dedicated-server installation", UseDescriptionForTitle = true };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) ServerPath.Text = dialog.SelectedPath;
    }
    private async void NewWorld_Click(object sender, RoutedEventArgs e)
    {
        profile.SaveMode = PlaytestSaveMode.Empty; profile.SourceSavePath = "";
        Saves.SelectedItem = null; SavePath.Text = "A new world will be created in an isolated session.";
        Capture.Content = "Create a test character";
        CaptureHelp.Text = "In the game: Solo > create a world and character > enter the world > quit to desktop.";
        await CheckAsync();
    }
    private async void Server_Changed(object sender, TextChangedEventArgs e)
    {
        if (loading || profile is null) return;
        profile.ServerInstallPath = ServerPath.Text.Trim(); await CheckAsync();
    }
    private async void Recheck_Click(object sender, RoutedEventArgs e) => await CheckAsync();
    private async void Installation_Click(object sender, RoutedEventArgs e)
    {
        new AppOptions { Owner = this }.ShowDialog(); await CheckAsync();
    }
    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        try { if (artifactDirectory is not null) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(artifactDirectory) { UseShellExecute = true }); }
        catch (Exception ex) { Progress.Text = ex.Message; }
    }

    private async Task CheckAsync()
    {
        if (profile is null || running is not null) return;
        var version = ++revision;
        UseProfile.IsEnabled = Capture.IsEnabled = false;
        var snapshot = Newtonsoft.Json.JsonConvert.DeserializeObject<PlaytestProfile>(Newtonsoft.Json.JsonConvert.SerializeObject(profile))!;
        var gameRoot = PlaytestSetupService.ResolveGameRoot(snapshot.Build);
        GamePath.Text = string.IsNullOrWhiteSpace(gameRoot) ? "Game installation not configured" : gameRoot;
        try
        {
            var result = await Task.Run(() =>
            {
                ModDependencyService.SynchronizeRequiredDependencies(project, snapshot, ModDependencyService.Discover(snapshot.Build));
                var checks = PlaytestSetupService.Check(project, snapshot, gameRoot, automated: true);
                var launch = PlaytestSetupService.Check(project, snapshot, gameRoot, automated: false);
                return (checks, launch);
            });
            if (version != revision || running is not null) return;
            profile.Dependencies = snapshot.Dependencies;
            Checks.ItemsSource = result.checks.Select(c => c with { Title = (c.Blocking ? "Needs attention · " : "") + c.Title }).ToArray();
            Capture.IsEnabled = result.launch.All(c => !c.Blocking);
            UseProfile.IsEnabled = result.checks.All(c => !c.Blocking);
            UseProfile.Content = profile.Mode == PlaytestMode.DedicatedServer && profile.SaveMode == PlaytestSaveMode.Empty ? "Use without fixture" : "Use this profile";
        }
        catch (Exception ex) { if (version == revision) Progress.Text = ex.Message; }
    }

    private async void Capture_Click(object sender, RoutedEventArgs e)
    {
        if (running is not null) return;
        running = new CancellationTokenSource(); ++revision;
        SetupForm.IsEnabled = ModePanel.IsEnabled = Recheck.IsEnabled = UseProfile.IsEnabled = false;
        Cancel.Content = "Cancel setup";
        Progress.Text = profile.Mode == PlaytestMode.DedicatedServer
            ? "Starting your server and two clients. Create a character in each window, enter the world, then quit both clients to desktop."
            : "Starting your isolated game. Create a world and living character, enter the world, then quit to desktop.";
        Progress.BringIntoView();
        try
        {
            profile = await GameFixtureSetup.CaptureAsync(project, profile, PlaytestSetupService.ResolveGameRoot(profile.Build),
                line => Dispatcher.BeginInvoke(() => { Progress.Text = line; Progress.BringIntoView(); }), running.Token,
                path => { artifactDirectory = path; OpenLogs.Visibility = Visibility.Visible; });
            if (profile.Mode == PlaytestMode.SinglePlayer) SavePath.Text = profile.SourceSavePath;
            Capture.Content = "Capture another fixture";
            CaptureHelp.Text = "Your captured fixture is kept in this project's isolated data. Use this profile to finish setup.";
        }
        catch (OperationCanceledException) { Progress.Text = "Setup cancelled. No fixture was saved to your profiles. Existing saves and profiles are unchanged."; }
        catch (Exception ex) { Progress.Text = ex.Message; }
        finally
        {
            running.Dispose(); running = null;
            SetupForm.IsEnabled = ModePanel.IsEnabled = Recheck.IsEnabled = true;
            Cancel.Content = "Close";
            Cancel.IsEnabled = true;
            if (closeRequested) Close(); else { await CheckAsync(); Activate(); }
        }
    }

    private async void UseProfile_Click(object sender, RoutedEventArgs e)
    {
        await CheckAsync();
        if (!UseProfile.IsEnabled) return;
        try
        {
            var profiles = PlaytestProfileStore.Load(project);
            var name = profile.Name; var suffix = 2;
            while (profiles.Any(p => p.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase))) profile.Name = name + " " + suffix++;
            PlaytestProfileStore.Add(project, profile);
            SavedProfile = profile; DialogResult = true;
        }
        catch (Exception ex) { Progress.Text = "Could not save profile: " + ex.Message; }
    }
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (running is null) Close(); else { running.Cancel(); Cancel.IsEnabled = false; Progress.Text = "Stopping setup processes…"; }
    }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (running is null) return;
        e.Cancel = true; closeRequested = true; running.Cancel();
    }
}
