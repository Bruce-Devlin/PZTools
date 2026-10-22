using System.IO;
using PZTools.Core.Functions.Zomboid;
using PZTools.Core.Models;

namespace PZTools.Core.Functions.Projects
{
    public static class ProjectDeployer
    {
        private static readonly SemaphoreSlim DeploymentGate = new(1, 1);
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
            await DeploymentGate.WaitAsync(ct);
            try { await DeployProjectCore(project, deployRoot, ct); }
            finally { DeploymentGate.Release(); }
        }

        internal static async Task DeploySnapshot(DeploymentSnapshotPayload payload, string deployRoot, CancellationToken ct)
        {
            await DeploymentGate.WaitAsync(ct);
            try
            {
                await DeployProjectCore(new ModProject { RootPath = payload.RootPath, ModInfo = new() { Id = payload.ModId } },
                    deployRoot, ct, Path.Combine(payload.RootPath, DeploymentManifestService.ManifestFileName));
            }
            finally { DeploymentGate.Release(); }
        }

        private static async Task DeployProjectCore(ModProject project, string deployRoot, CancellationToken ct, string? snapshotManifest = null)
        {
            ArgumentNullException.ThrowIfNull(project);
            if (string.IsNullOrWhiteSpace(project.RootPath) || !Directory.Exists(project.RootPath))
                throw new DirectoryNotFoundException(project.RootPath);

            var projectRoot = Path.GetFullPath(project.RootPath.Trim());
            deployRoot = Path.GetFullPath(deployRoot);

            var projectName = new DirectoryInfo(projectRoot).Name;

            var finalDest = Path.Combine(deployRoot, projectName);

            // Isolated playtests live under the excluded .pztools folder. Other
            // overlapping destinations can recurse or replace the source itself.
            if (IsWithin(projectRoot, finalDest) ||
                (IsWithin(finalDest, projectRoot) && ShouldIncludePath(Path.GetRelativePath(projectRoot, finalDest))))
                throw new InvalidOperationException("Deployment must not replace the source project or copy into its loadable content. Choose a separate mods folder.");
            RejectLinkedPath(projectRoot);
            RejectLinkedPath(finalDest);
            Directory.CreateDirectory(deployRoot);

            var operationId = Guid.NewGuid().ToString("N");
            var stagingDest = Path.Combine(deployRoot, $".{projectName}.{operationId}.staging");
            var backupDest = Path.Combine(deployRoot, $".{projectName}.{operationId}.backup");

            await Console.Log($"Beginning deployment of project: \"{projectRoot}\" to: \"{finalDest}\"");

            Directory.CreateDirectory(stagingDest);

            try
            {
                await Console.Log("Staging project...");

                var stagedFilesCount = await Task.Run(() =>
                {
                    return CopyDirectory(
                        sourceRoot: projectRoot,
                        destRoot: stagingDest,
                        shouldInclude: ShouldIncludePath,
                        ct: ct);
                }, ct);

                if (snapshotManifest is null)
                    DeploymentManifestService.Write(stagingDest, project.ModInfo.Id.Length > 0 ? project.ModInfo.Id : projectName);
                else
                {
                    File.Copy(snapshotManifest, Path.Combine(stagingDest, DeploymentManifestService.ManifestFileName));
                    var errors = DeploymentManifestService.Verify(stagingDest, stagingDest, project.ModInfo.Id)
                        .Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
                    if (errors.Length > 0) throw new IOException("Snapshot copy failed verification: " + string.Join("; ", errors.Select(e => e.Message)));
                }

                await Console.Log($"Deploying {stagedFilesCount} files from staging folder...");
                await ReplaceDeploymentAsync(stagingDest, finalDest, backupDest, ct);
                await Console.Log("Project deployed successfully.");
            }
            finally
            {
                await TryDeleteDirectoryAsync(stagingDest);
            }
        }

        private static bool IsWithin(string path, string root) =>
            Path.TrimEndingDirectorySeparator(path).Equals(Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

        internal static void RejectLinkedPath(string path)
        {
            for (var current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
                if ((Directory.Exists(current) || File.Exists(current)) &&
                    File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                    throw new IOException($"Linked paths cannot be deployed or verified: {current}");
        }

        internal static IEnumerable<string> EnumeratePayloadFiles(string root)
        {
            RejectLinkedPath(root);
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.TryPop(out var current))
            {
                foreach (var path in Directory.EnumerateFileSystemEntries(current))
                {
                    if (!ShouldIncludePath(Path.GetRelativePath(root, path))) continue;
                    var attributes = File.GetAttributes(path);
                    if (attributes.HasFlag(FileAttributes.ReparsePoint))
                        throw new IOException($"Linked paths cannot be deployed or verified: {path}");
                    if (attributes.HasFlag(FileAttributes.Directory)) pending.Push(path);
                    else yield return path;
                }
            }
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

        internal static async Task ReplaceDeploymentAsync(string stagingDest, string finalDest, string backupDest, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var hadExistingDeployment = Directory.Exists(finalDest);
            if (hadExistingDeployment)
                await MoveDirectoryWithRetryAsync(finalDest, backupDest, ct);

            try
            {
                await MoveDirectoryWithRetryAsync(stagingDest, finalDest, ct);
            }
            catch (Exception deploymentError)
            {
                // A failed rename does not install a partial deployment. Do not delete
                // an unexpected destination, and restore the backup even on cancellation.
                if (hadExistingDeployment && Directory.Exists(backupDest))
                {
                    try
                    {
                        await MoveDirectoryWithRetryAsync(backupDest, finalDest, CancellationToken.None);
                    }
                    catch (Exception restoreError) when (restoreError is IOException or UnauthorizedAccessException)
                    {
                        throw new IOException($"Deployment failed and the previous deployment could not be restored to '{finalDest}'. " +
                            $"The previous files are preserved at '{backupDest}'. Close processes using these folders before restoring them.",
                            new AggregateException(deploymentError, restoreError));
                    }
                }
                throw;
            }
            // A cleanup failure must not roll back an already committed deployment.
            await TryDeleteDirectoryAsync(backupDest);
        }

        private static async Task MoveDirectoryWithRetryAsync(string source, string destination, CancellationToken ct)
        {
            const int attempts = 7;
            for (var attempt = 0; ; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    Directory.Move(source, destination);
                    return;
                }
                catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) &&
                                           (ex.HResult & 0xffff) is 5 or 32 or 33)
                {
                    // Windows can report ACCESS_DENIED for a directory rename while
                    // a scanner or reader holds a child file without delete sharing.
                    if (attempt == attempts - 1)
                        throw new IOException($"Could not move deployment folder '{source}' to '{destination}' after retrying. " +
                            "Close processes using these folders and check their permissions before retrying.", ex);
                    if (attempt == 0)
                        await Console.Log($"Deployment folder is unavailable; retrying move from '{source}' to '{destination}'.", Console.LogLevel.Warning);
                    await Task.Delay(Math.Min(100 << attempt, 500), ct);
                }
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
                    RejectLinkedPath(dir);
                    Directory.CreateDirectory(Path.Combine(destRoot, rel));
                    pending.Push(dir);
                }

                foreach (var file in Directory.EnumerateFiles(current))
                {
                    ct.ThrowIfCancellationRequested();
                    var rel = Path.GetRelativePath(sourceRoot, file);
                    if (!shouldInclude(rel))
                        continue;
                    RejectLinkedPath(file);
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
                    p.Equals(".codex", StringComparison.OrdinalIgnoreCase) ||
                    p.Equals(".agents", StringComparison.OrdinalIgnoreCase) ||
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
                    fileName.Equals(DeploymentManifestService.ManifestFileName, StringComparison.OrdinalIgnoreCase) ||
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

        private static async Task TryDeleteDirectoryAsync(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    await Task.Run(() => WindowsHelpers.DeleteDirectoryRobust(path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Retain locked temporary files without hiding a deployment failure
                // or reporting a committed deployment as failed.
                await Console.Log($"Deployment cleanup could not remove '{path}': {ex.Message} " +
                    "The remaining files have been preserved.", Console.LogLevel.Warning);
            }
        }
    }
}
