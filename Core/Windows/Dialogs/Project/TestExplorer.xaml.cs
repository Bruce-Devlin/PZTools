using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using PZTools.Core.Functions.Projects;
using PZTools.Core.Functions.Tester;
using PZTools.Core.Functions.Zomboid;
using PZTools.Core.Models;
using PZTools.Core.Models.Test;

namespace PZTools.Core.Windows.Dialogs.Project;

public partial class TestExplorer : Window
{
    public Action<Process>? DockClient { get; init; }
    private readonly ModProject project;
    private CancellationTokenSource? running;
    private ModTestReport? report;
    private bool closing;
    private bool preflighting;
    private RunSession? selectedSession;
    private sealed record TestFile(string Path, string Display);
    public TestExplorer(ModProject project)
    {
        InitializeComponent(); this.project = project; RefreshFiles(); RefreshHistory();
    }
    private void RefreshHistory()
    {
        try { History.ItemsSource = SessionRegistry.Default.List(project.RootPath); }
        catch (Exception ex) { Status.Text = ex.Message; }
    }
    private void HistoryRefresh_Click(object sender, RoutedEventArgs e) => RefreshHistory();
    private void Restart_Click(object sender, RoutedEventArgs e)
    {
        if (running is not null || selectedSession is null) return;
        try
        {
            SessionLifecycleService.RestartProfile(selectedSession);
            new RunProject(false) { Owner = this, DockClient = DockClient, RestartSession = selectedSession }.Show();
        }
        catch (Exception ex) { Status.Text = ex.Message; }
    }
    private void History_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (running is not null || History.SelectedItem is not RunSession session) return;
        selectedSession = session;
        try
        {
            var path = Path.Combine(session.ArtifactDirectory, "results.json");
            report = File.Exists(path) ? Newtonsoft.Json.JsonConvert.DeserializeObject<ModTestReport>(File.ReadAllText(path)) : null;
            if (report is not null && session.State == "interrupted") report.OperationState = "interrupted; no actions replayed";
            Results.ItemsSource = report?.Tests;
            Details.Text = report is null ? Newtonsoft.Json.JsonConvert.SerializeObject(session, Newtonsoft.Json.Formatting.Indented) : TestEvidenceService.Summary(report);
            EvidenceTabs.SelectedIndex = 1;
            HistoryPanel.IsExpanded = false;
            EndpointStatus.Text = $"{session.State} · {session.Retention} · {session.WorkspacePath}\n" +
                string.Join("\n", session.Processes.Select(p => $"{p.Endpoint}: PID {p.Id}; saves: {(p.SavePaths.Count > 0 ? string.Join(", ", p.SavePaths) : Path.Combine(p.CachePath, "Saves"))}; log: {p.LogPath}"));
            Artifacts.IsEnabled = Directory.Exists(session.ArtifactDirectory);
            Status.Text = session.Display;
        }
        catch (Exception ex) { Status.Text = ex.Message; }
    }
    private void Evidence_Click(object sender, RoutedEventArgs e)
    {
        try { if (report is not null) { Details.Text = TestEvidenceService.Summary(report); EvidenceTabs.SelectedIndex = 1; } }
        catch (Exception ex) { Status.Text = ex.Message; }
    }
    private void Capture_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Results.SelectedItem is ModTestCase { Diagnostic.Screenshot: { } path } && File.Exists(path))
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            else Status.Text = "No screenshot was available for this case. Select it to see capture details.";
        }
        catch (Exception ex) { Status.Text = ex.Message; }
    }
    private void RefreshFiles()
    {
        try
        {
            Files.ItemsSource = ModTestService.Discover(project, "unit").Concat(ModTestService.Discover(project, "game"))
                .Select(p => new TestFile(p, Path.GetRelativePath(Path.Combine(project.RootPath, ModTestService.TestFolder), p))).ToArray();
            var selected = (Profiles.SelectedItem as PlaytestProfile)?.Id;
            var profiles = PlaytestProfileStore.Load(project);
            Profiles.ItemsSource = profiles; Profiles.SelectedItem = profiles.FirstOrDefault(p => p.Id == selected) ?? profiles[0];
        }
        catch (Exception ex) { Status.Text = ex.Message; }
    }
    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshFiles();
    private async void NativeCompile_Click(object sender, RoutedEventArgs e)
    {
        if (running is not null || preflighting) return;
        preflighting = true;
        try
        {
            var profile = Profiles.SelectedItem as PlaytestProfile ?? throw new InvalidOperationException("Choose an installation profile.");
            Status.Text = "Compiling saved Lua with the installed Kahlua runtime...";
            var files = TestEvidenceService.SourceFiles(project.RootPath).Where(p => p.EndsWith(".lua", StringComparison.OrdinalIgnoreCase));
            var results = await NativeLuaValidator.CompileAsync(PlaytestSetupService.ResolveGameRoot(profile.Build), files);
            Details.Text = Newtonsoft.Json.JsonConvert.SerializeObject(results, Newtonsoft.Json.Formatting.Indented);
            EvidenceTabs.SelectedIndex = 1;
            var folder = Path.Combine(project.RootPath, ".pztools", "validation"); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-kahlua.json"), Details.Text);
            Status.Text = $"Installed Kahlua compilation: {results.Count(r => r.Result.Ok)}/{results.Count}. API behavior requires a native probe test.";
        }
        catch (Exception ex) { Status.Text = ex.Message; }
        finally { preflighting = false; }
    }
    private void Clear_Click(object sender, RoutedEventArgs e) => Files.SelectedItem = null;
    private void Scaffold_Click(object sender, RoutedEventArgs e)
    {
        try { ModTestService.Scaffold(project); RefreshFiles(); Status.Text = "Created missing examples in .pztests. Existing files were preserved."; }
        catch (Exception ex) { Status.Text = ex.Message; }
    }
    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        try { if (Files.SelectedItem is TestFile file) EditorIntegration.OpenInVsCode(project, file.Path); }
        catch (Exception ex) { Status.Text = ex.Message; }
    }
    private void Profiles_Click(object sender, RoutedEventArgs e)
    {
        new RunProject { Owner = this }.ShowDialog(); RefreshFiles();
    }
    private void Storage_Click(object sender, RoutedEventArgs e) => new ProjectStorageWindow(project) { Owner = this }.ShowDialog();
    private void Setup_Click(object sender, RoutedEventArgs e) => OpenSetup();
    private bool OpenSetup()
    {
        if (running is not null) return false;
        var setup = new TestRunnerSetup(project, Profiles.SelectedItem as PlaytestProfile) { Owner = this };
        if (setup.ShowDialog() != true) return false;
        RefreshFiles();
        Profiles.SelectedItem = Profiles.Items.Cast<PlaytestProfile>().FirstOrDefault(p => p.Id == setup.SavedProfile?.Id);
        return true;
    }
    private void Profile_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (SetupSummary is null || Profiles.SelectedItem is not PlaytestProfile p) return;
        SetupSummary.Text = p.Mode == PlaytestMode.SinglePlayer
            ? p.SaveMode == PlaytestSaveMode.FreshClone ? "Singleplayer · fresh save copy · character and compatibility checked by the game at startup."
                : "Singleplayer needs a saved character. Set up game tests can find a world or help you create one."
            : $"Local multiplayer · {p.ClientCount} clients · 127.0.0.1:{p.ServerPort} · " +
                (p.SaveMode == PlaytestSaveMode.FreshSessionClone ? "fresh fixture copy. Tests wait for every client to enter the world."
                : "create characters in the game, or capture a fixture in Set up game tests to reuse them.");
    }
    private async void Unit_Click(object sender, RoutedEventArgs e) => await Run(false);
    private async void Game_Click(object sender, RoutedEventArgs e) => await Run(true);
    private async Task Run(bool game)
    {
        if (running is not null || preflighting) return;
        if (game && (!int.TryParse(Timeout.Text, out var seconds) || seconds is < 10 or > 86400))
        { Status.Text = "Set a game timeout between 10 and 86400 seconds."; return; }
        if (game && Profiles.SelectedItem is PlaytestProfile selected)
        {
            preflighting = true;
            try
            {
                var checks = await Task.Run(() => PlaytestSetupService.Check(project, selected, PlaytestSetupService.ResolveGameRoot(selected.Build), true));
                if (checks.Any(c => c.Blocking) && !OpenSetup()) return;
            }
            catch (Exception ex) { Status.Text = ex.Message; return; }
            finally { preflighting = false; }
        }
        running = new CancellationTokenSource(); Unit.IsEnabled = Game.IsEnabled = Setup.IsEnabled = false; Stop.IsEnabled = true;
        Details.Clear(); EndpointStatus.Text = ""; Results.ItemsSource = null; Status.Text = "Running...";
        try
        {
            var filter = Files.SelectedItem is TestFile file ? file.Display : Filter.Text.Trim();
            if (game)
            {
                var profile = Profiles.SelectedItem as PlaytestProfile ?? throw new InvalidOperationException("Choose a playtest profile.");
                var root = PlaytestSetupService.ResolveGameRoot(profile.Build);
                report = await GameTestService.RunAsync(project, profile, root, filter, timeoutSeconds: int.Parse(Timeout.Text),
                    progress: line => Dispatcher.BeginInvoke(() => Status.Text = line), cancellationToken: running.Token,
                    dockClient: DockClient is null ? null : process => Dispatcher.BeginInvoke(() =>
                    {
                        DockClient(process);
                        WindowState = WindowState.Minimized;
                    }), endpointProgress: states =>
                    {
                        var text = string.Join(Environment.NewLine, states.Select(s =>
                            $"{s.Endpoint}: {s.Phase} | ticks {s.SimulationTicks} | heartbeat {s.LastHeartbeat?.ToLocalTime():HH:mm:ss} | " +
                            (string.IsNullOrEmpty(s.BlockingReason) ? s.CurrentCase : s.BlockingReason)));
                        Dispatcher.BeginInvoke(() => EndpointStatus.Text = text);
                    });
            }
            else report = await ModTestService.RunUnitAsync(project, filter, running.Token);
            Results.ItemsSource = report.Tests;
            Details.Text = TestEvidenceService.Summary(report);
            Status.Text = $"Operation: {report.OperationState}; verdict: {report.Verdict}; {report.Tests.Count(t => t.Status == "passed")}/{report.Tests.Count} cases passed; {report.Errors.Count} runner errors.";
            RefreshHistory();
            Artifacts.IsEnabled = true;
        }
        catch (Exception ex) { Status.Text = ex.Message; }
        finally
        {
            running.Dispose(); running = null; Unit.IsEnabled = Game.IsEnabled = Setup.IsEnabled = true; Stop.IsEnabled = false;
            if (closing) Close();
            else if (game && WindowState == WindowState.Minimized) { WindowState = WindowState.Normal; Activate(); }
        }
    }
    private void Result_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Results.SelectedItem is ModTestCase test) { Details.Text = Newtonsoft.Json.JsonConvert.SerializeObject(test, Newtonsoft.Json.Formatting.Indented); EvidenceTabs.SelectedIndex = 1; }
    }
    private void Stop_Click(object sender, RoutedEventArgs e) { running?.Cancel(); Status.Text = "Cancelling and stopping owned test processes..."; }
    private void Artifacts_Click(object sender, RoutedEventArgs e)
    {
        try { var path = report?.ArtifactDirectory ?? selectedSession?.ArtifactDirectory; if (path is not null) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { Status.Text = ex.Message; }
    }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (running is not null) { e.Cancel = true; closing = true; running.Cancel(); }
    }
}
