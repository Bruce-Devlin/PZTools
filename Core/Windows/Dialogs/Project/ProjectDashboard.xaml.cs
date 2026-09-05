using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PZTools.Core.Functions;
using PZTools.Core.Functions.Projects;
using PZTools.Core.Models;

namespace PZTools.Core.Windows.Dialogs.Project
{
    public partial class ProjectDashboard : Window
    {
        private readonly ModProject _project;
        private CancellationTokenSource? _refreshCts;
        private ProjectHealthReport? _latestReport;

        public ProjectDashboard(ModProject project)
        {
            InitializeComponent();
            _project = project ?? throw new ArgumentNullException(nameof(project));
            ProjectTitle.Text = project.Name;
            ProjectPathText.Text = project.RootPath;
            TargetCountText.Text = project.Targets.Count.ToString();
            Loaded += async (_, _) => await RefreshReportAsync();
        }

        private async Task RefreshReportAsync()
        {
            _refreshCts?.Cancel();
            _refreshCts?.Dispose();
            var refreshCts = new CancellationTokenSource();
            _refreshCts = refreshCts;
            _latestReport = null;
            ExportReportButton.IsEnabled = false;
            BusyOverlay.Visibility = Visibility.Visible;
            BusyText.Text = ValidateLuaCheck.IsChecked == true ? "Checking structure and Lua syntax..." : "Checking project structure...";

            try
            {
                var validateLua = ValidateLuaCheck.IsChecked == true;
                var token = refreshCts.Token;
                var report = await Task.Run(() => ProjectHealthService.AnalyzeAsync(_project, validateLua, token), token);
                _latestReport = report;
                ExportReportButton.IsEnabled = true;
                DiagnosticsList.ItemsSource = report.Diagnostics
                    .OrderByDescending(x => x.Severity)
                    .ThenBy(x => x.Target)
                    .ThenBy(x => x.Code)
                    .ToList();
                LuaCountText.Text = report.LuaFileCount.ToString();
                ScriptCountText.Text = report.ScriptFileCount.ToString();
                ContentSizeText.Text = FormatSize(report.ContentBytes);
                ReadinessText.Text = report.Summary;
                ReadinessBadge.Background = FindResource(report.ErrorCount > 0 ? "Brush.TextDanger" :
                    report.WarningCount > 0 ? "Brush.TextWarn" : "Brush.Accent") as System.Windows.Media.Brush;
                LastCheckedText.Text = $"Checked {report.CheckedAt:t} · {report.Diagnostics.Count} finding(s)";

                if (report.Diagnostics.Count == 0)
                    RecommendationText.Text = "No structural or Lua syntax problems found.";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                DiagnosticsList.ItemsSource = new[]
                {
                    new ProjectDiagnostic
                    {
                        Severity = DiagnosticSeverity.Error,
                        Code = "PZT000",
                        Target = "Project",
                        Message = ex.Message,
                        Recommendation = "Review the project path and file permissions, then retry."
                    }
                };
                ReadinessText.Text = "Health check failed";
            }
            finally
            {
                if (ReferenceEquals(_refreshCts, refreshCts))
                    BusyOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshReportAsync();

        private void SetupVsCode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = EditorIntegration.PrepareVsCodeWorkspace(_project);
                var created = result.CreatedFiles.Count == 0 ? "No files needed creating." :
                    $"Created {result.CreatedFiles.Count} workspace file(s).";
                var updated = result.UpdatedFiles.Count == 0 ? "" :
                    $"\nUpdated {result.UpdatedFiles.Count} workspace file(s) with missing PZTools tasks.";
                var preserved = result.PreservedFiles.Count == 0 ? "" :
                    $"\nPreserved {result.PreservedFiles.Count} existing file(s); user-authored settings and tasks remain intact.";
                MessageBox.Show($"{created}{updated}{preserved}\n\nThe workspace recommends Lua, EmmyLua, and Build 42 ZedScript tooling and provides test/deploy tasks.",
                    "VS Code workspace ready", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "VS Code setup failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenVsCode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                EditorIntegration.OpenInVsCode(_project);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Open in VS Code", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private async void ProjectSettings_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ProjectSettings(_project.RootPath) { Owner = this };
            if (dialog.ShowDialog() == true)
                await RefreshReportAsync();
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e) => WindowsHelpers.ShowInExplorer(_project.RootPath);

        private void ExportReport_Click(object sender, RoutedEventArgs e)
        {
            if (_latestReport is null)
                return;

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export project health report",
                Filter = "Markdown document (*.md)|*.md|All files (*.*)|*.*",
                DefaultExt = ".md",
                AddExtension = true,
                InitialDirectory = _project.RootPath,
                FileName = $"{SafeFileName(_project.Name)}-health.md"
            };
            if (dialog.ShowDialog(this) != true)
                return;

            try
            {
                File.WriteAllText(dialog.FileName, ProjectHealthReportMarkdown.Format(_latestReport), new UTF8Encoding(false));
                MessageBox.Show("The project health report was exported successfully.", "Project Health",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Could not export report", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void GameLog_Click(object sender, RoutedEventArgs e)
        {
            var path = Path.Combine(PZTools.Core.Functions.Zomboid.ZomboidGame.GameUserDirectory, "console.txt");
            if (File.Exists(path))
                WindowsHelpers.OpenFile(path);
            else
                MessageBox.Show("No Project Zomboid console.txt was found. Run the game once, then refresh project health.",
                "Game log", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void DiagnosticsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DiagnosticsList.SelectedItem is ProjectDiagnostic diagnostic)
                RecommendationText.Text = diagnostic.Recommendation;
        }

        private void DiagnosticsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DiagnosticsList.SelectedItem is not ProjectDiagnostic diagnostic || string.IsNullOrWhiteSpace(diagnostic.FilePath))
                return;
            try
            {
                EditorIntegration.OpenInVsCode(_project, diagnostic.FilePath, diagnostic.Line);
            }
            catch { WindowsHelpers.OpenFile(diagnostic.FilePath); }
        }

        protected override void OnClosed(EventArgs e)
        {
            _refreshCts?.Cancel();
            _refreshCts?.Dispose();
            base.OnClosed(e);
        }

        private static string FormatSize(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB" };
            double value = bytes;
            var unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            return $"{value:0.#} {units[unit]}";
        }

        private static string SafeFileName(string value)
            => string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c));
    }
}
