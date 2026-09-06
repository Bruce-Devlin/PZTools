using System.IO;
using PZTools.Core.Models;
using PZTools.Core.Functions.Tester;
using PZTools.Core.Functions.Zomboid;

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

                if (command == "init-tests")
                {
                    ModTestService.Scaffold(project);
                    await output.WriteLineAsync("Created missing test examples in .pztests.");
                    return 0;
                }
                if (command is "unit" or "game-tests")
                {
                    var filter = ReadOption(args, "--filter");
                    Models.Test.ModTestReport report;
                    if (command == "unit") report = await ModTestService.RunUnitAsync(project, filter, cancellationToken);
                    else
                    {
                        var profiles = PlaytestProfileStore.Load(project);
                        var selected = ReadOption(args, "--profile");
                        var profile = selected is null ? profiles[0] : profiles.SingleOrDefault(p => p.Name == selected || p.Id.ToString() == selected)
                            ?? throw new ArgumentException("Playtest profile was not found: " + selected);
                        var root = ReadOption(args, "--game-root") ?? (ZomboidGame.GameMode == "Managed"
                            ? Path.Combine(ZomboidGame.GameDirectory, profile.Build.ToString(System.Globalization.CultureInfo.InvariantCulture)) : ZomboidGame.GameDirectory);
                        var limit = ReadOption(args, "--timeout");
                        if (limit is not null && !int.TryParse(limit, out _)) throw new ArgumentException("--timeout must be an integer in seconds.");
                        report = await GameTestService.RunAsync(project, profile, root, filter, limit is null ? 300 : int.Parse(limit), line => output.WriteLine(line), cancellationToken);
                    }
                    foreach (var test in report.Tests) await output.WriteLineAsync($"{test.File}(1,1): {(test.Status == "passed" ? "info" : "error")} PZTEST: {test.Name}: {test.Status} {SingleLine(test.Message)}");
                    foreach (var problem in report.Errors) await error.WriteLineAsync("Runner: " + problem);
                    await output.WriteLineAsync($"{(report.Passed ? "PASS" : "FAIL")}: {report.Tests.Count} tests. Reports: {report.ArtifactDirectory}");
                    return report.Passed ? 0 : report.Errors.Count > 0 ? 2 : 1;
                }

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
            await error.WriteLineAsync("Usage: PZTools.exe --project-task <health|deploy|init-tests|unit|game-tests> [--project <folder>] [--filter <file substring>] [--profile <name>] [--game-root <folder>] [--timeout <seconds>]");
            return 2;
        }
    }
}
