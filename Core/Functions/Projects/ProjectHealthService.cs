using System.Globalization;
using System.IO;
using PZTools.Core.Functions.Tester;
using PZTools.Core.Functions.Zomboid;
using PZTools.Core.Models;

namespace PZTools.Core.Functions.Projects
{
    public static class ProjectHealthService
    {
        public static async Task<ProjectHealthReport> AnalyzeAsync(
            ModProject project,
            bool validateLua = true,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(project);
            var report = new ProjectHealthReport { Project = project };

            if (project.Targets.Count == 0)
            {
                Add(report, DiagnosticSeverity.Error, "PZT001", "Project", "The project has no build targets.",
                    "Add at least one Project Zomboid build target.");
                return report;
            }

            var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var target in project.Targets.OrderByDescending(x => x.Build))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await AnalyzeTargetAsync(report, target, ids, validateLua, cancellationToken);
            }

            await AnalyzeCommonAsync(report, project, validateLua, cancellationToken);

            if (project.Targets.Select(x => x.Build).Distinct().Count() != project.Targets.Count)
                Add(report, DiagnosticSeverity.Error, "PZT002", "Project", "Duplicate build targets were found.",
                    "Remove the duplicate target before deploying.");

            if (string.IsNullOrWhiteSpace(project.ModInfo.Description))
                Add(report, DiagnosticSeverity.Warning, "PZT030", "Workshop", "The mod has no description.",
                    "Add a concise player-facing description in Project Settings.");

            if (project.ModInfo.Posters.Count == 0 && string.IsNullOrWhiteSpace(project.ModInfo.Icon))
                Add(report, DiagnosticSeverity.Warning, "PZT031", "Workshop", "No poster or icon is configured.",
                    "Add a poster in Project Settings before publishing to Steam Workshop.");

            CheckDependencies(report, project);
            report.Diagnostics.AddRange(ProjectConflictAnalyzer.AnalyzeRequiredMods(project, cancellationToken));

            report.Diagnostics.AddRange(GameLogAnalyzer.FindProjectIssues(project));

            return report;
        }

        private static async Task AnalyzeCommonAsync(ProjectHealthReport report, ModProject project, bool validateLua, CancellationToken cancellationToken)
        {
            var common = Path.Combine(project.RootPath, "common");
            if (!Directory.Exists(common))
                return;

            var files = Directory.EnumerateFiles(common, "*", SearchOption.AllDirectories).ToList();
            report.ContentBytes += files.Sum(x =>
            {
                try
                {
                    return new FileInfo(x).Length;
                }
                catch { return 0; }
            });
            var luaFiles = files.Where(x => Path.GetExtension(x).Equals(".lua", StringComparison.OrdinalIgnoreCase)).ToList();
            report.LuaFileCount += luaFiles.Count;
            report.ScriptFileCount += files.Count(x =>
                Path.GetExtension(x).Equals(".txt", StringComparison.OrdinalIgnoreCase) &&
                x.Contains($"{Path.DirectorySeparatorChar}media{Path.DirectorySeparatorChar}scripts{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

            foreach (var luaFile in luaFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!validateLua)
                    continue;
                var result = await LuaTester.TestFile(luaFile, logResult: false);
                if (!result.Ok)
                    Add(report, DiagnosticSeverity.Error, "PZT100", "Common",
                        string.IsNullOrWhiteSpace(result.Message) ? "Lua syntax validation failed." : result.Message,
                        "Open this file in your editor and correct the syntax error.", luaFile, result.Line);
            }
        }

        private static async Task AnalyzeTargetAsync(
            ProjectHealthReport report,
            ModTarget target,
            Dictionary<string, string> ids,
            bool validateLua,
            CancellationToken cancellationToken)
        {
            var label = target.BuildName;
            if (!Directory.Exists(target.Path))
            {
                Add(report, DiagnosticSeverity.Error, "PZT003", label, "The target folder does not exist.",
                    "Restore the target folder or remove this build target.", target.Path);
                return;
            }

            var infoPath = Path.Combine(target.Path, "mod.info");
            if (!File.Exists(infoPath))
            {
                Add(report, DiagnosticSeverity.Error, "PZT010", label, "Required mod.info is missing.",
                    "Open Project Settings and save the metadata to recreate it.", infoPath);
            }
            else
            {
                try
                {
                    var info = ModInfoParser.Load(target.Path);
                    if (string.IsNullOrWhiteSpace(info.Name))
                        Add(report, DiagnosticSeverity.Error, "PZT011", label, "mod.info has no name.", "Set the mod name in Project Settings.", infoPath);
                    if (string.IsNullOrWhiteSpace(info.Id))
                        Add(report, DiagnosticSeverity.Error, "PZT012", label, "mod.info has no id.", "Set a stable, unique mod ID in Project Settings.", infoPath);
                    else if (ids.TryGetValue(info.Id, out var prior) && !string.Equals(prior, label, StringComparison.OrdinalIgnoreCase))
                    {
                        // Matching IDs across build targets are expected.
                    }
                    else
                        ids[info.Id] = label;

                    CheckVersionRange(report, label, info, infoPath);
                    CheckAssets(report, label, target.Path, info, infoPath);
                }
                catch (Exception ex)
                {
                    Add(report, DiagnosticSeverity.Error, "PZT013", label, $"mod.info could not be read: {ex.Message}",
                        "Correct the metadata file or resave it through Project Settings.", infoPath);
                }
            }

            var mediaPath = Path.Combine(target.Path, "media");
            var commonMediaPath = Path.Combine(report.Project.RootPath, "common", "media");
            if (!Directory.Exists(mediaPath) && !Directory.Exists(commonMediaPath))
                Add(report, DiagnosticSeverity.Error, "PZT020", label, "The required media folder is missing.",
                    "Create media content in this build target or in the shared common folder.", mediaPath);

            var files = Directory.EnumerateFiles(target.Path, "*", SearchOption.AllDirectories)
                .Where(x => !IsGeneratedEditorFile(x, target.Path)).ToList();
            report.ContentBytes += files.Sum(x =>
            {
                try
                {
                    return new FileInfo(x).Length;
                }
                catch { return 0; }
            });

            var luaFiles = files.Where(x => Path.GetExtension(x).Equals(".lua", StringComparison.OrdinalIgnoreCase)).ToList();
            var scriptFiles = files.Where(x =>
                Path.GetExtension(x).Equals(".txt", StringComparison.OrdinalIgnoreCase) &&
                x.Contains($"{Path.DirectorySeparatorChar}media{Path.DirectorySeparatorChar}scripts{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)).ToList();
            report.LuaFileCount += luaFiles.Count;
            report.ScriptFileCount += scriptFiles.Count;

            foreach (var scriptFile in scriptFiles)
                report.Diagnostics.AddRange(PzContentValidator.ValidateZedScript(scriptFile, label, target.Build));

            if (target.Build >= 42)
            {
                foreach (var translationJson in files.Where(x =>
                    Path.GetExtension(x).Equals(".json", StringComparison.OrdinalIgnoreCase) &&
                    x.Contains($"{Path.DirectorySeparatorChar}Translate{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)))
                    report.Diagnostics.AddRange(PzContentValidator.ValidateTranslationJson(translationJson, label));

                foreach (var legacyTranslation in files.Where(x =>
                    Path.GetExtension(x).Equals(".txt", StringComparison.OrdinalIgnoreCase) &&
                    x.Contains($"{Path.DirectorySeparatorChar}Translate{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)))
                    Add(report, DiagnosticSeverity.Warning, "PZT023", label,
                        "Legacy .txt translation found in a Build 42 target.",
                        "Build 42.15+ uses category-named JSON translation files such as UI.json, IG_UI.json, and ItemName.json.", legacyTranslation);
            }

            var commonHasLoadableContent = Directory.Exists(commonMediaPath) &&
                Directory.EnumerateFiles(commonMediaPath, "*", SearchOption.AllDirectories)
                    .Any(x => Path.GetExtension(x).Equals(".lua", StringComparison.OrdinalIgnoreCase) ||
                              Path.GetExtension(x).Equals(".txt", StringComparison.OrdinalIgnoreCase));
            if (luaFiles.Count == 0 && scriptFiles.Count == 0 && !commonHasLoadableContent)
                Add(report, DiagnosticSeverity.Warning, "PZT021", label, "This target has no Lua or ZedScript content.",
                    "Add gameplay code under media/lua or definitions under media/scripts.");

            foreach (var luaFile in luaFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsInValidLuaScope(luaFile, target))
                    Add(report, DiagnosticSeverity.Warning, "PZT022", label,
                        "Lua file is outside media/lua/client, server, or shared.",
                        "Move it into the correct execution scope so the game can load it.", luaFile);

                if (!validateLua)
                    continue;
                var result = await LuaTester.TestFile(luaFile, logResult: false);
                if (!result.Ok)
                    Add(report, DiagnosticSeverity.Error, "PZT100", label,
                        string.IsNullOrWhiteSpace(result.Message) ? "Lua syntax validation failed." : result.Message,
                        "Open this file in your editor and correct the syntax error.", luaFile, result.Line);
            }
        }

        private static void CheckAssets(ProjectHealthReport report, string label, string root, ModInfo info, string infoPath)
        {
            foreach (var asset in info.Posters.Append(info.Icon).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>())
            {
                var fullPath = Path.GetFullPath(Path.Combine(root, asset));
                if (!fullPath.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    Add(report, DiagnosticSeverity.Error, "PZT014", label, $"Asset path escapes the mod folder: {asset}",
                        "Use an asset stored inside the target folder.", infoPath);
                else if (!File.Exists(fullPath))
                    Add(report, DiagnosticSeverity.Error, "PZT015", label, $"Referenced asset is missing: {asset}",
                        "Choose an existing local poster or icon in Project Settings.", infoPath);
            }
        }

        private static void CheckVersionRange(ProjectHealthReport report, string label, ModInfo info, string infoPath)
        {
            if (!TryVersion(info.VersionMin, out var min) || !TryVersion(info.VersionMax, out var max) ||
                min == null || max == null)
                return;
            if (min > max)
                Add(report, DiagnosticSeverity.Error, "PZT016", label, "versionMin is greater than versionMax.",
                    "Correct the supported build range in Project Settings.", infoPath);
        }

        private static bool TryVersion(string? value, out Version? version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(value))
                return true;
            var normalized = value.Trim();
            if (!normalized.Contains('.'))
                normalized += ".0";
            return Version.TryParse(normalized, out version);
        }

        private static void CheckDependencies(ProjectHealthReport report, ModProject project)
        {
            if (project.ModInfo.Requires.Count == 0)
                return;
            var target = project.Targets.FirstOrDefault(x => x.IsPrimary) ?? project.Targets.OrderByDescending(x => x.Build).FirstOrDefault();
            var build = target?.Build ?? ZomboidGame.latestStableBuild;
            var installed = ModDependencyService.Discover(build);
            var checkedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { project.ModInfo.Id };

            void Check(string rawId, string requiredBy)
            {
                var dependency = rawId.Trim().TrimStart('\\');
                if (dependency.Length == 0)
                    return;
                if (visiting.Contains(dependency))
                {
                    Add(report, DiagnosticSeverity.Error, "PZT034", "Dependencies",
                        $"Dependency cycle detected: '{requiredBy}' requires '{dependency}'.",
                        "Remove the circular require declarations before testing this mod set.");
                    return;
                }
                if (!checkedIds.Add(dependency))
                    return;

                var matches = installed.Where(x => x.ModId.Equals(dependency, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matches.Count == 0)
                {
                    Add(report, DiagnosticSeverity.Warning, "PZT032", "Dependencies",
                        $"Required mod '{dependency}' (required by '{requiredBy}') was not found in local Mods or Workshop content.",
                        "Install the dependency before runtime testing, or remove it from Project Settings if it is no longer required.");
                    return;
                }
                if (matches.Count > 1)
                    Add(report, DiagnosticSeverity.Warning, "PZT033", "Dependencies",
                        $"Required mod '{dependency}' has {matches.Count} installed copies.",
                        "Choose the intended copy in Playtest Lab and remove or disable stale duplicates.");

                var selected = ModDependencyService.Resolve(dependency, matches)!;
                if (!SupportsBuild(selected.Info, build))
                    Add(report, DiagnosticSeverity.Error, "PZT035", "Dependencies",
                        $"Required mod '{dependency}' declares game compatibility {FormatRange(selected.Info)}, not Build {build:0.##}.",
                        "Install a compatible dependency version or adjust its metadata only if the mod has been verified on this build.");

                visiting.Add(dependency);
                foreach (var transitive in selected.Info.Requires)
                    Check(transitive, dependency);
                visiting.Remove(dependency);
            }

            foreach (var dependency in project.ModInfo.Requires)
                Check(dependency, project.ModInfo.Id);

            static bool SupportsBuild(ModInfo info, double build)
            {
                var version = Version.Parse(build.ToString("0.################", CultureInfo.InvariantCulture));
                return (!TryVersion(info.VersionMin, out var min) || min is null || version >= min) &&
                       (!TryVersion(info.VersionMax, out var max) || max is null || version <= max);
            }

            static string FormatRange(ModInfo info)
                => $"{info.VersionMin ?? "any"} to {info.VersionMax ?? "any"}";
        }

        private static bool IsInValidLuaScope(string path, ModTarget target)
        {
            var relative = Path.GetRelativePath(target.Path, path);
            if (target.Build >= 42 && relative.Equals(
                    Path.Combine("media", "registries.lua"), StringComparison.OrdinalIgnoreCase))
                return true;

            var normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            return new[] { "client", "server", "shared" }.Any(scope =>
                normalized.Contains($"{Path.DirectorySeparatorChar}media{Path.DirectorySeparatorChar}lua{Path.DirectorySeparatorChar}{scope}{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsGeneratedEditorFile(string file, string root)
        {
            var relative = Path.GetRelativePath(root, file);
            return relative.StartsWith(".vscode" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                   relative.Equals(".luarc.json", StringComparison.OrdinalIgnoreCase) ||
                   relative.EndsWith(".code-workspace", StringComparison.OrdinalIgnoreCase);
        }

        private static void Add(ProjectHealthReport report, DiagnosticSeverity severity, string code, string target,
            string message, string recommendation, string? filePath = null, int? line = null)
            => report.Diagnostics.Add(new ProjectDiagnostic
            {
                Severity = severity,
                Code = code,
                Target = target,
                Message = message,
                Recommendation = recommendation,
                FilePath = filePath,
                Line = line
            });
    }
}
