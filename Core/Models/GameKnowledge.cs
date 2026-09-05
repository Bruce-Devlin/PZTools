using Newtonsoft.Json;

namespace PZTools.Core.Models
{
    public enum GameSymbolKind
    {
        Type,
        Constructor,
        Method,
        Field
    }

    public sealed class GameKnowledgeSymbol
    {
        public GameSymbolKind Kind { get; set; }
        public string Name { get; set; } = string.Empty;
        public string QualifiedName { get; set; } = string.Empty;
        public string DeclaringType { get; set; } = string.Empty;
        public string Package { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
        public string ReturnType { get; set; } = string.Empty;
        public string Parameters { get; set; } = string.Empty;
        public string Modifiers { get; set; } = string.Empty;
        public string Documentation { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public int Line { get; set; }

        [JsonIgnore] public string KindLabel => Kind.ToString();
        [JsonIgnore] public string Location => $"{RelativePath}:{Line}";
        [JsonIgnore] public string SearchText => string.Join(' ', Name, QualifiedName, DeclaringType, Package,
            Signature, ReturnType, Parameters, Documentation);
    }

    public sealed class GameKnowledgeIndex
    {
        public const int CurrentFormatVersion = 1;

        public int FormatVersion { get; set; } = CurrentFormatVersion;
        public string Build { get; set; } = string.Empty;
        public string SourceRoot { get; set; } = string.Empty;
        public string SourceFingerprint { get; set; } = string.Empty;
        public DateTimeOffset GeneratedAt { get; set; }
        public int FileCount { get; set; }
        public List<GameKnowledgeSymbol> Symbols { get; set; } = new();

        [JsonIgnore] public int TypeCount => Symbols.Count(x => x.Kind == GameSymbolKind.Type);
        [JsonIgnore] public int MethodCount => Symbols.Count(x => x.Kind is GameSymbolKind.Method or GameSymbolKind.Constructor);
        [JsonIgnore] public int FieldCount => Symbols.Count(x => x.Kind == GameSymbolKind.Field);
    }

    public sealed record GameSourceBuild(string Name, string SourcePath);
}
