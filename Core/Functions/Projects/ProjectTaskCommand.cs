using System.IO;
using PZTools.Core.Models;

namespace PZTools.Core.Functions.Projects
{
    public static class ProjectTaskCommand
    {
        public const string Argument = "--project-task";

        public static bool IsRequested(IReadOnlyList<string> args)
            => args.Any(x => x.Equals(Argument, StringComparison.OrdinalIgnoreCase));

        public static async Task<int> RunAsync(IReadOnlyList<string> args, TextWriter output, TextWriter error, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(args);
            ArgumentNullException.ThrowIfNull(output);
            ArgumentNullException.ThrowIfNull(error);

            try
            {
                var commandIndex = FindArgument(args, Argument);
                if (commandIndex < 0 || commandIndex + 1 >= args.Count)
                    return await UsageErrorAsync(error, "Missing project task. Expected 'health' or 'deploy'.");

                var command = args[commandIndex + 1].Trim().ToLowerInvariant();
                var projectPath = ReadOption(args, "--project") ?? Directory.GetCurrentDirectory();
                var project = LoadProject(projectPath);

                return command switch
                {
                    "health" or "test" => await RunHealthAsync(project, output, cancellationToken),
                    "deploy" => await RunDeployAsync(project, output, cancellationToken),
                    _ => await UsageErrorAsync(error, $"Unknown project task '{command}'. Expected 'health' or 'deploy'.")
                };
            }
            catch (OperationCanceledException)
            {
                await error.WriteLineAsync("PZTools project task was cancelled.");
                return 2;
            }
            catch (Exception ex)
            {
                await error.WriteLineAsync($"PZTools project task failed: {ex.Message}");
                return 2;
            }
        }

        private static async Task<int> RunHealthAsync(ModProject project, TextWriter output, CancellationToken cancellationToken)
        {
            var report = await ProjectHealthService.AnalyzeAsync(project, validateLua: true, cancellationToken);
            WriteDiagnostics(report, output);
            await output.WriteLineAsync($"PZTools: {project.Name}: {report.Summary}. Lua: {report.LuaFileCount}; ZedScript: {report.ScriptFileCount}.");
            return report.IsReadyToDeploy ? 0 : 1;
        }

        private static async Task<int> RunDeployAsync(ModProject project, TextWriter output, CancellationToken cancellationToken)
        {
            var report = await ProjectHealthService.AnalyzeAsync(project, validateLua: true, cancellationToken);
            WriteDiagnostics(report, output);
            if (!report.IsReadyToDeploy)
            {
                await output.WriteLineAsync($"PZTools: Deployment blocked: {report.Summary}.");
                return 1;
            }

            var deployRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Zomboid", "mods");
            await ProjectDeployer.DeployProject(project, deployRoot, cancellationToken);
            await output.WriteLineAsync($"PZTools: Deployed {project.Name} to {Path.Combine(deployRoot, new DirectoryInfo(project.RootPath).Name)}");
            return 0;
        }

        private static ModProject LoadProject(string projectPath)
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectPath.Trim()));
            if (!Directory.Exists(root))
                throw new DirectoryNotFoundException($"Project folder was not found: {root}");

            var originalProjectsRoot = ProjectEngine.ProjectsRootPath;
            try
            {
                ProjectEngine.ProjectsRootPath = Directory.GetParent(root)?.FullName
                    ?? throw new InvalidOperationException("The filesystem root cannot be used as a mod project.");
                var project = ProjectEngine.LoadProjects().FirstOrDefault(candidate =>
                    Path.GetFullPath(candidate.RootPath).Equals(root, StringComparison.OrdinalIgnoreCase));
                return project ?? throw new InvalidOperationException(
                    $"'{root}' is not a recognised PZTools mod project. Expected mod.info at the root, in a numeric build folder such as 42, or in common.");
            }
            finally
            {
                ProjectEngine.ProjectsRootPath = originalProjectsRoot;
            }
        }

        private static void WriteDiagnostics(ProjectHealthReport report, TextWriter output)
        {
            foreach (var diagnostic in report.Diagnostics
                         .OrderByDescending(x => x.Severity)
                         .ThenBy(x => x.FilePath)
                         .ThenBy(x => x.Line))
            {
                var path = GetDisplayPath(report.Project.RootPath, diagnostic.FilePath);
                var line = Math.Max(diagnostic.Line ?? 1, 1);
                var level = diagnostic.Severity.ToString().ToLowerInvariant();
                output.WriteLine($"{path}({line},1): {level} {diagnostic.Code}: {SingleLine(diagnostic.Message)}");
            }
        }

        private static string GetDisplayPath(string projectRoot, string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return ".";
            try
            {
                var relative = Path.GetRelativePath(projectRoot, Path.GetFullPath(filePath));
                return relative.StartsWith("..", StringComparison.Ordinal) ? filePath : relative;
            }
            catch
            {
                return filePath;
            }
        }

        private static string SingleLine(string value)
            => value.Replace('\r', ' ').Replace('\n', ' ').Trim();

        private static int FindArgument(IReadOnlyList<string> args, string name)
        {
            for (var i = 0; i < args.Count; i++)
                if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        private static string? ReadOption(IReadOnlyList<string> args, string name)
        {
            var index = FindArgument(args, name);
            return index >= 0 && index + 1 < args.Count ? args[index + 1] : null;
        }

        private static async Task<int> UsageErrorAsync(TextWriter error, string message)
        {
            await error.WriteLineAsync(message);
            await error.WriteLineAsync("Usage: PZTools.exe --project-task <health|deploy> [--project <folder>]");
            return 2;
        }
    }
}
