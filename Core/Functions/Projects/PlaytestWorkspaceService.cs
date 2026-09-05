using System.IO;
using PZTools.Core.Models;

namespace PZTools.Core.Functions.Projects
{
    public static class PlaytestWorkspaceService
    {
        public static PlaytestSessionWorkspace Prepare(ModProject project, PlaytestProfile profile)
        {
            ArgumentNullException.ThrowIfNull(project);
            ArgumentNullException.ThrowIfNull(profile);

            var pzToolsRoot = Path.Combine(project.RootPath, ".pztools");
            Directory.CreateDirectory(pzToolsRoot);
            var ignorePath = Path.Combine(pzToolsRoot, ".gitignore");
            if (!File.Exists(ignorePath))
                File.WriteAllText(ignorePath, $"playtests/{Environment.NewLine}");
            else if (!File.ReadAllLines(ignorePath).Any(x => x.Trim().Equals("playtests/", StringComparison.OrdinalIgnoreCase)))
                File.AppendAllText(ignorePath, $"playtests/{Environment.NewLine}");
            var profileRoot = Path.Combine(pzToolsRoot, "playtests", profile.Id.ToString("N"));
            var sessionRoot = profile.SaveMode == PlaytestSaveMode.ReuseProfileData
                ? Path.Combine(profileRoot, "reusable")
                : Path.Combine(profileRoot, "sessions", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            var serverCache = Path.Combine(sessionRoot, "server");
            var clientCaches = Enumerable.Range(1, profile.Mode == PlaytestMode.DedicatedServer ? profile.ClientCount : 1)
                .Select(x => Path.Combine(sessionRoot, $"client-{x}"))
                .ToList();

            Directory.CreateDirectory(serverCache);
            foreach (var cache in clientCaches)
                Directory.CreateDirectory(cache);

            if (profile.SaveMode == PlaytestSaveMode.FreshClone)
            {
                var destinationCache = profile.Mode == PlaytestMode.DedicatedServer ? serverCache : clientCaches[0];
                CloneSave(profile.SourceSavePath, destinationCache);
            }

            return new PlaytestSessionWorkspace
            {
                RootPath = sessionRoot,
                ServerCachePath = serverCache,
                ClientCachePaths = clientCaches,
                IsReusable = profile.SaveMode == PlaytestSaveMode.ReuseProfileData
            };
        }

        public static void Cleanup(PlaytestSessionWorkspace workspace, bool keepSessionData)
        {
            if (keepSessionData || workspace.IsReusable || !Directory.Exists(workspace.RootPath))
                return;
            var fullPath = Path.GetFullPath(workspace.RootPath);
            if (!fullPath.Contains($"{Path.DirectorySeparatorChar}.pztools{Path.DirectorySeparatorChar}playtests{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing to remove a folder outside the PZTools playtest workspace.");
            WindowsHelpers.DeleteDirectoryRobust(fullPath);
        }

        private static void CloneSave(string sourceSave, string destinationCache)
        {
            if (!Directory.Exists(sourceSave))
                throw new DirectoryNotFoundException(sourceSave);
            var savesMarker = $"{Path.DirectorySeparatorChar}Saves{Path.DirectorySeparatorChar}";
            var fullSource = Path.GetFullPath(sourceSave);
            var markerIndex = fullSource.IndexOf(savesMarker, StringComparison.OrdinalIgnoreCase);
            var relative = markerIndex >= 0
                ? fullSource[(markerIndex + 1)..]
                : Path.Combine("Saves", "PZTools", Path.GetFileName(fullSource));
            CopyDirectory(fullSource, Path.Combine(destinationCache, relative));
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(destination, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
            }
        }
    }
}
