using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using PZTools.Core.Functions.Zomboid;
using PZTools.Core.Models;

namespace PZTools.Core.Functions.Projects
{
    public static class ProjectConflictAnalyzer
    {
        private const int MaxModInfoFiles = 5000;
        private const int MaxConflictFindings = 200;

        public static IReadOnlyList<ProjectDiagnostic> AnalyzeRequiredMods(
            ModProject project,
            CancellationToken cancellationToken = default)
        {
            var roots = ZomboidGame.GetModSearchRoots();
            var ignored = new[]
            {
                project.RootPath,
                Path.Combine(ZomboidGame.GameUserDirectory, "mods", project.Name),
                Path.Combine(ZomboidGame.GameUserDirectory, "Workshop", project.Name)
            };
            return AnalyzeRequiredMods(project, roots, ignored, cancellationToken);
        }

        public static IReadOnlyList<ProjectDiagnostic> AnalyzeRequiredMods(
            ModProject project,
            IEnumerable<string> searchRoots,
            IEnumerable<string>? ignoredModRoots = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(project);
            ArgumentNullException.ThrowIfNull(searchRoots);

            var requiredIds = project.ModInfo.Requires
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (requiredIds.Count == 0)
                return Array.Empty<ProjectDiagnostic>();

            var target = project.Targets.FirstOrDefault(x => x.IsPrimary) ??
                         project.Targets.OrderByDescending(x => x.Build).FirstOrDefault();
            if (target is null)
                return Array.Empty<ProjectDiagnostic>();

            var ignored = (ignoredModRoots ?? Array.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(SafeFullPath)
                .Where(x => x is not null)
                .Cast<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var stableBuild = ZomboidGame.latestStableBuild;
            var analysisBuild = Math.Truncate(stableBuild) == Math.Truncate(target.Build)
                ? Math.Max(stableBuild, target.Build)
                : target.Build;
            var installed = DiscoverInstalledMods(searchRoots, analysisBuild, ignored, cancellationToken)
                .Where(x => requiredIds.Contains(x.Info.Id))
                .ToList();
            var diagnostics = new List<ProjectDiagnostic>();

            foreach (var duplicate in installed.GroupBy(x => x.Info.Id, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1))
            {
                Add(diagnostics, DiagnosticSeverity.Warning, "PZC001",
                    $"Required mod '{duplicate.Key}' has {duplicate.Count()} installed copies.",
                    "Keep one intended copy for the playtest profile so the game cannot resolve an unexpected version.");
            }

            var projectFiles = CollectLoadFiles(GetProjectContentRoots(project, target), cancellationToken);
            var projectSymbols = CollectZedSymbols(GetProjectContentRoots(project, target), cancellationToken);

            foreach (var dependency in installed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CheckRelationshipConflicts(project, dependency, diagnostics);

                var dependencyFiles = CollectLoadFiles(dependency.ContentRoots, cancellationToken);
                foreach (var collision in projectFiles.Keys.Intersect(dependencyFiles.Keys, StringComparer.OrdinalIgnoreCase))
                {
                    if (diagnostics.Count >= MaxConflictFindings)
                        break;
                    Add(diagnostics, DiagnosticSeverity.Warning, "PZC010",
                        $"'{project.ModInfo.Id}' and required mod '{dependency.Info.Id}' both provide '{collision}'.",
                        "Confirm that this override is intentional and enforce the required load order; otherwise rename or remove one file.",
                        projectFiles[collision]);
                }

                var dependencySymbols = CollectZedSymbols(dependency.ContentRoots, cancellationToken);
                foreach (var collision in projectSymbols.Keys.Intersect(dependencySymbols.Keys, StringComparer.OrdinalIgnoreCase))
                {
                    if (diagnostics.Count >= MaxConflictFindings)
                        break;
                    Add(diagnostics, DiagnosticSeverity.Warning, "PZC011",
                        $"ZedScript definition '{collision}' is also declared by required mod '{dependency.Info.Id}'.",
                        "Give the definition a unique module/name or document and enforce the intended override order.",
                        projectSymbols[collision]);
                }

                if (diagnostics.Count >= MaxConflictFindings)
                    break;
            }

            if (diagnostics.Count >= MaxConflictFindings)
                Add(diagnostics, DiagnosticSeverity.Info, "PZC099",
                    $"Conflict analysis stopped after {MaxConflictFindings} findings.",
                    "Resolve the current collisions, then run project health again to reveal any remaining findings.");

            return diagnostics;
        }

        private static void CheckRelationshipConflicts(ModProject project, InstalledMod dependency, List<ProjectDiagnostic> diagnostics)
        {
            if (dependency.Info.Requires.Contains(project.ModInfo.Id, StringComparer.OrdinalIgnoreCase))
                Add(diagnostics, DiagnosticSeverity.Warning, "PZC020",
                    $"Direct dependency cycle: '{project.ModInfo.Id}' requires '{dependency.Info.Id}', which requires '{project.ModInfo.Id}'.",
                    "Remove the cycle or extract the shared API into a separate library mod.");

            if (dependency.Info.Incompatibles.Contains(project.ModInfo.Id, StringComparer.OrdinalIgnoreCase))
                Add(diagnostics, DiagnosticSeverity.Error, "PZC021",
                    $"Required mod '{dependency.Info.Id}' declares '{project.ModInfo.Id}' incompatible.",
                    "Do not launch this combination until one mod removes the incompatibility or a compatibility patch is selected.");

            var bothAfter = project.ModInfo.LoadModAfter.Contains(dependency.Info.Id, StringComparer.OrdinalIgnoreCase) &&
                            dependency.Info.LoadModAfter.Contains(project.ModInfo.Id, StringComparer.OrdinalIgnoreCase);
            var bothBefore = project.ModInfo.LoadModBefore.Contains(dependency.Info.Id, StringComparer.OrdinalIgnoreCase) &&
                             dependency.Info.LoadModBefore.Contains(project.ModInfo.Id, StringComparer.OrdinalIgnoreCase);
            if (bothAfter || bothBefore)
                Add(diagnostics, DiagnosticSeverity.Error, "PZC022",
                    $"Contradictory load-order rules exist between '{project.ModInfo.Id}' and '{dependency.Info.Id}'.",
                    "Choose one authoritative before/after relationship and remove the opposing declaration.");
        }

        private static List<InstalledMod> DiscoverInstalledMods(
            IEnumerable<string> searchRoots,
            double build,
            HashSet<string> ignored,
            CancellationToken cancellationToken)
        {
            var containers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var searchRoot in searchRoots.Where(Directory.Exists))
            {
                try
                {
                    foreach (var infoPath in Directory.EnumerateFiles(searchRoot, "mod.info", SearchOption.AllDirectories).Take(MaxModInfoFiles))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var contentRoot = Path.GetDirectoryName(infoPath)!;
                        var container = IsBuildFolder(Path.GetFileName(contentRoot), out _)
                            ? Directory.GetParent(contentRoot)?.FullName ?? contentRoot
                            : contentRoot;
                        var fullContainer = SafeFullPath(container);
                        if (fullContainer is not null && !ignored.Contains(fullContainer))
                            containers.Add(fullContainer);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { }
            }

            var mods = new List<InstalledMod>();
            foreach (var container in containers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var versionRoots = new List<(double Build, string Path)>();
                try
                {
                    versionRoots.AddRange(Directory.EnumerateDirectories(container)
                        .Select(path => (Path: path, Name: Path.GetFileName(path)))
                        .Where(x => File.Exists(Path.Combine(x.Path, "mod.info")) && IsBuildFolder(x.Name, out _))
                        .Select(x => (double.Parse(x.Name, CultureInfo.InvariantCulture), x.Path)));
                }
                catch { }

                var selectedRoot = versionRoots
                    .Where(x => x.Build <= build)
                    .OrderByDescending(x => x.Build)
                    .Select(x => x.Path)
                    .FirstOrDefault();
                if (selectedRoot is null && File.Exists(Path.Combine(container, "mod.info")))
                    selectedRoot = container;
                if (selectedRoot is null)
                    continue;

                var info = ModInfoParser.Load(selectedRoot);
                if (string.IsNullOrWhiteSpace(info.Id))
                    continue;
                var contentRoots = new List<string>();
                var common = Path.Combine(container, "common");
                if (Directory.Exists(common))
                    contentRoots.Add(common);
                contentRoots.Add(selectedRoot);
                mods.Add(new InstalledMod(container, info, contentRoots));
            }
            return mods;
        }

        private static IEnumerable<string> GetProjectContentRoots(ModProject project, ModTarget target)
        {
            var common = Path.Combine(project.RootPath, "common");
            if (Directory.Exists(common))
                yield return common;
            if (Directory.Exists(target.Path))
                yield return target.Path;
        }

        private static Dictionary<string, string> CollectLoadFiles(IEnumerable<string> contentRoots, CancellationToken cancellationToken)
        {
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in contentRoots)
            {
                var media = Path.Combine(root, "media");
                if (!Directory.Exists(media))
                    continue;
                try
                {
                    foreach (var file in Directory.EnumerateFiles(media, "*", SearchOption.AllDirectories))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                        files[relative] = file;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { }
            }
            return files;
        }

        private static Dictionary<string, string> CollectZedSymbols(IEnumerable<string> contentRoots, CancellationToken cancellationToken)
        {
            var symbols = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in contentRoots)
            {
                var scripts = Path.Combine(root, "media", "scripts");
                if (!Directory.Exists(scripts))
                    continue;
                foreach (var file in Directory.EnumerateFiles(scripts, "*.txt", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string source;
                    try
                    {
                        source = File.ReadAllText(file);
                    }
                    catch { continue; }
                    var module = Regex.Match(source, @"\bmodule\s+([A-Za-z_][\w.]*)", RegexOptions.IgnoreCase).Groups[1].Value;
                    foreach (Match match in Regex.Matches(source,
                                 @"\b(item|recipe|craftRecipe|buildable|fluid|vehicle|template|sound|animation|model)\s+([A-Za-z_][\w.]*)",
                                 RegexOptions.IgnoreCase))
                    {
                        var key = $"{module}:{match.Groups[1].Value}:{match.Groups[2].Value}";
                        symbols[key] = file;
                    }
                }
            }
            return symbols;
        }

        private static bool IsBuildFolder(string value, out double build)
            => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out build) && double.IsFinite(build) && build > 0;

        private static string? SafeFullPath(string path)
        {
            try
            {
                return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            }
            catch { return null; }
        }

        private static void Add(List<ProjectDiagnostic> diagnostics, DiagnosticSeverity severity, string code,
            string message, string recommendation, string? filePath = null)
            => diagnostics.Add(new ProjectDiagnostic
            {
                Severity = severity,
                Code = code,
                Target = "Mod conflicts",
                Message = message,
                Recommendation = recommendation,
                FilePath = filePath
            });

        private sealed record InstalledMod(string RootPath, ModInfo Info, IReadOnlyList<string> ContentRoots);
    }
}
