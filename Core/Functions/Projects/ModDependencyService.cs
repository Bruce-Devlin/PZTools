using System.Globalization;
using System.IO;
using PZTools.Core.Functions.Zomboid;
using PZTools.Core.Models;

namespace PZTools.Core.Functions.Projects
{
    public static class ModDependencyService
    {
        private const int MaxModInfoFiles = 5000;

        public static IReadOnlyList<DiscoveredModDependency> Discover(double build, CancellationToken cancellationToken = default)
            => Discover(ZomboidGame.GetModSearchRoots(), build, cancellationToken);

        public static IReadOnlyList<DiscoveredModDependency> Discover(
            IEnumerable<string> searchRoots,
            double build,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(searchRoots);
            var candidates = new List<(string Container, string InfoPath, double? Version, int RootPriority)>();
            var rootPriority = 0;

            foreach (var searchRoot in searchRoots.Where(Directory.Exists))
            {
                try
                {
                    foreach (var infoPath in Directory.EnumerateFiles(searchRoot, "mod.info", SearchOption.AllDirectories).Take(MaxModInfoFiles))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var infoDirectory = Path.GetDirectoryName(infoPath)!;
                        var folderName = Path.GetFileName(infoDirectory);
                        var isVersion = TryBuild(folderName, out var version);
                        var isCommon = folderName.Equals("common", StringComparison.OrdinalIgnoreCase);
                        var container = isVersion || isCommon
                            ? Directory.GetParent(infoDirectory)?.FullName ?? infoDirectory
                            : infoDirectory;
                        candidates.Add((Path.TrimEndingDirectorySeparator(Path.GetFullPath(container)), infoPath,
                            isVersion ? version : null, rootPriority));
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { }
                rootPriority++;
            }

            var discovered = new List<DiscoveredModDependency>();
            foreach (var containerGroup in candidates.GroupBy(x => x.Container, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var selected = SelectForBuild(containerGroup, build);
                if (selected == default)
                    continue;

                ModInfo info;
                try { info = ModInfoParser.Load(Path.GetDirectoryName(selected.InfoPath)!); }
                catch { continue; }
                if (string.IsNullOrWhiteSpace(info.Id))
                    continue;

                var workshopId = FindWorkshopId(selected.Container);
                discovered.Add(new DiscoveredModDependency
                {
                    ModId = info.Id,
                    Name = string.IsNullOrWhiteSpace(info.Name) ? info.Id : info.Name,
                    SourcePath = selected.Container,
                    WorkshopId = workshopId,
                    Build = selected.Version ?? build,
                    Origin = string.IsNullOrWhiteSpace(workshopId) ? "Local" : "Workshop",
                    Info = info
                });
            }

            return discovered
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.ModId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.SourcePath, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static DiscoveredModDependency? Resolve(
            string modId,
            IEnumerable<DiscoveredModDependency> discovered,
            string? preferredWorkshopId = null)
        {
            if (string.IsNullOrWhiteSpace(modId))
                return null;
            return discovered
                .Where(x => x.ModId.Equals(modId.Trim().TrimStart('\\'), StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => !string.IsNullOrWhiteSpace(preferredWorkshopId) &&
                                        x.WorkshopId.Equals(preferredWorkshopId, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(x => !string.IsNullOrWhiteSpace(x.WorkshopId))
                .ThenBy(x => x.SourcePath, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        public static void SynchronizeRequiredDependencies(
            ModProject project,
            PlaytestProfile profile,
            IEnumerable<DiscoveredModDependency>? discovered = null)
        {
            ArgumentNullException.ThrowIfNull(project);
            ArgumentNullException.ThrowIfNull(profile);
            profile.Dependencies ??= new();
            var available = discovered?.ToList() ?? new List<DiscoveredModDependency>();
            foreach (var source in profile.Dependencies.Select(x => x.SourcePath)
                         .Where(x => !string.IsNullOrWhiteSpace(x) && Directory.Exists(x))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                foreach (var local in Discover(new[] { source }, profile.Build))
                {
                    if (!available.Any(x => x.ModId.Equals(local.ModId, StringComparison.OrdinalIgnoreCase) &&
                                            x.SourcePath.Equals(local.SourcePath, StringComparison.OrdinalIgnoreCase)))
                        available.Add(local);
                }
            }
            var existing = profile.Dependencies
                .Where(x => !string.IsNullOrWhiteSpace(x.ModId))
                .GroupBy(x => x.ModId.Trim().TrimStart('\\'), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
            var synchronized = new List<PlaytestDependency>();

            var requiredIds = ExpandRequiredIds(project.ModInfo.Requires, available, project.ModInfo.Id);
            foreach (var requiredId in requiredIds)
            {
                var normalizedId = requiredId.Trim().TrimStart('\\');
                if (!existing.Remove(normalizedId, out var dependency))
                    dependency = new PlaytestDependency { ModId = normalizedId };
                dependency.ModId = normalizedId;
                dependency.Enabled = true;
                dependency.IsProjectRequired = true;
                Enrich(dependency, available);
                synchronized.Add(dependency);
            }

            foreach (var dependency in profile.Dependencies.Where(x => !string.IsNullOrWhiteSpace(x.ModId)))
            {
                var normalizedId = dependency.ModId.Trim().TrimStart('\\');
                if (!existing.Remove(normalizedId))
                    continue;
                dependency.ModId = normalizedId;
                dependency.IsProjectRequired = false;
                Enrich(dependency, available);
                synchronized.Add(dependency);
            }

            profile.Dependencies = synchronized;
        }

        public static IReadOnlyList<string> ValidateSource(PlaytestDependency dependency, double build)
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(dependency.SourcePath))
                return errors;
            if (!Directory.Exists(dependency.SourcePath))
            {
                errors.Add($"Dependency source folder does not exist: {dependency.SourcePath}");
                return errors;
            }

            var found = Discover(new[] { dependency.SourcePath }, build)
                .Where(x => x.ModId.Equals(dependency.ModId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (found.Count == 0)
                errors.Add($"Dependency source folder does not contain a loadable Build {build:0.##} mod with ID '{dependency.ModId}': {dependency.SourcePath}");
            else if (!found.Any(x => Path.TrimEndingDirectorySeparator(Path.GetFullPath(x.SourcePath)).Equals(
                         Path.TrimEndingDirectorySeparator(Path.GetFullPath(dependency.SourcePath)), StringComparison.OrdinalIgnoreCase)))
                errors.Add($"Dependency source must be the mod container that holds its build/common folders. Select: {found[0].SourcePath}");
            return errors;
        }

        private static IReadOnlyList<string> ExpandRequiredIds(
            IEnumerable<string> directIds,
            IReadOnlyList<DiscoveredModDependency> discovered,
            string projectId)
        {
            var ordered = new List<string>();
            var complete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Visit(string rawId)
            {
                var id = rawId.Trim().TrimStart('\\');
                if (id.Length == 0 || id.Equals(projectId, StringComparison.OrdinalIgnoreCase) || complete.Contains(id))
                    return;
                if (!visiting.Add(id))
                    return;
                var match = Resolve(id, discovered);
                if (match is not null)
                    foreach (var transitive in match.Info.Requires)
                        Visit(transitive);
                visiting.Remove(id);
                if (complete.Add(id))
                    ordered.Add(id);
            }

            foreach (var id in directIds)
                Visit(id);
            return ordered;
        }

        private static void Enrich(PlaytestDependency dependency, IReadOnlyList<DiscoveredModDependency> discovered)
        {
            var match = Resolve(dependency.ModId, discovered, dependency.WorkshopId);
            if (match is null)
                return;
            if (string.IsNullOrWhiteSpace(dependency.SourcePath))
                dependency.SourcePath = match.SourcePath;
            if (string.IsNullOrWhiteSpace(dependency.WorkshopId))
                dependency.WorkshopId = match.WorkshopId;
        }

        private static (string Container, string InfoPath, double? Version, int RootPriority) SelectForBuild(
            IEnumerable<(string Container, string InfoPath, double? Version, int RootPriority)> candidates,
            double build)
        {
            var list = candidates.ToList();
            var versioned = list.Where(x => x.Version.HasValue && x.Version.Value <= build)
                .OrderByDescending(x => x.Version)
                .ThenBy(x => x.RootPriority)
                .FirstOrDefault();
            if (versioned != default)
                return versioned;
            var common = list.FirstOrDefault(x => Path.GetFileName(Path.GetDirectoryName(x.InfoPath)!)
                .Equals("common", StringComparison.OrdinalIgnoreCase));
            if (common != default)
                return common;
            return list.FirstOrDefault(x => !x.Version.HasValue);
        }

        private static string FindWorkshopId(string path)
        {
            var current = new DirectoryInfo(path);
            while (current is not null)
            {
                if (current.Parent?.Name.Equals("108600", StringComparison.OrdinalIgnoreCase) == true &&
                    ulong.TryParse(current.Name, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                    return current.Name;

                var manifest = Path.Combine(current.FullName, "workshop.txt");
                if (File.Exists(manifest))
                {
                    try
                    {
                        var idLine = File.ReadLines(manifest).FirstOrDefault(x => x.StartsWith("id=", StringComparison.OrdinalIgnoreCase));
                        var id = idLine?[3..].Trim();
                        if (ulong.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                            return id!;
                    }
                    catch { }
                }
                current = current.Parent;
            }
            return "";
        }

        private static bool TryBuild(string value, out double build)
            => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out build) && double.IsFinite(build) && build > 0;
    }
}
