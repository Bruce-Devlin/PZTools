using System.IO;
using PZTools.Core.Models;

namespace PZTools.Core.Functions.Projects
{
    public static class DependencyLoadOrder
    {
        public static IReadOnlyList<string> Resolve(ModProject project, PlaytestProfile profile)
        {
            var enabled = profile.Dependencies.Where(x => x.Enabled && !string.IsNullOrWhiteSpace(x.ModId)).ToList();
            var original = enabled.Select(x => x.ModId.Trim().TrimStart('\\')).Append(project.ModInfo.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var edges = original.ToDictionary(x => x, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

            AddRules(project.ModInfo.Id, project.ModInfo, edges);
            foreach (var dependency in enabled.Where(x => !string.IsNullOrWhiteSpace(x.SourcePath) && Directory.Exists(x.SourcePath)))
            {
                var discovered = ModDependencyService.Discover(new[] { dependency.SourcePath }, profile.Build);
                var match = ModDependencyService.Resolve(dependency.ModId, discovered, dependency.WorkshopId);
                if (match is not null)
                    AddRules(match.ModId, match.Info, edges);
            }

            var remaining = new HashSet<string>(original, StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            while (remaining.Count > 0)
            {
                var next = original.FirstOrDefault(id => remaining.Contains(id) &&
                    edges.Where(x => remaining.Contains(x.Key)).All(x => !x.Value.Contains(id)));
                if (next is null)
                    throw new InvalidDataException("Dependency load-order rules contain a cycle. Check loadModAfter/loadModBefore in the project and its local dependencies.");
                result.Add(next);
                remaining.Remove(next);
            }
            return result;
        }

        private static void AddRules(string ownerId, ModInfo info, Dictionary<string, HashSet<string>> edges)
        {
            if (!edges.ContainsKey(ownerId))
                return;
            foreach (var required in info.Requires.Select(Normalize).Where(edges.ContainsKey))
                edges[required].Add(ownerId);
            foreach (var before in info.LoadModBefore.Select(Normalize).Where(edges.ContainsKey))
                edges[ownerId].Add(before);
            foreach (var after in info.LoadModAfter.Select(Normalize).Where(edges.ContainsKey))
                edges[after].Add(ownerId);
        }

        private static string Normalize(string id) => id.Trim().TrimStart('\\');
    }
}
