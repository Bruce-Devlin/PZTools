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
