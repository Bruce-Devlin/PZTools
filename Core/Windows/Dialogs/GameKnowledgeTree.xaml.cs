using System.Diagnostics;
using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using PZTools.Core.Functions;
using PZTools.Core.Functions.Decompile;
using PZTools.Core.Functions.Projects;
using PZTools.Core.Models;
using Node = PZTools.Core.Functions.Decompile.GameKnowledgeGraph.Node;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using Point = System.Windows.Point;
using Path = System.IO.Path;

namespace PZTools.Core.Windows.Dialogs;

public partial class GameKnowledgeTree : Window
{
    private readonly GameKnowledgeIndex _index;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _filterTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Stack<Node> _history = new();
    private readonly GameKnowledgeSymbol? _initial;
    private GameKnowledgeGraph? _graph;
    private Node? _focus;
    private int _page;
    private const int PageSize = 30;
    private readonly ConcurrentQueue<string> _priorityFiles = new();
    private readonly Dictionary<Node, List<TreeViewItem>> _treeItems = new();
    private readonly HashSet<TreeViewItem> _expandedItems = new();
    private readonly HashSet<Node> _matching = new();
    private readonly Dictionary<string, TreeViewItem> _packages = new();
    private readonly Dictionary<string, List<Node>> _packageNodes = new();
    private bool _treeReady;
    private bool _failed;

    public GameKnowledgeTree(GameKnowledgeIndex index, GameKnowledgeSymbol? initial = null)
    {
        _index = index; _initial = initial;
        InitializeComponent();
        Title = $"Game code tree · {index.Build} - PZ Tools";
        _filterTimer.Tick += (_, _) => { _filterTimer.Stop(); PopulateTree(); };
        Loaded += LoadGraph;
    }

    private async void LoadGraph(object sender, RoutedEventArgs e)
    {
        try
        {
            _graph = new GameKnowledgeGraph();
            if (_initial != null) _priorityFiles.Enqueue(_initial.RelativePath);
            await Task.Run(() => GameKnowledgeGraph.BuildAsync(_index, cancellationToken: _lifetime.Token,
                publish: batch => Dispatcher.InvokeAsync(() => ApplyBatch(batch), DispatcherPriority.Background, _lifetime.Token).Task,
                priorityFiles: _priorityFiles), _lifetime.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _failed = true;
            LoadStateText.Text = "Loading stopped · available results remain browsable";
            StatusText.Text = $"Unable to finish relationships: {ex.Message}";
        }
    }

    private void ApplyBatch(GameKnowledgeGraph.Update batch)
    {
        if (_lifetime.IsCancellationRequested || _graph == null) return;
        var changed = _graph.Apply(batch);
        if (!_treeReady)
        {
            _treeReady = true;
            PopulateTree();
            var first = _graph.Nodes.FirstOrDefault(n => n.Symbol == _initial) ?? _graph.Types.FirstOrDefault(n => n.Symbol!.IsGameCode) ?? _graph.Types.FirstOrDefault();
            if (first != null) Navigate(first);
        }
        else
        {
            foreach (var node in changed)
                if (_treeItems.TryGetValue(node, out var items))
                    foreach (var item in items.ToArray()) RefreshItem(item, node);
            // New local variables may make an additional class match the active filter.
            var query = FilterBox.Text.Trim();
            if (query.Length > 0)
                foreach (var node in changed.Where(n => Matches(n, query)))
                {
                    var type = ContainingType(node);
                    if (_matching.Add(type)) AddMatchingType(type);
                }
            if (_focus != null && changed.Contains(_focus))
            {
                var source = _graph.Source(_focus);
                if (SourceText.Text != source) SourceText.Text = source;
                DrawMap();
            }
            else if (batch.Finished) DrawMap();
        }
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (_graph == null) return;
        LoadProgress.Maximum = Math.Max(1, _graph.Total);
        LoadProgress.Value = _graph.Finished ? LoadProgress.Maximum : _graph.Completed;
        LoadStateText.Text = _failed ? "Loading stopped · available results remain browsable" : _graph.Finished ? "Relationships loaded" : $"Loading relationships · {_graph.Completed:N0} / {_graph.Total:N0} declarations · callers are still being discovered";
        StatusText.Text = $"{_graph.Types.Count:N0} classes · {_graph.Nodes.Count:N0} declarations / variables · {_graph.Edges.Count:N0} relationships loaded · {_matching.Count:N0} matching classes" +
            (_graph.Warnings.Count > 0 ? $" · {_graph.Warnings.Count} source files unavailable" : "");
        StatusText.ToolTip = string.Join('\n', _graph.Warnings);
    }

    private static bool Matches(Node node, string query) => node.Label.Contains(query, StringComparison.OrdinalIgnoreCase) || node.Symbol!.QualifiedName.Contains(query, StringComparison.OrdinalIgnoreCase);
    private static Node ContainingType(Node node) { while (node.Parent != null && node.Kind != "Type") node = node.Parent; return node; }

    private TreeViewItem Item(Node node)
    {
        var item = new TreeViewItem { Header = $"{node.Kind} · {node.Label}", Tag = node, ToolTip = node.Symbol?.QualifiedName };
        if (!_treeItems.TryGetValue(node, out var items)) _treeItems[node] = items = new();
        items.Add(item);
        item.Expanded += (_, e) =>
        {
            if (e.OriginalSource != item) return;
            _expandedItems.Add(item);
            RefreshItem(item, node);
            Prioritize(node);
        };
        RefreshItem(item, node);
        return item;
    }

    private void RefreshItem(TreeViewItem item, Node node)
    {
        item.ToolTip = node.Symbol?.QualifiedName + (node.Unavailable ? "\nSource unavailable" : node.Analyzed ? "\nRelationships loaded" : "\nRelationships pending; select to prioritize");
        var pending = !node.Analyzed && !node.Unavailable && node.Kind is "Method" or "Constructor";
        foreach (var placeholder in item.Items.Cast<TreeViewItem>().Where(i => i.Tag == null).ToArray()) item.Items.Remove(placeholder);
        if (_expandedItems.Contains(item))
        {
            var existing = item.Items.Cast<TreeViewItem>().Select(i => i.Tag).ToHashSet();
            foreach (var child in node.Children.Where(n => !existing.Contains(n)).OrderBy(n => n.Kind).ThenBy(n => n.Label)) item.Items.Add(Item(child));
            if (pending) item.Items.Add(new TreeViewItem { Header = "Variables loading...", IsEnabled = false });
        }
        else if (node.Children.Count > 0 || pending) item.Items.Add(new TreeViewItem { Header = "Expand to load members", IsEnabled = false });
    }

    private void Prioritize(Node node)
    {
        if (!node.Analyzed && !node.Unavailable && !_failed) _priorityFiles.Enqueue(node.Symbol!.RelativePath);
    }

    private void PopulateTree()
    {
        if (_graph is null) return;
        ClassTree.Items.Clear();
        _treeItems.Clear(); _expandedItems.Clear(); _matching.Clear(); _packages.Clear(); _packageNodes.Clear();
        var query = FilterBox.Text.Trim();
        var matching = query.Length == 0 ? _graph.Types.Where(n => n.Parent == null).ToList()
            : _graph.Nodes.Where(n => Matches(n, query)).Select(ContainingType).Distinct().ToList();
        foreach (var node in matching.OrderBy(n => n.Symbol!.Package).ThenBy(n => n.Label))
        { _matching.Add(node); AddMatchingType(node); }
        UpdateStatus();
    }

    private void AddMatchingType(Node node)
    {
        var package = node.Symbol!.Package;
        if (!_packages.TryGetValue(package, out var item))
        {
            item = new TreeViewItem();
            _packages[package] = item; _packageNodes[package] = new();
            var packageItem = item;
            item.Expanded += (_, e) =>
            {
                if (e.OriginalSource != packageItem || _expandedItems.Contains(packageItem)) return;
                _expandedItems.Add(packageItem); packageItem.Items.Clear();
                foreach (var child in _packageNodes[package]) packageItem.Items.Add(Item(child));
            };
            item.Items.Add(new TreeViewItem { Header = "Expand to browse classes" });
            ClassTree.Items.Add(item);
        }
        _packageNodes[package].Add(node);
        item.Header = $"{(package.Length == 0 ? "(default package)" : package)} ({_packageNodes[package].Count:N0})";
        if (_expandedItems.Contains(item)) item.Items.Add(Item(node));
        if (FilterBox.Text.Trim().Length > 0) item.IsExpanded = true;
    }

    private void Navigate(Node node, bool remember = true)
    {
        if (_graph is null) return;
        if (remember && _focus != null && _focus != node) _history.Push(_focus);
        _focus = node; _page = 0;
        Prioritize(node);
        BackButton.IsEnabled = _history.Count > 0;
        OwnerButton.IsEnabled = node.Parent != null;
        OpenButton.IsEnabled = node.Symbol != null;
        FocusTitle.Text = node.Symbol?.QualifiedName ?? node.Label;
        SourceLocation.Text = $"{node.Kind} · {node.Symbol?.Location}";
        SourceText.Text = _graph.Source(node);
        SourceText.ScrollToHome();
        DrawMap();
    }

    private void DrawMap()
    {
        if (_graph is null || _focus is null) return;
        Map.Children.Clear();
        var incoming = _graph.Incoming(_focus).GroupBy(e => (e.From, e.Kind)).Select(g => (Node: (Node?)g.Key.From, Label: $"{g.Key.Kind} ← {g.Key.From.Symbol?.QualifiedName}")).ToList();
        var outgoing = _focus.Children.Select(n => (Node: (Node?)n, Label: $"Contains · {n.Kind}\n{n.Label}"))
            .Concat(_graph.Outgoing(_focus).GroupBy(e => (e.To, e.Kind, e.Description)).Select(g => (Node: g.Key.To,
                Label: $"{g.Key.Kind} · line {g.First().Line}\n{g.Key.To?.Label ?? g.Key.Description}"))).ToList();
        if (_focus.Parent != null) incoming.Insert(0, (_focus.Parent, $"Contained by\n{_focus.Parent.Label}"));
        var pages = Math.Max(1, (Math.Max(incoming.Count, outgoing.Count) + PageSize - 1) / PageSize);
        _page = Math.Clamp(_page, 0, pages - 1);
        var left = incoming.Skip(_page * PageSize).Take(PageSize).ToList();
        var right = outgoing.Skip(_page * PageSize).Take(PageSize).ToList();
        Map.Height = Math.Max(260, 110 + Math.Max(left.Count, right.Count) * 82);
        Caption($"Incoming / owner ({incoming.Count:N0}){(_graph.Finished ? "" : " · loading")}", 15, 8);
        Caption($"Members / outgoing ({outgoing.Count:N0}){(_focus.Analyzed ? "" : _focus.Unavailable ? " · unavailable" : " · loading")}", 590, 8);
        Caption($"Page {_page + 1} / {pages}", 330, 8);
        if (_page > 0) PageButton("← Previous", 300, () => { _page--; DrawMap(); });
        if (_page + 1 < pages) PageButton("Next →", 415, () => { _page++; DrawMap(); });
        const double centerY = 130;
        for (var i = 0; i < left.Count; i++) Connector(265, 85 + i * 82, 300, centerY);
        for (var i = 0; i < right.Count; i++) Connector(550, centerY, 585, 85 + i * 82);
        Card(_focus, $"{_focus.Kind}\n{_focus.Label}", 300, centerY - 32, true);
        for (var i = 0; i < left.Count; i++) Card(left[i].Node, left[i].Label, 15, 53 + i * 82);
        for (var i = 0; i < right.Count; i++) Card(right[i].Node, right[i].Label, 585, 53 + i * 82);
        if (incoming.Count + outgoing.Count == 0) Caption(_graph.Finished ? "No indexed relationships. Read the source below." : "Relationships are loading; you can keep browsing.", 280, 205);
    }

    private void Caption(string text, double x, double y)
    {
        var label = new TextBlock { Text = text, Foreground = Brush("Brush.TextMuted"), FontSize = 11 };
        Canvas.SetLeft(label, x); Canvas.SetTop(label, y); Map.Children.Add(label);
    }
    private Brush Brush(string key) => (Brush)FindResource(key);
    private void Connector(double x1, double y1, double x2, double y2)
    {
        Map.Children.Add(new Polyline { Points = new PointCollection { new(x1, y1), new((x1 + x2) / 2, y1), new((x1 + x2) / 2, y2), new(x2, y2) }, Stroke = Brush("Brush.TextInfo"), StrokeThickness = 1.5 });
        Map.Children.Add(new Polygon { Points = new PointCollection { new(x2, y2), new(x2 - 6, y2 - 4), new(x2 - 6, y2 + 4) }, Fill = Brush("Brush.TextInfo") });
    }
    private void Card(Node? node, string text, double x, double y, bool focused = false)
    {
        var button = new Button { Width = 250, Height = 64, Padding = new Thickness(8),
            Style = (Style)FindResource("CompactButton"), BorderThickness = new Thickness(focused ? 2 : 1),
            BorderBrush = Brush(focused ? "Brush.TextInfo" : "Brush.Border"),
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 48 },
            ToolTip = node == null ? text + "\nTarget unavailable; inspect this call in the source below." : text + "\n" + node.Symbol?.QualifiedName };
        if (node != null) button.Click += (_, _) => Navigate(node);
        else button.IsEnabled = false;
        Canvas.SetLeft(button, x); Canvas.SetTop(button, y); Map.Children.Add(button);
    }
    private void PageButton(string label, double x, Action action)
    {
        var button = new Button { Content = label, Style = (Style)FindResource("CompactButton") };
        button.Click += (_, _) => action(); Canvas.SetLeft(button, x); Canvas.SetTop(button, 35); Map.Children.Add(button);
    }
    private void Filter_Changed(object sender, TextChangedEventArgs e) { _filterTimer.Stop(); _filterTimer.Start(); }
    private void Tree_Selected(object sender, RoutedPropertyChangedEventArgs<object> e) { if (e.NewValue is TreeViewItem { Tag: Node node }) Navigate(node); }
    private void Back_Click(object sender, RoutedEventArgs e) { if (_history.TryPop(out var node)) Navigate(node, false); }
    private void Owner_Click(object sender, RoutedEventArgs e) { if (_focus?.Parent is { } parent) Navigate(parent); }
    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (_focus?.Symbol is not { } symbol) return;
        try
        {
            var root = Path.GetFullPath(_index.SourceRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var path = Path.GetFullPath(Path.Combine(root, symbol.RelativePath));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) { StatusText.Text = "Source file unavailable."; return; }
            var editor = EditorIntegration.FindVsCode();
            if (string.IsNullOrWhiteSpace(editor)) WindowsHelpers.OpenFile(path);
            else
            {
                var start = new ProcessStartInfo(editor) { UseShellExecute = true };
                start.ArgumentList.Add("--reuse-window"); start.ArgumentList.Add("--goto"); start.ArgumentList.Add($"{path}:{symbol.Line}"); Process.Start(start);
            }
        }
        catch (Exception ex) { StatusText.Text = $"Unable to open source: {ex.Message}"; }
    }
    protected override void OnClosed(EventArgs e)
    {
        _filterTimer.Stop(); _lifetime.Cancel();
        base.OnClosed(e);
    }
}
