using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using PZTools.Core.Functions;
using PZTools.Core.Functions.Decompile;
using PZTools.Core.Functions.Projects;
using PZTools.Core.Models;

namespace PZTools.Core.Windows.Dialogs
{
    public partial class GameKnowledgeExplorer : Window
    {
        private readonly IReadOnlyList<GameSourceBuild> _builds;
        private readonly DispatcherTimer _searchTimer;
        private CancellationTokenSource? _loadCts;
        private GameKnowledgeIndex? _index;

        public GameKnowledgeExplorer(string sourceRoot)
        {
            InitializeComponent();
            this.FreeDragThisWindow();
            _builds = GameKnowledgeBase.DiscoverBuilds(sourceRoot);
            BuildCombo.ItemsSource = _builds;
            if (_builds.Count > 0)
                BuildCombo.SelectedIndex = 0;
            _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
            _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); ApplySearch(); };
            Loaded += async (_, _) =>
            {
                if (_builds.Count == 0)
                {
                    BusyOverlay.Visibility = Visibility.Collapsed;
                    IndexSummaryText.Text = "No decompiled builds";
                    ResultCountText.Text = "Decompile the game files to create the knowledge base.";
                    return;
                }
                await LoadSelectedBuildAsync(false);
            };
        }

        private async Task LoadSelectedBuildAsync(bool force)
        {
            if (BuildCombo.SelectedItem is not GameSourceBuild build)
                return;

            _loadCts?.Cancel();
            _loadCts?.Dispose();
            var cts = new CancellationTokenSource();
            _loadCts = cts;
            BusyOverlay.Visibility = Visibility.Visible;
            BusyText.Text = force ? "Rebuilding game-code index..." : "Loading game-code index...";
            ResultsList.ItemsSource = null;
            _index = null;

            try
            {
                var progress = new Progress<string>(message => BusyText.Text = message);
                _index = await Task.Run(() => GameKnowledgeBase.LoadOrBuildAsync(
                    build.SourcePath, build.Name, force, progress, cts.Token), cts.Token);
                IndexSummaryText.Text = $"{_index.FileCount:N0} files · {_index.Symbols.Count:N0} symbols";
                ApplySearch();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                IndexSummaryText.Text = "Index unavailable";
                ResultCountText.Text = ex.Message;
            }
            finally
            {
                if (ReferenceEquals(_loadCts, cts))
                    BusyOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void ApplySearch()
        {
            if (_index is null)
                return;

            GameSymbolKind? kind = null;
            if (KindCombo.SelectedItem is ComboBoxItem item &&
                Enum.TryParse<GameSymbolKind>(item.Tag?.ToString(), out var selectedKind))
                kind = selectedKind;

            var results = GameKnowledgeBase.Search(_index, SearchBox.Text, kind);
            ResultsList.ItemsSource = results;
            ResultCountText.Text = results.Count == 500
                ? "Showing the first 500 matches. Add another search term to narrow the results."
                : $"{results.Count:N0} result(s) · {_index.TypeCount:N0} types · {_index.MethodCount:N0} methods/constructors · {_index.FieldCount:N0} fields";
        }

        private void ShowDetails(GameKnowledgeSymbol? symbol)
        {
            var selected = symbol is not null;
            OpenButton.IsEnabled = selected;
            CopyButton.IsEnabled = selected;
            if (symbol is null)
                return;

            DetailNameText.Text = symbol.QualifiedName;
            SignatureText.Text = symbol.Signature;
            DetailLocationText.Text = $"{symbol.Location}\nPackage: {(string.IsNullOrWhiteSpace(symbol.Package) ? "(default)" : symbol.Package)}";
            DocumentationText.Text = string.IsNullOrWhiteSpace(symbol.Documentation)
                ? "CFR did not recover JavaDoc for this declaration. Read the implementation and call sites before relying on its behaviour."
                : symbol.Documentation;
            GuidanceText.Text = BuildGuidance(symbol);
        }

        private static string BuildGuidance(GameKnowledgeSymbol symbol)
        {
            var visibility = symbol.Modifiers.Contains("public", StringComparison.Ordinal) ? "public" :
                symbol.Modifiers.Contains("protected", StringComparison.Ordinal) ? "protected" :
                symbol.Modifiers.Contains("private", StringComparison.Ordinal) ? "private" : "package-private";
            var staticText = symbol.Modifiers.Contains("static", StringComparison.Ordinal) ? "static" : "instance";
            return symbol.Kind switch
            {
                GameSymbolKind.Type => $"Recovered {visibility} Java type. Search its qualified name to see indexed members and inspect its source for lifecycle and ownership rules.",
                GameSymbolKind.Field => $"Recovered {visibility} {staticText} field. Direct access may be restricted; prefer a public game method when one exists.",
                GameSymbolKind.Constructor => $"Recovered {visibility} constructor. Confirm how the game creates and registers this type before constructing it in a mod.",
                _ => $"Recovered {visibility} {staticText} method. Its presence in decompiled Java does not guarantee Lua exposure or safe mod API status; inspect the implementation, callers, and runtime accessibility first."
            };
        }

        private async void BuildCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded)
                await LoadSelectedBuildAsync(false);
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchTimer.Stop();
            _searchTimer.Start();
        }

        private void KindCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded) ApplySearch();
        }

        private void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => ShowDetails(ResultsList.SelectedItem as GameKnowledgeSymbol);

        private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelectedSource();

        private async void Rebuild_Click(object sender, RoutedEventArgs e) => await LoadSelectedBuildAsync(true);

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            if (ResultsList.SelectedItem is GameKnowledgeSymbol symbol)
                System.Windows.Clipboard.SetText(symbol.Signature);
        }

        private void Open_Click(object sender, RoutedEventArgs e) => OpenSelectedSource();

        private void OpenSelectedSource()
        {
            if (_index is null || ResultsList.SelectedItem is not GameKnowledgeSymbol symbol)
                return;
            var sourceRoot = Path.GetFullPath(_index.SourceRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var path = Path.GetFullPath(Path.Combine(sourceRoot, symbol.RelativePath));
            if (!path.StartsWith(sourceRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                return;

            var editor = EditorIntegration.FindVsCode();
            if (!string.IsNullOrWhiteSpace(editor))
            {
                var start = new ProcessStartInfo { FileName = editor, UseShellExecute = true };
                start.ArgumentList.Add("--reuse-window");
                start.ArgumentList.Add("--goto");
                start.ArgumentList.Add($"{path}:{symbol.Line}");
                Process.Start(start);
            }
            else
            {
                WindowsHelpers.OpenFile(path);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _searchTimer.Stop();
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            base.OnClosed(e);
        }
    }
}
