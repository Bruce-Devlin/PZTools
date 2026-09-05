using System.IO;
using System.IO.Pipes;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PZTools.Core.Functions.Projects;
using PZTools.Core.Functions.Tester;
using PZTools.Core.Functions.Zomboid;
using PZTools.Core.Models;
using PZTools.Core.Windows;

namespace PZTools.Core.Functions.Agent
{
    public sealed class AgentMcpEditorServer : IAsyncDisposable
    {
        private readonly MainWindow _window;
        private readonly string _projectRoot;
        private readonly string _pipeName;
        private readonly string _lifetimePipeName;
        private readonly CancellationTokenSource _shutdown = new();
        private Task? _listenerTask;
        private Task? _lifetimeListenerTask;

        public AgentMcpEditorServer(MainWindow window)
        {
            _window = window;
            _projectRoot = Path.GetFullPath(window.ModProject.RootPath);
            _pipeName = AgentMcpConfiguration.GetPipeName(_projectRoot);
            _lifetimePipeName = AgentMcpConfiguration.GetLifetimePipeName(_projectRoot);
        }

        public void Start()
        {
            if (_listenerTask != null)
                return;
            _listenerTask = ListenAsync(_shutdown.Token);
            _lifetimeListenerTask = ListenForLifetimeClientsAsync(_shutdown.Token);
        }

        public async ValueTask DisposeAsync()
        {
            _shutdown.Cancel();
            if (_listenerTask != null)
            {
                try
                {
                    await _listenerTask;
                }
                catch (OperationCanceledException) { }
            }
            if (_lifetimeListenerTask != null)
            {
                try
                {
                    await _lifetimeListenerTask;
                }
                catch (OperationCanceledException) { }
            }
            _shutdown.Dispose();
        }

        private async Task ListenForLifetimeClientsAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var pipe = new NamedPipeServerStream(
                    _lifetimePipeName,
                    PipeDirection.Out,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                try
                {
                    await pipe.WaitForConnectionAsync(cancellationToken);
                    _ = HoldLifetimeConnectionAsync(pipe, cancellationToken);
                    pipe = null!;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
                catch (Exception ex)
                {
                    await Logger.Console.Log($"Agent MCP lifetime bridge error: {ex.Message}", Logger.Console.LogLevel.Warning);
                }
                finally
                {
                    pipe?.Dispose();
                }
            }
        }

        private static async Task HoldLifetimeConnectionAsync(Stream pipe, CancellationToken cancellationToken)
        {
            await using (pipe)
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            }
        }

        private async Task ListenAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await using var pipe = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                try
                {
                    await pipe.WaitForConnectionAsync(cancellationToken);
                    await HandleClientAsync(pipe, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
                catch (Exception ex)
                {
                    await Logger.Console.Log($"Agent MCP bridge error: {ex.Message}", Logger.Console.LogLevel.Warning);
                }
            }
        }

        private async Task HandleClientAsync(Stream stream, CancellationToken cancellationToken)
        {
            using var reader = new StreamReader(stream, new UTF8Encoding(false), false, 4096, leaveOpen: true);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
            while (!cancellationToken.IsCancellationRequested && stream.CanRead)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line == null)
                    break;
                AgentResponse response;
                try
                {
                    var request = JsonConvert.DeserializeObject<AgentRequest>(line)
                        ?? throw new InvalidOperationException("The Agent MCP request was empty.");
                    response = new AgentResponse { Id = request.Id, Result = await DispatchAsync(request, cancellationToken) };
                }
                catch (Exception ex)
                {
                    response = new AgentResponse { Error = ex.Message };
                }
                await writer.WriteLineAsync(JsonConvert.SerializeObject(response));
            }
        }

        private async Task<object?> DispatchAsync(AgentRequest request, CancellationToken cancellationToken)
        {
            var settings = Config.GetAppSettings();
            if (!settings.AgentMcpEnabled)
                throw new InvalidOperationException("Agent MCP is disabled in PZTools settings.");

            var args = request.Arguments ?? new JObject();
            switch (request.Method)
            {
                case "capabilities":
                    return new
                    {
                        server = "PZTools MCP",
                        project = _window.ModProject.Name,
                        projectRoot = _projectRoot,
                        pipe = _pipeName,
                        enabledTools = AgentMcpConfiguration.GetEnabledTools(settings),
                        editorConnected = true
                    };
                case "editor_state":
                    Require(settings.AgentMcpAllowEditorControl, "Editor control");
                    return await _window.GetAgentEditorStateAsync();
                case "list_files":
                    Require(settings.AgentMcpAllowEditorControl, "Editor control");
                    return ListFiles(args.Value<string>("path"), args.Value<string>("pattern"));
                case "read_file":
                    Require(settings.AgentMcpAllowEditorControl, "Editor control");
                    return await ReadFileAsync(args.Value<string>("path"), cancellationToken);
                case "open_file":
                    Require(settings.AgentMcpAllowEditorControl, "Editor control");
                    return await _window.OpenFileForAgentAsync(ResolvePath(args.Value<string>("path"), mustExist: true), args.Value<int?>("line"));
                case "write_file":
                    Require(settings.AgentMcpAllowProjectWrites, "Project writes");
                    return await WriteFileAsync(args.Value<string>("path"), args.Value<string>("content") ?? string.Empty, cancellationToken);
                case "test_lua":
                    Require(settings.AgentMcpAllowTesting, "Testing");
                    return await LuaTester.TestFile(ResolvePath(args.Value<string>("path"), mustExist: true), logResult: false);
                case "check_project":
                    Require(settings.AgentMcpAllowTesting, "Testing");
                    return await ProjectHealthService.AnalyzeAsync(_window.ModProject, validateLua: true, cancellationToken);
                case "deploy_project":
                    Require(settings.AgentMcpAllowDeployment, "Deployment");
                    return await DeployAsync(cancellationToken);
                case "start_game":
                    Require(settings.AgentMcpAllowGameControl, "Game control");
                    return await StartGameAsync(args.Value<string>("build"), args.Value<string>("arguments"));
                case "stop_game":
                    Require(settings.AgentMcpAllowGameControl, "Game control");
                    await ZomboidGame.StopGame();
                    return GetGameStatus();
                case "game_status":
                    Require(settings.AgentMcpAllowGameControl, "Game control");
                    return GetGameStatus();
                case "game_log":
                    Require(settings.AgentMcpAllowGameControl, "Game control");
                    return await ReadGameLogAsync(Math.Clamp(args.Value<int?>("lines") ?? 200, 1, 2000), cancellationToken);
                default:
                    throw new InvalidOperationException($"Unknown PZTools operation '{request.Method}'.");
            }
        }

        private IReadOnlyList<object> ListFiles(string? relativePath, string? pattern)
        {
            var root = ResolvePath(relativePath, mustExist: true, allowDirectory: true);
            if (!Directory.Exists(root))
                throw new DirectoryNotFoundException(root);
            var searchPattern = string.IsNullOrWhiteSpace(pattern) ? "*" : pattern;
            return Directory.EnumerateFileSystemEntries(root, searchPattern, SearchOption.AllDirectories)
                .Take(5000)
                .Select(path => (object)new
                {
                    path = Path.GetRelativePath(_projectRoot, path),
                    type = Directory.Exists(path) ? "directory" : "file",
                    bytes = File.Exists(path) ? new FileInfo(path).Length : (long?)null
                }).ToArray();
        }

        private async Task<object> ReadFileAsync(string? relativePath, CancellationToken cancellationToken)
        {
            var path = ResolvePath(relativePath, mustExist: true);
            if (!File.Exists(path))
                throw new FileNotFoundException("Project file was not found.", path);
            return new
            {
                path = Path.GetRelativePath(_projectRoot, path),
                content = await File.ReadAllTextAsync(path, cancellationToken)
            };
        }

        private async Task<object> WriteFileAsync(string? relativePath, string content, CancellationToken cancellationToken)
        {
            var path = ResolvePath(relativePath, mustExist: false);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, content, new UTF8Encoding(false), cancellationToken);
            await _window.RefreshFileForAgentAsync(path);
            return new
            {
                path = Path.GetRelativePath(_projectRoot, path),
                bytes = Encoding.UTF8.GetByteCount(content),
                saved = true
            };
        }

        private async Task<object> DeployAsync(CancellationToken cancellationToken)
        {
            var health = await ProjectHealthService.AnalyzeAsync(_window.ModProject, validateLua: true, cancellationToken);
            if (!health.IsReadyToDeploy)
                return new
                {
                    deployed = false,
                    health.Summary,
                    health.ErrorCount,
                    health.WarningCount,
                    diagnostics = health.Diagnostics
                };
            await ProjectDeployer.DeployProject(DeployFolder.Mods, cancellationToken);
            return new
            {
                deployed = true,
                health.Summary,
                health.ErrorCount,
                health.WarningCount
            };
        }

        private async Task<object> StartGameAsync(string? build, string? arguments)
        {
            if (ZomboidGame.IsRunning)
                return GetGameStatus();
            var settings = Config.GetAppSettings();
            var health = await ProjectHealthService.AnalyzeAsync(_window.ModProject, validateLua: true);
            if (!health.IsReadyToDeploy)
                return new
                {
                    started = false,
                    health.Summary,
                    health.ErrorCount,
                    health.WarningCount,
                    diagnostics = health.Diagnostics
                };

            var deployedRoot = Path.Combine(ZomboidGame.GameUserDirectory, "mods", new DirectoryInfo(_projectRoot).Name);
            var deployment = DeploymentManifestService.Verify(_projectRoot, deployedRoot, _window.ModProject.ModInfo.Id);
            if (deployment.Count > 0)
            {
                if (!settings.AgentMcpAllowDeployment)
                    return new
                    {
                        started = false,
                        reason = "The playtest copy is missing or stale and Agent MCP deployment is disabled.",
                        diagnostics = deployment
                    };
                await ProjectDeployer.DeployProject(DeployFolder.Mods);
            }

            var root = ZomboidGame.GameDirectory;
            var selectedBuild = string.IsNullOrWhiteSpace(build) ? Config.GetVariable(VariableType.user, "lastUsedGameBuild") : build;
            if (ZomboidGame.GameMode.Equals("Managed", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(selectedBuild))
            {
                var matchingBuild = Directory.Exists(root)
                    ? Directory.EnumerateDirectories(root)
                        .FirstOrDefault(path => Path.GetFileName(path).Equals(selectedBuild, StringComparison.OrdinalIgnoreCase))
                    : null;
                if (matchingBuild == null)
                    throw new ArgumentException($"Managed Project Zomboid build '{selectedBuild}' was not found.", nameof(build));
                root = matchingBuild;
            }
            var executable = Path.Combine(root, "ProjectZomboid64.exe");
            if (!File.Exists(executable))
                throw new FileNotFoundException("Project Zomboid executable was not found.", executable);

            var launchArguments = string.IsNullOrWhiteSpace(arguments) ? "-debug" : arguments;
            _ = ZomboidGame.StartGame(executable, launchArguments);
            return new
            {
                started = true,
                executable,
                arguments = launchArguments,
                debug = launchArguments.Contains("-debug", StringComparison.OrdinalIgnoreCase)
            };
        }

        private static object GetGameStatus()
        {
            var process = ZomboidGame.GameProcess;
            return new
            {
                running = ZomboidGame.IsRunning,
                starting = ZomboidGame.IsGameStarting,
                processId = process is { HasExited: false } ? process.Id : (int?)null,
                gameDirectory = ZomboidGame.GameDirectory,
                logPath = Path.Combine(ZomboidGame.GameUserDirectory, "console.txt")
            };
        }

        private static async Task<object> ReadGameLogAsync(int lineCount, CancellationToken cancellationToken)
        {
            var path = Path.Combine(ZomboidGame.GameUserDirectory, "console.txt");
            if (!File.Exists(path))
                return new
                {
                    path,
                    exists = false,
                    lines = Array.Empty<string>()
                };
            var lines = await File.ReadAllLinesAsync(path, cancellationToken);
            return new
            {
                path,
                exists = true,
                lines = lines.TakeLast(lineCount).ToArray()
            };
        }

        private string ResolvePath(string? relativePath, bool mustExist, bool allowDirectory = false)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                if (allowDirectory)
                    return _projectRoot;
                throw new ArgumentException("A project-relative path is required.", nameof(relativePath));
            }
            if (Path.IsPathRooted(relativePath))
                throw new ArgumentException("Use a path relative to the active PZTools project.", nameof(relativePath));
            var pathParts = relativePath.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);
            if (pathParts.Any(part => part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
                throw new ArgumentException("The project-relative path contains invalid file-name characters.", nameof(relativePath));
            var full = Path.GetFullPath(Path.Combine(_projectRoot, relativePath));
            var prefix = Path.TrimEndingDirectorySeparator(_projectRoot) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("The requested path is outside the active PZTools project.");
            var current = _projectRoot;
            foreach (var part in pathParts)
            {
                current = Path.Combine(current, part);
                if ((File.Exists(current) || Directory.Exists(current)) &&
                    File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                    throw new UnauthorizedAccessException("Agent MCP does not follow links or junctions inside the project.");
            }
            if (mustExist && !File.Exists(full) && !Directory.Exists(full))
                throw new FileNotFoundException("The requested project path does not exist.", relativePath);
            return full;
        }

        private static void Require(bool enabled, string capability)
        {
            if (!enabled)
                throw new UnauthorizedAccessException($"{capability} is disabled in PZTools Agent MCP settings.");
        }

        private sealed class AgentRequest
        {
            public string? Id { get; init; }
            public string Method { get; init; } = string.Empty;
            public JObject? Arguments { get; init; }
        }

        private sealed class AgentResponse
        {
            public string? Id { get; init; }
            public object? Result { get; init; }
            public string? Error { get; init; }
        }
    }
}
