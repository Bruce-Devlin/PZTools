using Newtonsoft.Json;
using PZTools.Core.Functions.Decompile;
using PZTools.Core.Models;

var root = Path.Combine(Path.GetTempPath(), "PZTools-Knowledge-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var gameDirectory = Path.Combine(root, "zombie", "characters");
    Directory.CreateDirectory(gameDirectory);
    var source = """
        /* Decompiled with CFR. A misleading class Fake { comment. */
        package zombie.characters;
        public class IsoPlayer
        extends IsoLivingCharacter
        implements IHumanVisual {
            private String url = "https://example.invalid/{";
            /* class Bogus {
               } */
            public float getHealth() {
                return 100;
            }
            public static class Nested
            implements Runnable {
                public void run() {
                }
            }
            public void update() {
            }
        }
        """;
    File.WriteAllText(Path.Combine(gameDirectory, "IsoPlayer.java"), source);
    var libraryDirectory = Path.Combine(root, "fmod");
    Directory.CreateDirectory(libraryDirectory);
    File.WriteAllText(Path.Combine(libraryDirectory, "Sound.java"), "package fmod;\npublic class Sound {\npublic void getHealth() {\n}\n}");
    var index = await GameKnowledgeBase.LoadOrBuildAsync(root, "fixture");
    Expect(index.TypeCount == 3 && index.MethodCount == 4 && index.FieldCount == 1, "CFR wrapped types, nested scopes, comments, and URL literals parse correctly");
    var player = index.Symbols.Single(x => x.QualifiedName == "zombie.characters.IsoPlayer");
    Expect(player.Line == 3 && player.Signature.Contains("implements IHumanVisual"), "wrapped type retains complete declaration and first source line");
    var health = GameKnowledgeBase.Search(index, "getHealth", GameSymbolKind.Method).Single();
    Expect(health.DeclaringType == player.QualifiedName && source.Split('\n')[health.Line - 1].Contains("getHealth"), "game method has correct owner and source line");
    Expect(index.Symbols.Single(x => x.Name == "update").DeclaringType == player.QualifiedName, "nested type closes before outer methods");
    Expect(GameKnowledgeBase.Search(index, "getHealth", includeLibraries: true).Count == 2, "libraries remain explicitly searchable");
    Expect(GameKnowledgeBase.Search(index, "fmod").Count == 0, "default search excludes library packages");
    var cached = await GameKnowledgeBase.LoadOrBuildAsync(root, "fixture");
    Expect(cached.GeneratedAt == index.GeneratedAt, "unchanged cache is reused");
    cached.FormatVersion = 1;
    cached.Symbols.Clear();
    File.WriteAllText(GameKnowledgeBase.GetIndexPath(root), JsonConvert.SerializeObject(cached));
    var upgraded = await GameKnowledgeBase.LoadOrBuildAsync(root, "fixture");
    Expect(upgraded.FormatVersion == 2 && upgraded.Symbols.Count == index.Symbols.Count, "old parser cache rebuilds automatically");
    index.Symbols.AddRange(Enumerable.Range(0, 600).Select(i => new GameKnowledgeSymbol
    {
        Package = "astar", QualifiedName = $"astar.A{i}", Name = $"A{i}", Kind = GameSymbolKind.Type
    }));
    Expect(GameKnowledgeBase.Search(index, "").All(x => x.IsGameCode), "library results cannot crowd game code out of the 500-result limit");
    index.Symbols.AddRange(new[]
    {
        new GameKnowledgeSymbol { Package = "zombie.future", Name = "futureMethod", Modifiers = "public", Kind = GameSymbolKind.Method },
        new GameKnowledgeSymbol { Package = "zombie.future", Name = "internalMethod", Modifiers = "private", Kind = GameSymbolKind.Method },
        new GameKnowledgeSymbol { Package = "zombie.future", Name = "lambda$future$0", Modifiers = "public static", Kind = GameSymbolKind.Method }
    });
    Expect(GameKnowledgeBase.Search(index, "futureMethod").Count == 1, "new game packages and method names are discovered without a whitelist");
    Expect(GameKnowledgeBase.Search(index, "internalMethod").Count == 0 &&
        GameKnowledgeBase.Search(index, "lambda$future$0").Count == 0, "internal and compiler-generated helpers are hidden by default");
    Expect(GameKnowledgeBase.Search(index, "internalMethod", includeInternals: true).Count == 1 &&
        GameKnowledgeBase.Search(index, "lambda$future$0", includeInternals: true).Count == 1, "internal declarations remain available on demand");

    var graphDirectory = Path.Combine(root, "zombie", "graph");
    Directory.CreateDirectory(graphDirectory);
    File.WriteAllText(Path.Combine(graphDirectory, "Actor.java"), """
        package zombie.graph;
        public class Actor {
            private Service service;
            public void tick(Service input) {
                Service local = new Service();
                service.run();
                input.run();
                local.run();
                this.service.run();
                service.overload(1);
                service.run("literal, text");
                unknown.run();
                service.factory().run();
                // service.fake();
                String message = "service.fake()";
                tick(input);
            }
            public class Inner {
                public void inside() {
                }
            }
        }
        """);
    File.WriteAllText(Path.Combine(graphDirectory, "Service.java"), """
        package zombie.graph;
        public class Service {
            public Service() {
            }
            public void run() {
            }
            public void run(String text) {
            }
            public void overload(int value) {
            }
            public void overload(String value) {
            }
            public Service factory() {
                return this;
            }
        }
        """);
    var graphIndex = await GameKnowledgeBase.LoadOrBuildAsync(root, "graph", true);
    var graph = await GameKnowledgeGraph.BuildAsync(graphIndex);
    var actor = graph.Types.Single(n => n.Symbol!.QualifiedName == "zombie.graph.Actor");
    var tick = actor.Children.Single(n => n.Symbol!.Name == "tick");
    Expect(actor.Children.Any(n => n.Kind == "Field") && actor.Children.Any(n => n.Kind == "Type"), "tree retains private fields and nested classes");
    Expect(tick.Children.Any(n => n.Kind == "Parameter" && n.Symbol!.Name == "input") && tick.Children.Any(n => n.Kind == "Local variable" && n.Symbol!.Name == "local"), "method tree includes parameters and local objects");
    var calls = graph.Outgoing(tick);
    Expect(calls.Count(e => e.To?.Symbol?.Name == "run" && e.To.Symbol.Parameters == "") == 4, "calls resolve field, parameter, local and explicit this receivers");
    Expect(calls.Count(e => e.Kind == "Possible call (overload)") == 2, "same arity overloads remain possible targets");
    Expect(calls.Any(e => e.To?.Symbol?.Parameters == "String text"), "string literal argument preserves call arity");
    Expect(calls.Count(e => e.Kind == "Unresolved call") == 2, "unknown and chained receivers remain unresolved");
    Expect(calls.All(e => !e.Description.Contains("fake")), "comments and string contents cannot introduce calls");
    Expect(calls.Any(e => e.To == tick) && graph.Incoming(tick).Any(e => e.From == tick), "recursive calls and reverse navigation are retained without recursive expansion");
    Expect(graph.Source(actor).Contains("class Inner") && graph.Source(tick).TrimEnd().EndsWith('}') && !graph.Source(tick).Contains("class Inner"), "source preview contains the complete selected object or method only");
    var live = new GameKnowledgeGraph();
    var priority = new System.Collections.Concurrent.ConcurrentQueue<string>();
    var publications = 0;
    GameKnowledgeGraph.Node? liveService = null;
    var sawPartialCalls = false;
    var streamed = await GameKnowledgeGraph.BuildAsync(graphIndex, publish: update =>
    {
        live.Apply(update);
        publications++;
        if (publications == 1)
        {
            Expect(live.Nodes.Count == graphIndex.Symbols.Count && live.Completed == 0 && live.Edges.Count == 0,
                "all classes and members are published before source inference starts");
            liveService = live.Types.Single(n => n.Symbol!.QualifiedName == "zombie.graph.Service");
            Expect(live.Source(liveService).Contains("loading"), "pending source is labelled as loading");
            priority.Enqueue(liveService.Symbol!.RelativePath);
        }
        if (live.Completed > 0 && !live.Finished)
        {
            Expect(liveService!.Analyzed, "selected file is inferred ahead of the background queue");
            if (live.Edges.Count > 0) sawPartialCalls = true;
        }
        Expect(ReferenceEquals(liveService, live.Types.Single(n => n.Symbol!.QualifiedName == "zombie.graph.Service")),
            "stream updates preserve node identity for selection and history");
        return Task.CompletedTask;
    }, priorityFiles: priority);
    Expect(publications >= 3 && sawPartialCalls && live.Finished && live.Completed == live.Total,
        "partial relationships are visible before completion with final progress");
    Expect(live.Edges.Count == streamed.Edges.Count && live.Nodes.Count == streamed.Nodes.Count,
        "streamed graph contains every final node and relationship without duplicates");
    Expect(live.Outgoing(live.Nodes.Single(n => n.Symbol?.QualifiedName == "zombie.graph.Actor.tick")).Count == calls.Count,
        "incremental forward and reverse indexes match full inference");
    using (var duringPublish = new CancellationTokenSource())
    {
        var updates = 0;
        try
        {
            await GameKnowledgeGraph.BuildAsync(graphIndex, cancellationToken: duringPublish.Token, publish: update =>
            { updates++; duringPublish.Cancel(); return Task.CompletedTask; });
            throw new Exception("Cancellation after initial publication was ignored");
        }
        catch (OperationCanceledException) { Expect(updates == 1, "closing after the initial tree cancels further publications"); }
    }
    using (var canceled = new CancellationTokenSource())
    {
        canceled.Cancel();
        try { await GameKnowledgeGraph.BuildAsync(graphIndex, cancellationToken: canceled.Token); throw new Exception("Cancellation was ignored"); }
        catch (OperationCanceledException) { Console.WriteLine("PASS graph construction honors cancellation"); }
    }

    if (args.Length > 0)
    {
        var real = await GameKnowledgeBase.LoadOrBuildAsync(args[0], "Current game");
        foreach (var name in new[] { "zombie.characters.IsoPlayer", "zombie.characters.IsoZombie", "zombie.characters.IsoGameCharacter" })
        {
            var type = real.Symbols.Single(x => x.Kind == GameSymbolKind.Type && x.QualifiedName == name);
            var methods = real.Symbols.Where(x => x.Kind == GameSymbolKind.Method && x.DeclaringType == name).ToList();
            Expect(methods.Count > 0, $"real {name}: {methods.Count} methods");
            var lines = File.ReadAllLines(Path.Combine(real.SourceRoot, type.RelativePath));
            Expect(methods.All(x => lines[x.Line - 1].Contains(x.Name + "(")), $"real {name} method source links verified");
        }
        Console.WriteLine($"Real index: {real.FileCount:N0} files, {real.Symbols.Count:N0} symbols, {real.Symbols.Count(x => x.IsGameCode):N0} zombie symbols.");
    }
    Console.WriteLine("Knowledge smoke checks passed.");
}
finally
{
    Directory.Delete(root, recursive: true);
}

static void Expect(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    Console.WriteLine("PASS " + message);
}
