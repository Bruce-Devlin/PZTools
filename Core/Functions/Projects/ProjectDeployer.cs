using PZTools.Core.Functions.Zomboid;
using PZTools.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PZTools.Core.Functions.Projects
{
    public static class ProjectDeployer
    {
        private static int stagedFilesCount = 0;

        public static async Task DeployProject(DeployFolder deployFolder)
        {
            await DeployProject(deployFolder, CancellationToken.None);
        }

        public static async Task DeployProject(DeployFolder deployFolder, CancellationToken ct)
        {
            var currentProjectPath = ProjectEngine.CurrentProjectPath;

            if (string.IsNullOrWhiteSpace(ZomboidGame.GameUserDirectory))
                throw new InvalidOperationException("Zomboid game user directory is not configured.");

            var projectRoot = Path.GetFullPath(currentProjectPath.Trim());
            var deployRoot = GetDeployRoot(deployFolder);

            Directory.CreateDirectory(deployRoot);

            var projectName = new DirectoryInfo(projectRoot).Name;

            var finalDest = Path.Combine(deployRoot, projectName);

            var stagingDest = Path.Combine(deployRoot, $".{projectName}.staging");
            var backupDest = Path.Combine(deployRoot, $".{projectName}.backup");

            await Console.Log($"Begining Deployment of Project: \"{currentProjectPath}\" to: \"{finalDest}\"");

            TryDeleteDirectory(stagingDest);
            Directory.CreateDirectory(stagingDest);

            await Console.Log("Staging project...");

            await Task.Run(() =>
            {
                CopyDirectoryIncremental(
                    sourceRoot: projectRoot,
                    destRoot: stagingDest,
                    shouldInclude: ShouldIncludePath,
                    ct: ct);
            }, ct);

            await Console.Log($"Deploying {stagedFilesCount} files from staging folder...");
            await Task.Run(() => DeployFromStaging(stagingDest, finalDest, ct), ct);

            TryDeleteDirectory(stagingDest);

            stagedFilesCount = 0;
            await Console.Log($"Deployed Project.");
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

        private static void DeployFromStaging(string stagingDest, string finalDest, CancellationToken ct)
        {
            Directory.CreateDirectory(finalDest);

            foreach (var dir in Directory.EnumerateDirectories(stagingDest, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                var rel = Path.GetRelativePath(stagingDest, dir);
                Directory.CreateDirectory(Path.Combine(finalDest, rel));
            }

            foreach (var file in Directory.EnumerateFiles(stagingDest, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                var rel = Path.GetRelativePath(stagingDest, file);
                var destFile = Path.Combine(finalDest, rel);

                Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);

                try
                {
                    Console.Log($"Deploying file: {file}");
                    File.Copy(file, destFile, overwrite: true);

                    var srcInfo = new FileInfo(file);
                    File.SetLastWriteTimeUtc(destFile, srcInfo.LastWriteTimeUtc);
                }
                catch (IOException)
                {
                    Console.Log($"Skipped locked file: {destFile}", Logger.Console.LogLevel.Warning);
                }
                catch (UnauthorizedAccessException)
                {
                    Console.Log($"Access denied copying: {destFile}", Logger.Console.LogLevel.Warning);
                }
            }
        }

        private static void CopyDirectoryIncremental(
            string sourceRoot,
            string destRoot,
            Func<string, bool> shouldInclude,
            CancellationToken ct)
        {
            foreach (var dir in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();

                var rel = Path.GetRelativePath(sourceRoot, dir);

                if (!shouldInclude(rel))
                    continue;

                Console.Log($"Staging Project folder: {dir}");

                var targetDir = Path.Combine(destRoot, rel);
                Directory.CreateDirectory(targetDir);
            }

            foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();

                var rel = Path.GetRelativePath(sourceRoot, file);

                if (!shouldInclude(rel))
                    continue;

                stagedFilesCount++;

                var targetFile = Path.Combine(destRoot, rel);

                Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);

                if (FileNeedsCopy(file, targetFile))
                {
                    Console.Log($"Staging Project file: {file}");
                    File.Copy(file, targetFile, overwrite: true);

                    var srcInfo = new FileInfo(file);
                    File.SetLastWriteTimeUtc(targetFile, srcInfo.LastWriteTimeUtc);
                }
            }
        }

        private static bool FileNeedsCopy(string sourceFile, string destFile)
        {
            if (!File.Exists(destFile))
                return true;

            var src = new FileInfo(sourceFile);
            var dst = new FileInfo(destFile);

            if (src.Length != dst.Length)
                return true;

            if (src.LastWriteTimeUtc != dst.LastWriteTimeUtc)
                return true;

            return false;
        }

        private static bool ShouldIncludePath(string relativePath)
        {
            var parts = SplitPathParts(relativePath);

            if (parts.Any(p =>
                    p.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                    p.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                    p.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                    p.Equals(".vs", StringComparison.OrdinalIgnoreCase) ||
                    p.Equals(".idea", StringComparison.OrdinalIgnoreCase) ||
                    p.Equals(".vscode", StringComparison.OrdinalIgnoreCase)))
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
            try
            {
                if (!Directory.Exists(path))
                    return;

                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var attrs = File.GetAttributes(file);
                        if ((attrs & FileAttributes.ReadOnly) != 0)
                            File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
                    }
                    catch
                    {
                    }
                }

                Directory.Delete(path, recursive: true);
            }
            catch (Exception ex)
            {
                {
                    Console.Log(ex.Message, Logger.Console.LogLevel.Warning);
                }
            }
        }
    }
}
