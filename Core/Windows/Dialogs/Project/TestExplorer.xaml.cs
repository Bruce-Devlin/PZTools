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
    private sealed record TestFile(string Path, string Display);
    public TestExplorer(ModProject project)
    {
        InitializeComponent(); this.project = project; RefreshFiles();
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
            Details.Text = string.Join(Environment.NewLine, report.Errors);
            Status.Text = $"{(report.Passed ? "Passed" : "Failed")}: {report.Tests.Count(t => t.Status == "passed")}/{report.Tests.Count} tests passed; {report.Errors.Count} runner errors. {report.ArtifactDirectory}";
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
        if (Results.SelectedItem is ModTestCase test) Details.Text = test.File + Environment.NewLine + test.Message + Environment.NewLine + string.Join(Environment.NewLine, test.Steps);
    }
    private void Stop_Click(object sender, RoutedEventArgs e) { running?.Cancel(); Status.Text = "Cancelling and stopping owned test processes..."; }
    private void Artifacts_Click(object sender, RoutedEventArgs e)
    {
        try { if (report is not null) Process.Start(new ProcessStartInfo(report.ArtifactDirectory) { UseShellExecute = true }); }
        catch (Exception ex) { Status.Text = ex.Message; }
    }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (running is not null) { e.Cancel = true; closing = true; running.Cancel(); }
    }
}
