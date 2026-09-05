using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using PZTools.Core.Models;

namespace PZTools.Core.Functions.Decompile
{
    internal static partial class GameKnowledgeBase
    {
        private const string IndexFileName = ".pztools-knowledge-index.json";

        [GeneratedRegex(@"^\s*package\s+(?<name>[\w.]+)\s*;")]
        private static partial Regex PackagePattern();

        [GeneratedRegex(@"(?:(?:public|protected|private|static|abstract|final|strictfp|sealed|non-sealed)\s+)*(?:class|interface|enum|record)\s+(?<name>[A-Za-z_$][\w$]*)")]
        private static partial Regex TypePattern();

        [GeneratedRegex(@"^(?<mods>(?:(?:public|protected|private|static|abstract|final|synchronized|native|strictfp|default)\s+)*)?(?:(?<return>[\w$.,<>?\[\] ]+)\s+)?(?<name>[A-Za-z_$][\w$]*)\s*\((?<params>[^)]*)\)\s*(?:throws\s+[^;{]+)?[;{]")]
        private static partial Regex MethodPattern();

        [GeneratedRegex(@"^(?<mods>(?:(?:public|protected|private|static|final|transient|volatile)\s+)+)(?<type>[\w$.,<>?\[\] ]+)\s+(?<name>[A-Za-z_$][\w$]*)\s*(?:=[^;]*)?;")]
        private static partial Regex FieldPattern();

        public static string GetIndexPath(string sourcePath) => Path.Combine(sourcePath, IndexFileName);

        public static IReadOnlyList<GameSourceBuild> DiscoverBuilds(string sourceRoot)
        {
            if (!Directory.Exists(sourceRoot))
                return Array.Empty<GameSourceBuild>();

            var builds = new List<GameSourceBuild>();
            if (Directory.Exists(Path.Combine(sourceRoot, "zombie")))
                builds.Add(new GameSourceBuild("Current game", sourceRoot));

            foreach (var directory in Directory.EnumerateDirectories(sourceRoot).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                if (Directory.Exists(Path.Combine(directory, "zombie")))
                    builds.Add(new GameSourceBuild(Path.GetFileName(directory), directory));
            }

            return builds;
        }

        public static async Task<GameKnowledgeIndex> LoadOrBuildAsync(
            string sourcePath,
            string build,
            bool force = false,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            sourcePath = Path.GetFullPath(sourcePath);
            if (!Directory.Exists(sourcePath))
                throw new DirectoryNotFoundException($"Decompiled source folder not found: {sourcePath}");

            var files = Directory.EnumerateFiles(sourcePath, "*.java", SearchOption.AllDirectories)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var fingerprint = CreateFingerprint(sourcePath, files);
            var indexPath = GetIndexPath(sourcePath);

            if (!force && File.Exists(indexPath))
            {
                try
                {
                    var existing = JsonConvert.DeserializeObject<GameKnowledgeIndex>(await File.ReadAllTextAsync(indexPath, cancellationToken));
                    if (existing?.FormatVersion == GameKnowledgeIndex.CurrentFormatVersion &&
                        existing.SourceFingerprint == fingerprint)
                    {
                        existing.SourceRoot = sourcePath;
                        existing.Build = build;
                        return existing;
                    }
                }
                catch (JsonException) { }
                catch (IOException) { }
            }

            progress?.Report($"Indexing {files.Count:N0} decompiled Java files...");
            var symbols = new List<GameKnowledgeSymbol>(Math.Max(files.Count * 8, 256));
            for (var i = 0; i < files.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (i % 100 == 0)
                    progress?.Report($"Indexing game code... {i:N0} / {files.Count:N0} files");

                var text = await File.ReadAllTextAsync(files[i], cancellationToken);
                ParseJavaFile(text, Path.GetRelativePath(sourcePath, files[i]), symbols);
            }

            var index = new GameKnowledgeIndex
            {
                Build = build,
                SourceRoot = sourcePath,
                SourceFingerprint = fingerprint,
                GeneratedAt = DateTimeOffset.Now,
                FileCount = files.Count,
                Symbols = symbols
                    .OrderBy(x => x.QualifiedName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.Line)
                    .ToList()
            };

            var json = JsonConvert.SerializeObject(index, Formatting.Indented);
            var temporaryPath = indexPath + ".new";
            await File.WriteAllTextAsync(temporaryPath, json, new UTF8Encoding(false), cancellationToken);
            File.Move(temporaryPath, indexPath, overwrite: true);
            progress?.Report($"Knowledge base ready: {index.Symbols.Count:N0} symbols indexed.");
            return index;
        }

        public static IReadOnlyList<GameKnowledgeSymbol> Search(
            GameKnowledgeIndex index,
            string? query,
            GameSymbolKind? kind = null,
            int limit = 500)
        {
            var terms = (query ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return index.Symbols
                .Where(x => kind is null || x.Kind == kind)
                .Select(x => (Symbol: x, Score: Score(x, terms)))
                .Where(x => x.Score >= 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Symbol.QualifiedName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Symbol.Line)
                .Take(Math.Clamp(limit, 1, 5000))
                .Select(x => x.Symbol)
                .ToList();
        }

        private static int Score(GameKnowledgeSymbol symbol, string[] terms)
        {
            if (terms.Length == 0)
                return 0;

            var score = 0;
            foreach (var term in terms)
            {
                if (!symbol.SearchText.Contains(term, StringComparison.OrdinalIgnoreCase))
                    return -1;
                if (symbol.Name.Equals(term, StringComparison.OrdinalIgnoreCase)) score += 100;
                else if (symbol.Name.StartsWith(term, StringComparison.OrdinalIgnoreCase)) score += 60;
                else if (symbol.Name.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 40;
                else if (symbol.QualifiedName.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 20;
                else score += 5;
            }
            return score;
        }

        private static void ParseJavaFile(string text, string relativePath, List<GameKnowledgeSymbol> output)
        {
            var lines = text.Replace("\r\n", "\n").Split('\n');
            var package = string.Empty;
            var braceDepth = 0;
            var types = new Stack<TypeScope>();
            var documentation = new StringBuilder();
            var inDoc = false;

            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                var original = lines[lineIndex];
                var trimmed = original.Trim();
                if (inDoc || trimmed.StartsWith("/**", StringComparison.Ordinal))
                {
                    inDoc = !trimmed.Contains("*/", StringComparison.Ordinal);
                    documentation.AppendLine(trimmed);
                    continue;
                }

                if (trimmed.Length == 0 || trimmed.StartsWith("@", StringComparison.Ordinal))
                    continue;

                var packageMatch = PackagePattern().Match(trimmed);
                if (packageMatch.Success)
                {
                    package = packageMatch.Groups["name"].Value;
                    documentation.Clear();
                    continue;
                }

                var code = StripLineComment(trimmed);
                var typeMatch = TypePattern().Match(code);
                if (typeMatch.Success)
                {
                    var name = typeMatch.Groups["name"].Value;
                    var declaring = types.Count == 0 ? string.Empty : types.Peek().QualifiedName;
                    var qualified = string.IsNullOrEmpty(declaring)
                        ? JoinName(package, name)
                        : declaring + "." + name;
                    output.Add(new GameKnowledgeSymbol
                    {
                        Kind = GameSymbolKind.Type,
                        Name = name,
                        QualifiedName = qualified,
                        DeclaringType = declaring,
                        Package = package,
                        Signature = CollapseWhitespace(code.TrimEnd('{', ' ')),
                        Modifiers = ReadLeadingModifiers(code),
                        Documentation = CleanDocumentation(documentation),
                        RelativePath = relativePath,
                        Line = lineIndex + 1
                    });
                    documentation.Clear();
                    var openingBraces = CountCodeCharacter(code, '{');
                    if (openingBraces > 0)
                        types.Push(new TypeScope(name, qualified, braceDepth + openingBraces));
                }
                else if (types.Count > 0 && braceDepth == types.Peek().BodyDepth)
                {
                    var methodMatch = MethodPattern().Match(code);
                    if (methodMatch.Success && !IsControlStatement(methodMatch.Groups["name"].Value))
                    {
                        var name = methodMatch.Groups["name"].Value;
                        var current = types.Peek();
                        var isConstructor = name == current.Name;
                        output.Add(new GameKnowledgeSymbol
                        {
                            Kind = isConstructor ? GameSymbolKind.Constructor : GameSymbolKind.Method,
                            Name = name,
                            QualifiedName = current.QualifiedName + "." + name,
                            DeclaringType = current.QualifiedName,
                            Package = package,
                            Signature = CollapseWhitespace(code.TrimEnd('{', ';', ' ')),
                            ReturnType = isConstructor ? string.Empty : CollapseWhitespace(methodMatch.Groups["return"].Value),
                            Parameters = CollapseWhitespace(methodMatch.Groups["params"].Value),
                            Modifiers = CollapseWhitespace(methodMatch.Groups["mods"].Value),
                            Documentation = CleanDocumentation(documentation),
                            RelativePath = relativePath,
                            Line = lineIndex + 1
                        });
                        documentation.Clear();
                    }
                    else
                    {
                        var fieldMatch = FieldPattern().Match(code);
                        if (fieldMatch.Success)
                        {
                            var name = fieldMatch.Groups["name"].Value;
                            var current = types.Peek();
                            output.Add(new GameKnowledgeSymbol
                            {
                                Kind = GameSymbolKind.Field,
                                Name = name,
                                QualifiedName = current.QualifiedName + "." + name,
                                DeclaringType = current.QualifiedName,
                                Package = package,
                                Signature = CollapseWhitespace(code.TrimEnd(';')),
                                ReturnType = CollapseWhitespace(fieldMatch.Groups["type"].Value),
                                Modifiers = CollapseWhitespace(fieldMatch.Groups["mods"].Value),
                                Documentation = CleanDocumentation(documentation),
                                RelativePath = relativePath,
                                Line = lineIndex + 1
                            });
                            documentation.Clear();
                        }
                        else if (!code.StartsWith("//", StringComparison.Ordinal))
                        {
                            documentation.Clear();
                        }
                    }
                }

                braceDepth += CountCodeCharacter(code, '{') - CountCodeCharacter(code, '}');
                while (types.Count > 0 && braceDepth < types.Peek().BodyDepth)
                    types.Pop();
            }
        }

        private static string CreateFingerprint(string sourcePath, IEnumerable<string> files)
        {
            using var sha = SHA256.Create();
            foreach (var file in files)
            {
                var info = new FileInfo(file);
                var value = $"{Path.GetRelativePath(sourcePath, file)}|{info.Length}|{info.LastWriteTimeUtc.Ticks}\n";
                var bytes = Encoding.UTF8.GetBytes(value);
                sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return Convert.ToHexString(sha.Hash!);
        }

        private static string CleanDocumentation(StringBuilder value)
        {
            if (value.Length == 0) return string.Empty;
            return string.Join(' ', value.ToString().Split('\n')
                .Select(x => x.Trim().TrimStart('/', '*').Trim())
                .Where(x => x.Length > 0 && !x.StartsWith('@')));
        }

        private static string StripLineComment(string value)
        {
            var index = value.IndexOf("//", StringComparison.Ordinal);
            return index < 0 ? value : value[..index].TrimEnd();
        }

        private static string CollapseWhitespace(string value) => Regex.Replace(value.Trim(), @"\s+", " ");
        private static int CountCodeCharacter(string value, char character)
        {
            var count = 0;
            var quote = '\0';
            var escaped = false;
            for (var i = 0; i < value.Length; i++)
            {
                var current = value[i];
                if (quote != '\0')
                {
                    if (escaped) escaped = false;
                    else if (current == '\\') escaped = true;
                    else if (current == quote) quote = '\0';
                    continue;
                }
                if (current is '\'' or '"')
                {
                    quote = current;
                    continue;
                }
                if (current == '/' && i + 1 < value.Length && value[i + 1] == '/')
                    break;
                if (current == character) count++;
            }
            return count;
        }
        private static string JoinName(string left, string right) => string.IsNullOrEmpty(left) ? right : left + "." + right;
        private static bool IsControlStatement(string name) => name is "if" or "for" or "while" or "switch" or "catch" or "return" or "new";
        private static string ReadLeadingModifiers(string value)
        {
            var known = new HashSet<string>(StringComparer.Ordinal) { "public", "protected", "private", "static", "abstract", "final", "strictfp", "sealed", "non-sealed" };
            return string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries).TakeWhile(known.Contains));
        }

        private sealed record TypeScope(string Name, string QualifiedName, int BodyDepth);
    }
}
