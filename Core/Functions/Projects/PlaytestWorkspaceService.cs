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
                : Path.Combine(profileRoot, "sessions", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..8]);
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
            if (profile.SaveMode == PlaytestSaveMode.FreshSessionClone)
            {
                var errors = PlaytestFixtureService.Validate(profile);
                if (errors.Count > 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors));
                foreach (var cache in clientCaches.Append(serverCache))
                {
                    var sourceCache = Path.Combine(profile.SourceSessionPath, Path.GetFileName(cache));
                    foreach (var folder in cache == serverCache ? new[] { "Saves", "db", "Server" } : new[] { "Saves", "db" })
                        if (Directory.Exists(Path.Combine(sourceCache, folder)))
                            CopyDirectory(Path.Combine(sourceCache, folder), Path.Combine(cache, folder));
                    var login = Path.Combine(sourceCache, "Lua", "pztools-local-login.txt");
                    if (File.Exists(login))
                    {
                        Directory.CreateDirectory(Path.Combine(cache, "Lua"));
                        File.Copy(login, Path.Combine(cache, "Lua", "pztools-local-login.txt"));
                    }
                }
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
            EnsureNoLinks(source);
            var sourceRoot = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if ((Path.GetFullPath(destination) + Path.DirectorySeparatorChar).StartsWith(sourceRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The destination must not be inside the source save.");
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

        internal static void EnsureNoLinks(string path)
        {
            for (var parent = new DirectoryInfo(path); parent is not null; parent = parent.Parent)
                if ((parent.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Linked save folders are not supported. Select the original folder.");
            var pending = new Stack<string>(); pending.Push(path);
            while (pending.Count > 0)
                foreach (var entry in Directory.EnumerateFileSystemEntries(pending.Pop()))
                {
                    var attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("The save contains a linked file or folder.");
                    if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
                }
        }
    }
}
