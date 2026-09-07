using System.IO;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using PZTools.Core.Models;

namespace PZTools.Core.Functions.Decompile;

// Deliberately conservative source analysis: this is navigation, not a Java compiler.
internal sealed class GameKnowledgeGraph
{
    internal sealed class Node
    {
        public required string Label { get; init; }
        public required string Kind { get; init; }
        public GameKnowledgeSymbol? Symbol { get; init; }
        public string TypeName { get; init; } = "";
        public Node? Parent { get; set; }
        public List<Node> Children { get; } = new();
        public int Start { get; set; }
        public int End { get; set; }
        public bool Analyzed { get; set; }
        public bool Unavailable { get; set; }
    }

    internal sealed record Edge(Node From, Node? To, string Kind, string Description, int Line);
    public List<Node> Types { get; } = new();
    public List<Node> Nodes { get; } = new();
    public List<Edge> Edges { get; } = new();
    public List<string> Warnings { get; } = new();
    private readonly Dictionary<string, string> _sources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Node, List<Edge>> _outgoing = new();
    private readonly Dictionary<Node, List<Edge>> _incoming = new();
    private readonly Dictionary<string, Node> _types = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _imports = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Node, Node> _publishedNodes = new();
    internal sealed record NodeUpdate(Node Key, Node? Parent, Node Snapshot);
    internal sealed record Update(NodeUpdate[] Nodes, Edge[] Edges, KeyValuePair<string, string>[] Sources,
        string[] Warnings, int Completed, int Total, bool Finished);
    public int Completed { get; private set; }
    public int Total { get; private set; }
    public bool Finished { get; private set; }

    // Worker nodes are identity keys only. The UI owns separate nodes and collections;
    // immutable batches cross the dispatcher, never live mutable graph collections.
    public HashSet<Node> Apply(Update update)
    {
        var changed = new HashSet<Node>();
        foreach (var data in update.Nodes)
        {
            if (!_publishedNodes.TryGetValue(data.Key, out var node))
            {
                node = data.Snapshot;
                _publishedNodes.Add(data.Key, node);
                Nodes.Add(node);
                if (node.Kind == "Type") Types.Add(node);
            }
            node.Start = data.Snapshot.Start; node.End = data.Snapshot.End;
            node.Analyzed = data.Snapshot.Analyzed; node.Unavailable = data.Snapshot.Unavailable;
            changed.Add(node);
        }
        foreach (var data in update.Nodes)
        {
            var node = _publishedNodes[data.Key];
            if (node.Parent == null && data.Parent != null)
            {
                node.Parent = _publishedNodes[data.Parent];
                node.Parent.Children.Add(node);
                changed.Add(node.Parent);
            }
        }
        foreach (var source in update.Sources) _sources[source.Key] = source.Value;
        foreach (var edge in update.Edges)
        {
            var from = _publishedNodes[edge.From];
            var to = edge.To == null ? null : _publishedNodes[edge.To];
            Add(from, to, edge.Kind, edge.Description, edge.Line);
            changed.Add(from); if (to != null) changed.Add(to);
        }
        Warnings.AddRange(update.Warnings);
        Completed = update.Completed; Total = update.Total; Finished = update.Finished;
        return changed;
    }
    public IReadOnlyList<Edge> Outgoing(Node node) => _outgoing.GetValueOrDefault(node) ?? [];
    public IReadOnlyList<Edge> Incoming(Node node) => _incoming.GetValueOrDefault(node) ?? [];
    public string Source(Node node) => node.Symbol is { } s && _sources.TryGetValue(s.RelativePath, out var text)
        && node.End > node.Start ? text[node.Start..Math.Min(node.End, text.Length)]
        : node.Unavailable ? "Source unavailable." : "Source is loading in the background...";

    public static async Task<GameKnowledgeGraph> BuildAsync(GameKnowledgeIndex index,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default,
        Func<Update, Task>? publish = null, ConcurrentQueue<string>? priorityFiles = null)
    {
        var graph = new GameKnowledgeGraph();
        var root = Path.GetFullPath(index.SourceRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var symbol in index.Symbols)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = new Node { Label = symbol.Kind == GameSymbolKind.Type ? symbol.Name : symbol.Signature,
                Kind = symbol.Kind.ToString(), Symbol = symbol, TypeName = symbol.ReturnType };
            graph.Nodes.Add(node);
            if (symbol.Kind == GameSymbolKind.Type)
            { graph.Types.Add(node); graph._types.TryAdd(symbol.QualifiedName, node); }
        }
        foreach (var node in graph.Nodes)
            if (graph._types.TryGetValue(node.Symbol!.DeclaringType, out var parent))
            { node.Parent = parent; parent.Children.Add(node); }
        var files = graph.Nodes.GroupBy(n => n.Symbol!.RelativePath).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.OrdinalIgnoreCase);
        var remaining = new Queue<string>(files.Keys);
        graph.Total = graph.Nodes.Count;
        var changed = new HashSet<Node>(graph.Nodes);
        var sources = new List<KeyValuePair<string, string>>();
        var edgeOffset = 0; var warningOffset = 0;
        var timer = Stopwatch.StartNew();
        async Task Flush(bool force = false)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!force && timer.ElapsedMilliseconds < 100 && changed.Count < 128) return;
            if (publish != null)
            {
                var batch = new Update(changed.Select(n => new NodeUpdate(n, n.Parent, new Node {
                    Label = n.Label, Kind = n.Kind, Symbol = n.Symbol, TypeName = n.TypeName,
                    Start = n.Start, End = n.End, Analyzed = n.Analyzed, Unavailable = n.Unavailable })).ToArray(),
                    graph.Edges.Skip(edgeOffset).ToArray(), sources.ToArray(), graph.Warnings.Skip(warningOffset).ToArray(),
                    graph.Completed, graph.Total, graph.Finished);
                await publish(batch);
            }
            progress?.Report($"Inferring relationships: {graph.Completed:N0} / {graph.Total:N0} declarations · {graph.Edges.Count:N0} relationships");
            changed.Clear(); sources.Clear(); edgeOffset = graph.Edges.Count; warningOffset = graph.Warnings.Count; timer.Restart();
        }
        // Publish the complete declaration skeleton before reading or inferring any source.
        await Flush(true);
        var firstFile = true;
        while (files.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? file = null;
            while (priorityFiles?.TryDequeue(out var requested) == true)
                if (files.ContainsKey(requested)) { file = requested; break; }
            while (file == null && remaining.TryDequeue(out var next)) if (files.ContainsKey(next)) file = next;
            if (file == null) break;
            var declarations = files[file]; files.Remove(file);
            string source;
            try
            {
                var path = Path.GetFullPath(Path.Combine(root, file));
                if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new IOException("Source path is outside the indexed root.");
                source = (await File.ReadAllTextAsync(path, cancellationToken)).Replace("\r\n", "\n");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                graph.Warnings.Add($"{file}: {ex.Message}");
                foreach (var node in declarations) { node.Unavailable = true; changed.Add(node); graph.Completed++; }
                await Flush(); continue;
            }
            graph._sources[file] = source;
            sources.Add(new(file, source));
            var masked = Mask(source);
            graph._imports[file] = Regex.Matches(masked, @"\bimport\s+(?!static\b)([\w.$]+(?:\.\*)?)\s*;")
                .Select(m => m.Groups[1].Value).ToList();
            var starts = new List<int> { 0 };
            for (var i = 0; i < source.Length; i++) if (source[i] == '\n') starts.Add(i + 1);
            foreach (var node in declarations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var symbol = node.Symbol!;
                if (symbol.Line < 1 || symbol.Line > starts.Count) node.Unavailable = true;
                else { node.Start = starts[symbol.Line - 1]; node.End = DeclarationEnd(masked, node.Start, symbol.Kind); }
                changed.Add(node);
            }
            await Flush(firstFile);
            foreach (var node in declarations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var nodeOffset = graph.Nodes.Count;
                if (!node.Unavailable) { graph.Analyze(node, cancellationToken, masked); node.Analyzed = true; }
                changed.Add(node);
                foreach (var variable in graph.Nodes.Skip(nodeOffset)) { variable.Analyzed = true; changed.Add(variable); }
                graph.Completed++;
                await Flush();
            }
            await Flush(firstFile);
            firstFile = false;
        }
        graph.Finished = true;
        await Flush(true);
        return graph;
    }

    private void Analyze(Node node, CancellationToken cancellationToken, string maskedSource)
    {
        var symbol = node.Symbol!;
        if (symbol.Kind == GameSymbolKind.Type)
        {
            var declaration = Regex.Match(symbol.Signature, @"\b(?:extends|implements)\s+(.+)").Groups[1].Value;
            foreach (Match match in Regex.Matches(declaration, @"[\w.$]+"))
                if (ResolveType(match.Value, node) is { } target && target != node)
                    Add(node, target, "Inherits / implements", match.Value, symbol.Line);
            return;
        }
        LinkType(node, symbol.ReturnType, "Declared type");
        if (symbol.Kind is not (GameSymbolKind.Method or GameSymbolKind.Constructor)) return;
        var text = maskedSource[node.Start..node.End];
        var lineStarts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++) if (text[i] == '\n') lineStarts.Add(i + 1);
        int LineAt(int offset)
        {
            var position = lineStarts.BinarySearch(offset);
            return symbol.Line + (position >= 0 ? position : ~position - 1);
        }
        var bodyStart = text.IndexOf('{');
        var variables = new Dictionary<string, List<Node>>(StringComparer.Ordinal);
        foreach (var field in node.Parent?.Children.Where(n => n.Kind == "Field") ?? [])
            variables[field.Symbol!.Name] = [field];
        void Variable(string name, string type, string kind, int offset)
        {
            var line = LineAt(Math.Min(offset, text.Length));
            var lineEnd = text.IndexOf('\n', offset);
            var variable = new Node { Label = $"{type} {name}", Kind = kind, Parent = node, TypeName = type,
                Symbol = new GameKnowledgeSymbol { Name = name, QualifiedName = symbol.QualifiedName + "." + name,
                    Signature = $"{type} {name}", RelativePath = symbol.RelativePath, Line = line, Package = symbol.Package },
                Start = node.Start + offset, End = node.Start + (lineEnd >= 0 ? lineEnd : text.Length) };
            node.Children.Add(variable); Nodes.Add(variable);
            if (!variables.TryGetValue(name, out var entries) || entries.All(n => n.Kind == "Field")) variables[name] = entries = new();
            entries.Add(variable);
            LinkType(variable, type, "Declared type");
        }
        foreach (var parameter in symbol.Parameters.Split(','))
        {
            var match = Regex.Match(parameter.Trim(), @"^(?:final\s+)?(?<type>[\w.$<>?\[\]]+)\s+(?<name>[\w$]+)$");
            if (match.Success) Variable(match.Groups["name"].Value, match.Groups["type"].Value, "Parameter", 0);
        }
        if (bodyStart < 0) return;
        var body = text[(bodyStart + 1)..];
        foreach (Match match in Regex.Matches(body, @"\b(?<type>[\w.$]+(?:<[^;={}()]+>)?(?:\[\])*)\s+(?<name>[\w$]+)\s*(?==|;|:|,)", RegexOptions.None))
        {
            if (match.Groups["type"].Value is "return" or "throw" or "break" or "continue" or "case") continue;
            Variable(match.Groups["name"].Value, match.Groups["type"].Value, "Local variable", bodyStart + 1 + match.Index);
        }
        foreach (Match match in Regex.Matches(body, @"(?:(?<new>\bnew)\s+)?(?:(?<receiver>[\w$]+(?:\.[\w$]+)*)\s*\.\s*)?(?<name>[A-Za-z_$][\w$]*)\s*\("))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = match.Groups["name"].Value;
            if (name is "if" or "for" or "while" or "switch" or "catch" or "synchronized" or "assert" or "this" or "super") continue;
            var receiver = match.Groups["receiver"].Value;
            var construct = match.Groups["new"].Success;
            var offset = bodyStart + 1 + match.Index;
            // A chained expression's receiver cannot be inferred by this source navigator.
            var previous = match.Index - 1;
            while (previous >= 0 && char.IsWhiteSpace(body[previous])) previous--;
            var chained = previous >= 0 && body[previous] == '.';
            Node? owner = null;
            if (!chained)
            {
                if (construct) owner = ResolveType(string.IsNullOrEmpty(receiver) ? name : receiver + "." + name, node);
                else if (receiver is "" or "this") owner = node.Parent;
                else if (variables.TryGetValue(receiver.StartsWith("this.") ? receiver[5..] : receiver, out var values))
                {
                    var usable = receiver.StartsWith("this.")
                        ? node.Parent?.Children.Where(n => n.Kind == "Field" && n.Symbol!.Name == receiver[5..]).ToList() ?? []
                        : values.Where(v => v.Kind != "Local variable" || v.Start <= node.Start + offset).ToList();
                    if (usable.Count == 1) owner = ResolveType(usable[0].TypeName, node);
                }
                else owner = ResolveType(receiver, node);
            }
            var arity = ArgumentCount(body, match.Index + match.Length - 1);
            var candidates = owner?.Children.Where(n => construct ? n.Kind == "Constructor" : n.Kind == "Method" && n.Symbol!.Name == name)
                .Where(n => ParameterCount(n.Symbol!.Parameters) == arity || n.Symbol.Parameters.Contains("...")).ToList() ?? [];
            var line = LineAt(offset);
            var description = (construct ? "new " : "") + (receiver.Length > 0 ? receiver + "." : "") + name + "(...)";
            if (candidates.Count == 0) Add(node, construct ? owner : null, construct && owner != null ? "Creates" : "Unresolved call", description, line);
            else foreach (var target in candidates) Add(node, target, candidates.Count > 1 ? "Possible call (overload)" : "Call (inferred)", description, line);
        }
        foreach (Match match in Regex.Matches(body, @"\b(?:this\.)?(?<name>[A-Za-z_$][\w$]*)\b"))
        {
            if (!variables.TryGetValue(match.Groups["name"].Value, out var values)) continue;
            var fields = match.Value.StartsWith("this.") ? node.Parent?.Children.Where(n => n.Kind == "Field" && n.Symbol!.Name == match.Groups["name"].Value).ToList() ?? [] : values;
            if (fields.Count == 1 && fields[0].Kind == "Field" && (match.Index == 0 || body[match.Index - 1] != '.'))
                Add(node, fields[0], "Uses field", fields[0].Label, LineAt(bodyStart + 1 + match.Index));
        }
    }

    private void LinkType(Node node, string type, string kind)
    {
        foreach (Match token in Regex.Matches(type, @"[\w.$]+"))
            if (ResolveType(token.Value, node) is { } target) Add(node, target, kind, token.Value, node.Symbol!.Line);
    }

    private Node? ResolveType(string name, Node context)
    {
        name = Regex.Replace(name, @"<.*>|\[\]|\.\.\.", "");
        if (_types.TryGetValue(name, out var exact)) return exact;
        for (var scope = context.Kind == "Type" ? context : context.Parent; scope != null; scope = scope.Parent)
            if (_types.TryGetValue(scope.Symbol!.QualifiedName + "." + name, out var nested)) return nested;
        var imports = _imports.GetValueOrDefault(context.Symbol!.RelativePath) ?? [];
        var explicitTypes = imports.Where(i => i.EndsWith("." + name, StringComparison.Ordinal)).Select(i => _types.GetValueOrDefault(i)).Where(n => n != null).Distinct().ToList();
        if (explicitTypes.Count > 0) return explicitTypes.Count == 1 ? explicitTypes[0] : null;
        if (_types.TryGetValue(context.Symbol.Package + "." + name, out var local)) return local;
        var wildcard = imports.Where(i => i.EndsWith(".*")).Select(i => _types.GetValueOrDefault(i[..^1] + name)).Where(n => n != null).Distinct().ToList();
        return wildcard.Count == 1 ? wildcard[0] : null;
    }

    private void Add(Node from, Node? to, string kind, string description, int line)
    {
        var edge = new Edge(from, to, kind, description, line);
        Edges.Add(edge);
        if (!_outgoing.TryGetValue(from, out var outgoing)) _outgoing[from] = outgoing = new();
        outgoing.Add(edge);
        if (to == null) return;
        if (!_incoming.TryGetValue(to, out var incoming)) _incoming[to] = incoming = new();
        incoming.Add(edge);
    }

    private static int ParameterCount(string parameters)
    {
        if (string.IsNullOrWhiteSpace(parameters)) return 0;
        var depth = 0; var count = 1;
        foreach (var c in parameters) { if (c == '<') depth++; if (c == '>') depth--; if (c == ',' && depth == 0) count++; }
        return count;
    }

    private static int ArgumentCount(string text, int open)
    {
        var depth = 0; var count = 0; var hasValue = false;
        for (var i = open + 1; i < text.Length; i++)
        {
            var c = text[i];
            if (c == ')' && depth == 0) return hasValue ? count + 1 : 0;
            if (c is '(' or '[' or '{') depth++;
            if (c is ')' or ']' or '}') depth--;
            if (c == ',' && depth == 0) count++;
            if (!char.IsWhiteSpace(c)) hasValue = true;
        }
        return -1;
    }

    private static int DeclarationEnd(string text, int start, GameSymbolKind kind)
    {
        var depth = 0;
        for (var i = start; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            if (text[i] == '}')
            {
                depth--;
                if (depth == 0 && kind != GameSymbolKind.Field) return i + 1;
            }
            if (text[i] == ';' && depth == 0) return i + 1;
        }
        return text.Length;
    }

    // Preserve offsets/newlines while hiding comments and literal contents.
    internal static string Mask(string text) => Regex.Replace(text,
        "//[^\\n]*|/\\*[\\s\\S]*?\\*/|\"\"\"[\\s\\S]*?\"\"\"|\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'",
        m => new string(m.Value.Select((c, i) => c == '\n' ? '\n' : i == 0 && (m.Value[0] is '\'' or '"') ? '0' : ' ').ToArray()));
}
