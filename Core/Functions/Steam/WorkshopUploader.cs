using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using PZTools.Core.Functions.Projects;
using PZTools.Core.Models;

namespace PZTools.Core.Functions.Steam
{
    public sealed record WorkshopUploadPlan(WorkshopSettings Settings, string PreviewPath, string PackagePath)
    {
        public bool IsNewItem => Settings.PublishedFileId == "0";
    }

    internal static class WorkshopUploader
    {
        private const string AppId = "108600";

        public static async Task<bool> UploadAsync(
            ModProject project,
            string username,
            string changeNote,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await UploadAttemptAsync(project, username, changeNote, cancellationToken);
            }
            catch (WorkshopItemMissingException ex)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Console.Log($"Steam could not find Workshop item {ex.ItemId}. Creating a replacement item...");
                // Retry only once. Preparation uses the cleared ID to create a fresh
                // manifest, and any ID assigned to the replacement is retained.
                return await UploadAttemptAsync(project, username, changeNote, cancellationToken);
            }
        }

        private static async Task<bool> UploadAttemptAsync(
            ModProject project,
            string username,
            string changeNote,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(project);
            if (string.IsNullOrWhiteSpace(username))
                throw new ArgumentException("A Steam username is required.", nameof(username));
            if (!Directory.Exists(project.RootPath))
                throw new DirectoryNotFoundException(project.RootPath);

            var plan = CreatePlan(project);
            var packageRoot = PreparePackage(project, plan.Settings, changeNote, cancellationToken);
            var steamCmdDirectory = Path.Combine(AppPaths.CurrentDirectoryPath, "SteamCMD");
            var installer = new SteamInstaller();
            await installer.SetupSteamCmdAsync(steamCmdDirectory, cancellationToken);

            var manifestPath = packageRoot + ".vdf";
            await Console.Log("Opening SteamCMD to upload the workshop item. Complete password and Steam Guard prompts there.");

            var startInfo = new ProcessStartInfo
            {
                FileName = installer.SteamCmdExecutable,
                WorkingDirectory = steamCmdDirectory,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal
            };
            startInfo.ArgumentList.Add("+login");
            startInfo.ArgumentList.Add(username);
            startInfo.ArgumentList.Add("+workshop_build_item");
            startInfo.ArgumentList.Add(manifestPath);
            startInfo.ArgumentList.Add("+quit");

            using var process = new Process { StartInfo = startInfo };
            var consoleLogPath = Path.Combine(steamCmdDirectory, "logs", "console_log.txt");
            var logOffset = File.Exists(consoleLogPath) ? new FileInfo(consoleLogPath).Length : 0;
            if (!process.Start())
                return false;
            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                process.TryKillProcessTree();
                await process.WaitForExitAsync();
                SaveAssignedItemId(project, plan.Settings, manifestPath);
                throw;
            }

            CompleteUpload(project, plan.Settings, manifestPath, changeNote, process.ExitCode,
                ReadUploadLog(consoleLogPath, logOffset));
            await Console.Log($"Steam Workshop upload completed (item {plan.Settings.PublishedFileId}).");
            return true;
        }

        internal static void CompleteUpload(ModProject project, WorkshopSettings settings,
            string manifestPath, string changeNote, int exitCode, string uploadLog)
        {
            var attemptedItemId = settings.PublishedFileId;
            // Steam creates the item before uploading its contents and preview. Preserve
            // that ID even on failure so a retry updates the same item.
            SaveAssignedItemId(project, settings, manifestPath);
            var error = uploadLog.Split('\n').LastOrDefault(line =>
                line.Contains("ERROR!", StringComparison.OrdinalIgnoreCase))?.Trim();
            if (exitCode != 0 || error != null)
            {
                if (attemptedItemId != "0" && settings.PublishedFileId == attemptedItemId &&
                    error?.Contains("Failed to update workshop item (File Not Found)", StringComparison.OrdinalIgnoreCase) == true)
                {
                    settings.PublishedFileId = "0";
                    WorkshopSettingsStore.Save(project, settings);
                    throw new WorkshopItemMissingException(attemptedItemId);
                }

                var itemMessage = settings.PublishedFileId != "0"
                    ? $" Item {settings.PublishedFileId} has been saved; retrying will update this item."
                    : " Steam did not assign an item ID.";
                var limitMessage = error?.Contains("Limit exceeded", StringComparison.OrdinalIgnoreCase) == true
                    ? " Steam rejected an upload limit. Check SteamCMD's workshop_log.txt for the specific preview or Steam Cloud limit."
                    : "";
                throw new InvalidOperationException(
                    $"Steam Workshop upload failed (exit code {exitCode}). {error}" + limitMessage + itemMessage);
            }

            if (settings.PublishedFileId == "0")
                throw new InvalidOperationException("SteamCMD exited without assigning a Published File ID. Review SteamCMD's workshop log before retrying.");

            settings.DefaultChangeNote = string.IsNullOrWhiteSpace(changeNote) ? settings.DefaultChangeNote : changeNote.Trim();
            WorkshopSettingsStore.Save(project, settings);
        }

        internal sealed class WorkshopItemMissingException(string itemId) : InvalidOperationException(
            $"Steam could not find Workshop item {itemId}. Its saved ID has been cleared; the next upload will create a new item.")
        {
            public string ItemId { get; } = itemId;
        }

        private static void SaveAssignedItemId(ModProject project, WorkshopSettings settings, string manifestPath)
        {
            var publishedId = File.Exists(manifestPath) ? ReadVdfValue(manifestPath, "publishedfileid") : null;
            if (ulong.TryParse(publishedId, out var id) && id > 0 && settings.PublishedFileId != publishedId)
            {
                settings.PublishedFileId = publishedId;
                WorkshopSettingsStore.Save(project, settings);
            }
        }

        private static string ReadUploadLog(string path, long offset)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                stream.Position = stream.Length >= offset ? offset : 0;
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return "";
            }
        }

        public static WorkshopUploadPlan CreatePlan(ModProject project)
        {
            var metadataRoot = project.Targets.FirstOrDefault(x => x.IsPrimary)?.Path
                ?? project.Targets.FirstOrDefault()?.Path
                ?? project.RootPath;
            var modInfo = ModInfoParser.Load(metadataRoot);
            if (string.IsNullOrWhiteSpace(modInfo.Id) || string.IsNullOrWhiteSpace(modInfo.Name))
                throw new InvalidOperationException("Set a valid mod name and ID in Project Settings before uploading.");

            var settings = WorkshopSettingsStore.Load(project);
            var errors = WorkshopSettingsStore.Validate(settings);
            if (errors.Count > 0)
                throw new InvalidDataException(string.Join(Environment.NewLine, errors));

            var previewSource = ResolvePreview(metadataRoot, modInfo)
                ?? throw new InvalidOperationException("Add a poster or icon in Project Settings before uploading to the Workshop.");
            var extension = Path.GetExtension(previewSource);
            if (!extension.Equals(".png", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The Workshop preview must be a PNG or JPEG image.");

            var packageRoot = Path.Combine(AppPaths.CurrentDirectoryPath, "Workshop", ModInfoUtil.NormalizeModId(modInfo.Id));
            return new WorkshopUploadPlan(settings, previewSource, packageRoot);
        }

        internal static string PreparePackage(ModProject project, WorkshopSettings settings, string changeNote, CancellationToken ct)
        {
            var metadataRoot = project.Targets.FirstOrDefault(x => x.IsPrimary)?.Path
                ?? project.Targets.FirstOrDefault()?.Path
                ?? project.RootPath;
            var modInfo = ModInfoParser.Load(metadataRoot);
            var plan = CreatePlan(project);
            var packageRoot = plan.PackagePath;
            var stagingRoot = packageRoot + ".staging-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(stagingRoot);

            try
            {
                var contentRoot = Path.Combine(stagingRoot, "Contents", "mods", ModInfoUtil.NormalizeModId(modInfo.Id));
                CopyProject(project.RootPath, contentRoot, ct);

                WorkshopPreview.Prepare(plan.PreviewPath, stagingRoot);

                var workshopInfo = new[]
                {
                    "version=1",
                    $"id={settings.PublishedFileId}",
                    $"title={SingleLine(settings.Title)}",
                    $"description={SingleLine(settings.Description)}",
                    $"tags={string.Join(';', settings.Tags.Select(SingleLine))}",
                    $"visibility={WorkshopSettingsStore.VisibilityLabel(settings.Visibility)}"
                };
                File.WriteAllLines(Path.Combine(stagingRoot, "workshop.txt"), workshopInfo, new UTF8Encoding(false));

                ct.ThrowIfCancellationRequested();
                if (Directory.Exists(packageRoot))
                    WindowsHelpers.DeleteDirectoryRobust(packageRoot);
                Directory.Move(stagingRoot, packageRoot);
            }
            catch
            {
                if (Directory.Exists(stagingRoot))
                    WindowsHelpers.DeleteDirectoryRobust(stagingRoot);
                throw;
            }

            var previewPath = Directory.EnumerateFiles(packageRoot, "preview.*", SearchOption.TopDirectoryOnly).Single();

            var vdf = new StringBuilder()
                .AppendLine("\"workshopitem\"")
                .AppendLine("{")
                .AppendLine($"    \"appid\" \"{AppId}\"")
                .AppendLine($"    \"publishedfileid\" \"{EscapeVdf(settings.PublishedFileId)}\"")
                .AppendLine($"    \"contentfolder\" \"{EscapeVdf(packageRoot)}\"")
                .AppendLine($"    \"previewfile\" \"{EscapeVdf(previewPath)}\"")
                .AppendLine($"    \"visibility\" \"{(int)settings.Visibility}\"")
                .AppendLine($"    \"title\" \"{EscapeVdf(settings.Title)}\"")
                .AppendLine($"    \"description\" \"{EscapeVdf(settings.Description)}\"")
                .AppendLine($"    \"tags\" \"{EscapeVdf(string.Join(',', settings.Tags))}\"")
                .AppendLine($"    \"changenote\" \"{EscapeVdf(string.IsNullOrWhiteSpace(changeNote) ? "Updated with PZTools" : changeNote)}\"")
                .AppendLine("}")
                .ToString();
            File.WriteAllText(packageRoot + ".vdf", vdf, new UTF8Encoding(false));
            return packageRoot;
        }

        private static void CopyProject(string sourceRoot, string destinationRoot, CancellationToken ct)
        {
            Directory.CreateDirectory(destinationRoot);
            foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(sourceRoot, file);
                var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (parts.Any(p => p.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                                   p.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                                   p.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                                   p.Equals(".vs", StringComparison.OrdinalIgnoreCase) ||
                                   p.Equals(".vscode", StringComparison.OrdinalIgnoreCase) ||
                                   p.Equals(".pztools", StringComparison.OrdinalIgnoreCase) ||
                                   p.Equals("Workshop", StringComparison.OrdinalIgnoreCase)))
                    continue;

                var fileName = Path.GetFileName(relative);
                if (fileName.Equals(".luarc.json", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Equals(".gitignore", StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith(".code-workspace", StringComparison.OrdinalIgnoreCase))
                    continue;

                var destination = Path.Combine(destinationRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination, overwrite: true);
            }
        }

        private static string? ResolvePreview(string root, ModInfo info)
        {
            foreach (var entry in info.Posters.Concat(new[] { info.Icon }))
            {
                var path = ModInfoParser.ResolveAssetPath(root, entry);
                if (path != null)
                    return path;
            }
            return null;
        }

        private static string SingleLine(string value) => value.Replace("\r", " ").Replace("\n", " ").Trim();
        private static string EscapeVdf(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r\n", "\\n").Replace("\r", "\\n").Replace("\n", "\\n");

        private static string? ReadVdfValue(string path, string key)
        {
            var match = Regex.Match(File.ReadAllText(path), $"\\\"{Regex.Escape(key)}\\\"\\s+\\\"(?<value>[^\\\"]*)\\\"", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups["value"].Value : null;
        }
    }
}
