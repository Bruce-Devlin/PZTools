using System.IO;
using PZTools.Core.Functions.Zomboid;
using PZTools.Core.Models;

namespace PZTools.Core.Functions.Projects
{
    public static class ProjectDeployer
    {
        public static async Task DeployProject(DeployFolder deployFolder)
        {
            await DeployProject(deployFolder, CancellationToken.None);
        }

        public static async Task DeployProject(DeployFolder deployFolder, CancellationToken ct)
        {
            var project = ProjectEngine.CurrentProject;
            if (project is null)
                throw new InvalidOperationException("No valid mod project is currently loaded.");

            if (string.IsNullOrWhiteSpace(project.RootPath) || !Directory.Exists(project.RootPath))
                throw new InvalidOperationException("No valid mod project is currently loaded.");

            if (string.IsNullOrWhiteSpace(ZomboidGame.GameUserDirectory))
                throw new InvalidOperationException("Zomboid game user directory is not configured.");

            var deployRoot = GetDeployRoot(deployFolder);
            await DeployProject(project, deployRoot, ct);
        }

        public static async Task DeployProject(ModProject project, string deployRoot, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(project);
            if (string.IsNullOrWhiteSpace(project.RootPath) || !Directory.Exists(project.RootPath))
                throw new DirectoryNotFoundException(project.RootPath);

            var projectRoot = Path.GetFullPath(project.RootPath.Trim());
            deployRoot = Path.GetFullPath(deployRoot);
            Directory.CreateDirectory(deployRoot);

            var projectName = new DirectoryInfo(projectRoot).Name;

            var finalDest = Path.Combine(deployRoot, projectName);

            var stagingDest = Path.Combine(deployRoot, $".{projectName}.staging");
            var backupDest = Path.Combine(deployRoot, $".{projectName}.backup");

            await Console.Log($"Beginning deployment of project: \"{projectRoot}\" to: \"{finalDest}\"");

            TryDeleteDirectory(stagingDest);
            Directory.CreateDirectory(stagingDest);

            await Console.Log("Staging project...");

            var stagedFilesCount = await Task.Run(() =>
            {
                return CopyDirectory(
                    sourceRoot: projectRoot,
                    destRoot: stagingDest,
                    shouldInclude: ShouldIncludePath,
                    ct: ct);
            }, ct);

            DeploymentManifestService.Write(stagingDest, project.ModInfo.Id.Length > 0 ? project.ModInfo.Id : projectName);

            await Console.Log($"Deploying {stagedFilesCount} files from staging folder...");
            await Task.Run(() => ReplaceDeployment(stagingDest, finalDest, backupDest, ct), ct);
            await Console.Log("Project deployed successfully.");
        }

        private static string GetDeployRoot(DeployFolder deployFolder)
        {
            var baseDir = Path.GetFullPath(ZomboidGame.GameUserDirectory.Trim());

            return deployFolder switch
            {
                DeployFolder.Mods => Path.Combine(baseDir, "mods"),
                DeployFolder.Workshop => Path.Combine(baseDir, "workshop"),
                _ => throw new ArgumentOutOfRangeException(nameof(deployFolder), deployFolder, null)
            };
        }

        private static void ReplaceDeployment(string stagingDest, string finalDest, string backupDest, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            TryDeleteDirectory(backupDest);

            var hadExistingDeployment = Directory.Exists(finalDest);
            if (hadExistingDeployment)
                Directory.Move(finalDest, backupDest);

            try
            {
                ct.ThrowIfCancellationRequested();
                Directory.Move(stagingDest, finalDest);
                TryDeleteDirectory(backupDest);
            }
            catch
            {
                TryDeleteDirectory(finalDest);
                if (hadExistingDeployment && Directory.Exists(backupDest))
                    Directory.Move(backupDest, finalDest);
                throw;
            }
        }

        private static int CopyDirectory(
            string sourceRoot,
            string destRoot,
            Func<string, bool> shouldInclude,
            CancellationToken ct)
        {
            var copiedFiles = 0;
            var pending = new Stack<string>();
            pending.Push(sourceRoot);
            while (pending.TryPop(out var current))
            {
                ct.ThrowIfCancellationRequested();
                // Prune excluded folders before enumeration: .pztools contains live
                // game caches and may also contain this deployment's destination.
                foreach (var dir in Directory.EnumerateDirectories(current))
                {
                    ct.ThrowIfCancellationRequested();
                    var rel = Path.GetRelativePath(sourceRoot, dir);
                    if (!shouldInclude(rel))
                        continue;
                    Directory.CreateDirectory(Path.Combine(destRoot, rel));
                    pending.Push(dir);
                }

                foreach (var file in Directory.EnumerateFiles(current))
                {
                    ct.ThrowIfCancellationRequested();
                    var rel = Path.GetRelativePath(sourceRoot, file);
                    if (!shouldInclude(rel))
                        continue;
                    var targetFile = Path.Combine(destRoot, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
                    File.Copy(file, targetFile, overwrite: true);
                    File.SetLastWriteTimeUtc(targetFile, File.GetLastWriteTimeUtc(file));
                    copiedFiles++;
                }
            }
            return copiedFiles;
        }

        internal static bool ShouldIncludePath(string relativePath)
        {
            var parts = SplitPathParts(relativePath);

            if (parts.Any(p =>
                    p.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                    p.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                    p.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                    p.Equals(".vs", StringComparison.OrdinalIgnoreCase) ||
                    p.Equals(".idea", StringComparison.OrdinalIgnoreCase) ||
                    p.Equals(".vscode", StringComparison.OrdinalIgnoreCase) ||
                    p.Equals(".pztools", StringComparison.OrdinalIgnoreCase)))
                return false;

            if (parts.Any(p => p.Equals(".pztests", StringComparison.OrdinalIgnoreCase)))
                return false;

            if (parts.Any(p =>
                    p.Equals("TestResults", StringComparison.OrdinalIgnoreCase) ||
                    p.Equals("packages", StringComparison.OrdinalIgnoreCase)))
                return false;

            var fileName = Path.GetFileName(relativePath);
            if (!string.IsNullOrEmpty(fileName))
            {
                if (fileName.EndsWith(".user", StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith(".suo", StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith(".code-workspace", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Equals(".luarc.json", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Equals(".gitignore", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Equals("thumbs.db", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Equals(".DS_Store", StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }

        private static IReadOnlyList<string> SplitPathParts(string path)
        {
            var normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            return normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        }

        private static void TryDeleteDirectory(string path)
        {
            if (Directory.Exists(path))
                WindowsHelpers.DeleteDirectoryRobust(path);
        }
    }
}
