using System.Windows;
using System.Windows.Input;
using PZTools.Core.Functions.Projects;
using PZTools.Core.Models;

namespace PZTools.Core.Windows.Dialogs.Project;

public partial class SearchProject : Window
{
    private readonly ModProject _project;
    private CancellationTokenSource? _searchCancellation;
    public ProjectSearchMatch? SelectedMatch { get; private set; }

    public SearchProject(ModProject project)
    {
        _project = project;
        InitializeComponent();
        ProjectLabel.Text = project.Name + "  ·  All build targets and shared files";
        Loaded += (_, _) => QueryBox.Focus();
    }

    private async void SearchChanged(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) await SearchAsync(debounce: true);
    }

    private async void Search_Click(object sender, RoutedEventArgs e) => await SearchAsync(debounce: false);

    private async Task SearchAsync(bool debounce)
    {
        _searchCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _searchCancellation = cancellation;
        var query = QueryBox.Text;
        var matchCase = MatchCaseBox.IsChecked == true;
        var fileNames = FileNamesBox.IsChecked == true;
        ResultsGrid.ItemsSource = null;
        EmptyText.Visibility = Visibility.Visible;
        EmptyText.Text = string.IsNullOrWhiteSpace(query) ? "Enter text or a file name to get started." : "Searching…";
        SummaryText.Text = "Generated folders and internal project data are excluded.";
        try
        {
            if (string.IsNullOrWhiteSpace(query)) return;
            if (debounce) await Task.Delay(250, cancellation.Token);
            var result = await Task.Run(() => ProjectSearchService.Search(_project.RootPath, query, matchCase, fileNames, cancellation.Token), cancellation.Token);
            if (cancellation.IsCancellationRequested) return;
            ResultsGrid.ItemsSource = result.Matches;
            EmptyText.Visibility = result.Matches.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyText.Text = "No matches found. Try a shorter term or search file names.";
            SummaryText.Text = $"{result.Matches.Count}{(result.LimitReached ? "+" : "")} matching {(fileNames ? "files" : "lines")} · {result.FilesSearched} files searched" +
                (result.FilesSkipped > 0 ? $" · {result.FilesSkipped} skipped (large, binary, linked, or unavailable)" : "") +
                (result.LimitReached ? " · Refine your search to see more." : "");
            if (result.Matches.Count > 0) ResultsGrid.SelectedIndex = 0;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!cancellation.IsCancellationRequested) { EmptyText.Text = "Search could not complete."; SummaryText.Text = ex.Message; }
        }
        finally
        {
            if (ReferenceEquals(_searchCancellation, cancellation)) _searchCancellation = null;
            cancellation.Dispose();
        }
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsGrid.SelectedItem is not ProjectSearchMatch match) return;
        SelectedMatch = match;
        DialogResult = true;
    }

    private void Results_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            System.Windows.Controls.ItemsControl.ContainerFromElement(ResultsGrid, source) is System.Windows.Controls.DataGridRow)
            Open_Click(sender, e);
    }

    private void Results_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; Open_Click(sender, e); }
    }

    protected override void OnClosed(EventArgs e)
    {
        _searchCancellation?.Cancel();
        base.OnClosed(e);
    }
}
