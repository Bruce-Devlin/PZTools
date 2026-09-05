using System.Globalization;
using System.IO;
using PZTools.Core.Functions.Zomboid;
using PZTools.Core.Models;

namespace PZTools.Core.Functions.Projects
{
    public static class ModImportService
    {
        private static readonly string[] ModInfoNames = { "mod.info", "modinfo", "mod-info", "mod.info.txt", "modinfo.txt" };
        private static readonly HashSet<string> MediaFolders = new(StringComparer.OrdinalIgnoreCase)
        {
            "lua", "scripts", "textures", "ui", "sound", "maps", "models", "fonts", "radio"
        };

        public static ModImportInspection Inspect(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
                throw new ArgumentException("Choose the folder that contains the existing mod.", nameof(sourcePath));

            var source = Path.GetFullPath(sourcePath);
            if (!Directory.Exists(source))
                throw new DirectoryNotFoundException($"The import folder does not exist: {source}");

            var issues = new List<ModImportIssue>();
            var contentRoot = FindContentRoot(source, issues);
            var infoFile = FindModInfo(contentRoot);
            var info = infoFile == null ? new ModInfo() : LoadInfoFile(infoFile);
            var suggested = FirstUsableProjectName(info.Name, info.Id, Path.GetFileName(contentRoot));

            if (infoFile == null)
                issues.Add(Warning("No mod.info was found. PZTools will create one with a generated name and ID."));

            var inspection = new ModImportInspection
            {
                SourcePath = source,
                ContentRootPath = contentRoot,
                SuggestedProjectName = suggested
            };
            inspection.Issues.AddRange(issues);
            return inspection;
        }

        public static ModImportResult Import(string sourcePath, string projectName)
        {
            var inspection = Inspect(sourcePath);
            var safeName = ProjectEngine.ValidateProjectName(projectName);
            var projectsRoot = Path.GetFullPath(ProjectEngine.ProjectsRootPath);
            var destination = Path.GetFullPath(Path.Combine(projectsRoot, safeName));
            EnsureInside(destination, projectsRoot);

            if (Directory.Exists(destination))
                throw new InvalidOperationException($"Project '{safeName}' already exists in the workspace.");

            Directory.CreateDirectory(projectsRoot);
            var issues = new List<ModImportIssue>(inspection.Issues);

            try
            {
                var versionDirectories = GetVersionDirectories(inspection.ContentRootPath);
                var sourceCommon = FindDirectory(inspection.ContentRootPath, "common");
                if (versionDirectories.Count > 0 || sourceCommon != null)
                {
                    CopyDirectory(inspection.ContentRootPath, destination, issues);
                    foreach (var version in GetVersionDirectories(destination))
                        NormalizeTarget(version.Path, safeName, issues);

                    var common = FindDirectory(destination, "common");
                    if (common != null)
                    {
                        NormalizeDirectoryName(common, "common", issues);
                        NormalizeSharedFolder(Path.Combine(destination, "common"), issues);
                    }
                    if (versionDirectories.Count == 0)
                    {
                        var build = InferBuild(inspection.ContentRootPath, issues);
                        var target = Path.Combine(destination, build.ToString("0.##", CultureInfo.InvariantCulture));
                        ProjectEngine.CreateModFolderStructure(target, foldersOnly: true);
                        issues.Add(Info($"Created a Build {build:0.##} target for the imported common content."));
                    }
                }
                else
                {
                    var build = InferBuild(inspection.ContentRootPath, issues);
                    var target = Path.Combine(destination, build.ToString("0.##", CultureInfo.InvariantCulture));
                    Directory.CreateDirectory(destination);
                    CopyDirectory(inspection.ContentRootPath, target, issues);
                    NormalizeTarget(target, safeName, issues);
                    issues.Add(Info($"Converted the imported flat mod into a Build {build:0.##} workspace target."));
                }

                EnsureGitIgnore(destination);
                EnsureTargetMetadata(destination, safeName, issues);

                var project = ProjectEngine.LoadProjects()
                    .FirstOrDefault(x => Path.GetFullPath(x.RootPath).Equals(destination, StringComparison.OrdinalIgnoreCase));
                if (project == null)
                    throw new InvalidOperationException("The imported files could not be recognized as a PZTools project.");

                var result = new ModImportResult { Project = project };
                result.Issues.AddRange(issues);
                return result;
            }
            catch
            {
                if (Directory.Exists(destination))
                    Directory.Delete(destination, recursive: true);
                throw;
            }
        }

        private static string FindContentRoot(string source, List<ModImportIssue> issues)
        {
            var workshopMods = Path.Combine(source, "Contents", "mods");
            if (Directory.Exists(workshopMods))
            {
                var candidates = Directory.GetDirectories(workshopMods).Where(IsModLike).ToList();
                if (candidates.Count == 1)
                {
                    issues.Add(Info("Detected and unwrapped a Steam Workshop Contents/mods package."));
                    return candidates[0];
                }
                if (candidates.Count > 1)
                    throw new InvalidOperationException("This Workshop package contains multiple mods. Select the individual folder under Contents\\mods to import one mod at a time.");
            }

            if (IsModLike(source))
                return source;

            var children = Directory.GetDirectories(source).Where(IsModLike).ToList();
            if (children.Count == 1)
            {
                issues.Add(Info($"Used the nested mod folder '{Path.GetFileName(children[0])}'."));
                return children[0];
            }

            if (children.Count > 1)
                throw new InvalidOperationException("Several possible mods were found. Select the folder for the single mod you want to import.");

            // An incomplete mod may contain only loose source files. It is still importable and will be flagged.
            return source;
        }

        private static bool IsModLike(string path)
            => FindModInfo(path) != null || FindDirectory(path, "media") != null || FindDirectory(path, "common") != null ||
               GetVersionDirectories(path).Count > 0;

        private static List<(double Build, string Path)> GetVersionDirectories(string root)
        {
            if (!Directory.Exists(root))
                return new();
            return Directory.GetDirectories(root)
                .Select(path => (Name: Path.GetFileName(path), Path: path))
                .Where(x => double.TryParse(x.Name, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                .Select(x => (double.Parse(x.Name, CultureInfo.InvariantCulture), x.Path))
                .Where(x => FindModInfo(x.Path) != null || FindDirectory(x.Path, "media") != null)
                .ToList();
        }

        private static double InferBuild(string root, List<ModImportIssue> issues)
        {
            var infoFile = FindModInfo(root);
            if (infoFile != null)
            {
                var info = LoadInfoFile(infoFile);
                if (double.TryParse(info.VersionMin, NumberStyles.Float, CultureInfo.InvariantCulture, out var minimum) && minimum > 0)
                    return Math.Truncate(minimum);
            }

            var stable = Math.Truncate(ZomboidGame.latestStableBuild);
            issues.Add(Warning($"No build folder or usable versionMin was found; assumed the current stable Build {stable:0.##}. Review this target after import."));
            return stable;
        }

        private static void NormalizeTarget(string target, string projectName, List<ModImportIssue> issues)
        {
            var media = FindDirectory(target, "media");
            if (media != null)
            {
                NormalizeDirectoryName(media, "media", issues);
                media = Path.Combine(target, "media");
            }
            else
            {
                media = Path.Combine(target, "media");
                Directory.CreateDirectory(media);
                issues.Add(Info("Created the missing media folder."));
            }

            foreach (var loose in Directory.GetDirectories(target)
                         .Where(x => MediaFolders.Contains(Path.GetFileName(x)))
                         .Where(x => !Path.GetFileName(x).Equals("media", StringComparison.OrdinalIgnoreCase))
                         .ToList())
            {
                var canonical = Path.Combine(media, Path.GetFileName(loose).ToLowerInvariant());
                if (Directory.Exists(canonical))
                {
                    issues.Add(Warning($"Left '{Path.GetFileName(loose)}' at the target root because media/{Path.GetFileName(loose)} already exists; merge these folders manually."));
                    continue;
                }
                Directory.Move(loose, canonical);
                issues.Add(Info($"Moved the misplaced '{Path.GetFileName(loose)}' folder under media."));
            }

            var infoFiles = Directory.GetFiles(target)
                .Where(x => ModInfoNames.Contains(Path.GetFileName(x), StringComparer.OrdinalIgnoreCase))
                .ToList();
            var canonicalInfo = Path.Combine(target, "mod.info");
            if (infoFiles.Count > 0)
            {
                var chosen = infoFiles.FirstOrDefault(x => Path.GetFileName(x).Equals("mod.info", StringComparison.OrdinalIgnoreCase)) ?? infoFiles[0];
                NormalizeFileName(chosen, canonicalInfo, issues);
                if (infoFiles.Count > 1)
                    issues.Add(Warning("Multiple mod.info-like files were found. PZTools used mod.info; review the remaining metadata files."));
            }

            ProjectEngine.CreateModFolderStructure(target, foldersOnly: true);
        }

        private static void NormalizeSharedFolder(string common, List<ModImportIssue> issues)
        {
            var media = FindDirectory(common, "media");
            if (media != null)
            {
                NormalizeDirectoryName(media, "media", issues);
                return;
            }

            var loose = Directory.GetDirectories(common)
                .Where(x => MediaFolders.Contains(Path.GetFileName(x)))
                .ToList();
            if (loose.Count == 0)
                return;

            media = Path.Combine(common, "media");
            Directory.CreateDirectory(media);
            foreach (var folder in loose)
            {
                Directory.Move(folder, Path.Combine(media, Path.GetFileName(folder).ToLowerInvariant()));
                issues.Add(Info($"Moved misplaced common/{Path.GetFileName(folder)} under common/media."));
            }
        }

        private static void EnsureTargetMetadata(string projectRoot, string projectName, List<ModImportIssue> issues)
        {
            var versions = GetVersionDirectories(projectRoot);
            var rootInfo = FindModInfo(projectRoot);
            var common = FindDirectory(projectRoot, "common");
            var commonInfo = common == null ? null : FindModInfo(common);
            ModInfo? fallback = rootInfo != null ? LoadInfoFile(rootInfo) : commonInfo != null ? LoadInfoFile(commonInfo) : null;

            foreach (var version in versions)
            {
                var infoPath = FindModInfo(version.Path);
                var info = infoPath != null ? LoadInfoFile(infoPath) : CloneInfo(fallback);
                var changed = false;
                if (string.IsNullOrWhiteSpace(info.Name))
                {
                    info.Name = projectName;
                    changed = true;
                    issues.Add(Warning($"Build {version.Build:0.##} metadata had no name; generated '{projectName}'."));
                }
                if (string.IsNullOrWhiteSpace(info.Id))
                {
                    info.Id = ModInfoUtil.NormalizeModId(projectName);
                    changed = true;
                    issues.Add(Warning($"Build {version.Build:0.##} metadata had no ID; generated '{info.Id}'. Confirm it matches the ID used by existing saves and servers."));
                }
                if (infoPath == null)
                {
                    changed = true;
                    issues.Add(Warning($"Build {version.Build:0.##} had no mod.info; created one from available metadata."));
                }
                if (changed)
                    ModInfoParser.Save(version.Path, info);
            }
        }

        private static ModInfo CloneInfo(ModInfo? source)
        {
            if (source == null)
                return new ModInfo();
            var clone = new ModInfo
            {
                Name = source.Name,
                Id = source.Id,
                Author = source.Author,
                Description = source.Description,
                Url = source.Url,
                Icon = source.Icon,
                ModVersion = source.ModVersion,
                Category = source.Category,
                VersionMin = source.VersionMin,
                VersionMax = source.VersionMax
            };
            clone.Posters.AddRange(source.Posters);
            clone.Requires.AddRange(source.Requires);
            clone.Incompatibles.AddRange(source.Incompatibles);
            clone.LoadModAfter.AddRange(source.LoadModAfter);
            clone.LoadModBefore.AddRange(source.LoadModBefore);
            clone.Packs.AddRange(source.Packs);
            clone.Tiledefs.AddRange(source.Tiledefs);
            clone.AdditionalEntries.AddRange(source.AdditionalEntries);
            return clone;
        }

        private static ModInfo LoadInfoFile(string path)
        {
            if (Path.GetFileName(path).Equals("mod.info", StringComparison.OrdinalIgnoreCase))
                return ModInfoParser.Load(Path.GetDirectoryName(path)!);

            var temporary = Path.Combine(Path.GetTempPath(), "PZTools-import-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);
            try
            {
                File.Copy(path, Path.Combine(temporary, "mod.info"));
                return ModInfoParser.Load(temporary);
            }
            finally { Directory.Delete(temporary, recursive: true); }
        }

        private static string? FindModInfo(string folder)
            => Directory.Exists(folder)
                ? Directory.GetFiles(folder).FirstOrDefault(x => ModInfoNames.Contains(Path.GetFileName(x), StringComparer.OrdinalIgnoreCase))
                : null;

        private static string? FindDirectory(string folder, string name)
            => Directory.Exists(folder)
                ? Directory.GetDirectories(folder).FirstOrDefault(x => Path.GetFileName(x).Equals(name, StringComparison.OrdinalIgnoreCase))
                : null;

        private static void NormalizeDirectoryName(string existing, string canonicalName, List<ModImportIssue> issues)
        {
            var expected = Path.Combine(Path.GetDirectoryName(existing)!, canonicalName);
            if (Path.GetFileName(existing) == canonicalName)
                return;
            RenameCaseSafely(existing, expected, isDirectory: true);
            issues.Add(Info($"Renamed folder '{Path.GetFileName(existing)}' to '{canonicalName}'."));
        }

        private static void NormalizeFileName(string existing, string expected, List<ModImportIssue> issues)
        {
            if (Path.GetFileName(existing) == Path.GetFileName(expected))
                return;
            RenameCaseSafely(existing, expected, isDirectory: false);
            issues.Add(Info($"Renamed metadata file '{Path.GetFileName(existing)}' to 'mod.info'."));
        }

        private static void RenameCaseSafely(string source, string destination, bool isDirectory)
        {
            if (Path.GetFullPath(source).Equals(Path.GetFullPath(destination), StringComparison.Ordinal))
                return;
            var temporary = destination + ".pztools-rename-" + Guid.NewGuid().ToString("N");
            if (isDirectory)
            {
                Directory.Move(source, temporary);
                Directory.Move(temporary, destination);
            }
            else
            {
                File.Move(source, temporary);
                File.Move(temporary, destination);
            }
        }

        private static void CopyDirectory(string source, string destination, List<ModImportIssue> issues)
        {
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: false);

            foreach (var directory in Directory.GetDirectories(source))
            {
                var info = new DirectoryInfo(directory);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    issues.Add(Warning($"Skipped linked folder '{info.Name}' so the import cannot escape its source tree."));
                    continue;
                }
                if (info.Name.Equals(".git", StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(Info("Skipped source-control metadata (.git); imported project files remain independent."));
                    continue;
                }
                CopyDirectory(directory, Path.Combine(destination, info.Name), issues);
            }
        }

        private static void EnsureGitIgnore(string projectRoot)
        {
            var path = Path.Combine(projectRoot, ".gitignore");
            if (!File.Exists(path))
                File.WriteAllText(path, $".pztools-deploy/{Environment.NewLine}Workshop/{Environment.NewLine}*.log{Environment.NewLine}");
        }

        private static string FirstUsableProjectName(params string[] candidates)
        {
            foreach (var candidate in candidates)
            {
                var cleaned = string.Concat((candidate ?? "").Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
                cleaned = cleaned.Trim().TrimEnd('.');
                if (!string.IsNullOrWhiteSpace(cleaned))
                    return cleaned;
            }
            return "ImportedMod";
        }

        private static void EnsureInside(string path, string root)
        {
            var prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The imported project must remain inside the configured projects folder.");
        }

        private static ModImportIssue Info(string message) => new() { Severity = ModImportIssueSeverity.Information, Message = message };
        private static ModImportIssue Warning(string message) => new() { Severity = ModImportIssueSeverity.Warning, Message = message };
    }
}
