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
    private readonly ModProject project;
    private CancellationTokenSource? running;
    private ModTestReport? report;
    private bool closing;
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
            Profiles.ItemsSource = PlaytestProfileStore.Load(project); Profiles.SelectedIndex = 0;
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
    private async void Unit_Click(object sender, RoutedEventArgs e) => await Run(false);
    private async void Game_Click(object sender, RoutedEventArgs e) => await Run(true);
    private async Task Run(bool game)
    {
        if (running is not null) return;
        running = new CancellationTokenSource(); Unit.IsEnabled = Game.IsEnabled = false; Stop.IsEnabled = true;
        Details.Clear(); Results.ItemsSource = null; Status.Text = "Running...";
        try
        {
            var filter = Files.SelectedItem is TestFile file ? file.Display : Filter.Text.Trim();
            if (game)
            {
                var profile = Profiles.SelectedItem as PlaytestProfile ?? throw new InvalidOperationException("Choose a playtest profile.");
                var root = ZomboidGame.GameMode == "Managed" ? Path.Combine(ZomboidGame.GameDirectory, profile.Build.ToString(CultureInfo.InvariantCulture)) : ZomboidGame.GameDirectory;
                report = await GameTestService.RunAsync(project, profile, root, filter, progress: line => Dispatcher.BeginInvoke(() => Status.Text = line), cancellationToken: running.Token);
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
            running.Dispose(); running = null; Unit.IsEnabled = Game.IsEnabled = true; Stop.IsEnabled = false;
            if (closing) Close();
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
