using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using PZTools.Core.Functions.Zomboid;
using PZTools.Core.Models;

namespace PZTools.Core.Functions.Projects
{
    public sealed class PlaytestSessionResult
    {
        public required PlaytestSessionWorkspace Workspace { get; init; }
        public List<ProjectDiagnostic> Diagnostics { get; } = new();
        public int StartedClients { get; set; }
        public bool WasStopped { get; set; }
    }

    public sealed class PlaytestSessionRunner : IDisposable
    {
        private readonly List<Process> _processes = new();
        private readonly object _processGate = new();
        private readonly CancellationTokenSource _stopCts = new();
        private TaskCompletionSource _serverReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _runCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private volatile bool _runStarted;

        public event EventHandler<string>? Output;
        public event Action<Process>? FirstClientStarted;
        internal event Action<int, Process>? ClientStarted;
        public bool DockFirstClient { get; init; }
        public bool IsRunning => ProcessSnapshot().Any(IsAlive);
        internal Action<PlaytestSessionWorkspace>? PrepareAutomation { get; init; }
        internal Func<PlaytestSessionWorkspace, CancellationToken, Task>? RunAutomation { get; init; }

        public async Task<PlaytestSessionResult> RunAsync(
            ModProject project,
            PlaytestProfile profile,
            string clientBuildRoot,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(project);
            ArgumentNullException.ThrowIfNull(profile);
            var validation = PlaytestProfileStore.Validate(profile);
            if (validation.Count > 0)
                throw new InvalidDataException(string.Join(Environment.NewLine, validation));
            if (!Directory.Exists(clientBuildRoot))
                throw new DirectoryNotFoundException(clientBuildRoot);

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopCts.Token);
            var workspace = await Task.Run(() => PlaytestWorkspaceService.Prepare(project, profile), linked.Token);
            _runStarted = true;
            var result = new PlaytestSessionResult { Workspace = workspace };
            try
            {
                Emit($"Preparing isolated playtest workspace: {workspace.RootPath}");
                await DeployProfileAsync(project, profile, workspace, linked.Token);
                foreach (var cache in workspace.ClientCachePaths)
                {
                    PlaytestClientConfig.Configure(cache, profile, ZomboidGame.GameUserDirectory,
                        DockFirstClient && cache == workspace.ClientCachePaths[0]);
                    Emit($"Configured client display: {(DockFirstClient && cache == workspace.ClientCachePaths[0] ? PlaytestWindowMode.Windowed : profile.WindowMode)}, {profile.WindowWidth}x{profile.WindowHeight}.");
                }

                if (profile.Mode == PlaytestMode.DedicatedServer)
                    PlaytestServerConfig.Write(profile, workspace, project.ModInfo.Id, project);
                if (profile.Mode == PlaytestMode.DedicatedServer && profile.NoSteam && profile.AutoConnectClients)
                    PlaytestLocalConnection.Prepare(profile, workspace);
                PrepareAutomation?.Invoke(workspace);

                if (profile.Mode == PlaytestMode.DedicatedServer)
                {
                    var parity = await Task.Run(() => ClientServerParityService.Compare(
                        Path.Combine(workspace.ServerCachePath, "mods"),
                        workspace.ClientCachePaths.Select(x => Path.Combine(x, "mods"))), linked.Token);
                    result.Diagnostics.AddRange(parity);
                    if (parity.Count > 0)
                        return result;

                    var server = StartServer(profile, workspace, clientBuildRoot);
                    var readyOrTimeout = await Task.WhenAny(
                        _serverReady.Task,
                        Task.Delay(TimeSpan.FromSeconds(profile.ServerStartupTimeoutSeconds), linked.Token));
                    linked.Token.ThrowIfCancellationRequested();
                    if (server.HasExited)
                        throw new InvalidOperationException($"Dedicated server exited before clients started (exit code {server.ExitCode}).");
                    if (readyOrTimeout != _serverReady.Task)
                    {
                        if (RunAutomation is not null) throw new TimeoutException("Dedicated server readiness timed out.");
                        Emit("Server readiness was not detected before the timeout; starting clients so the live output can reveal the cause.");
                    }

                    var clients = new List<Process>();
                    for (var i = 0; i < profile.ClientCount; i++)
                    {
                        clients.Add(StartClient(profile, workspace.ClientCachePaths[i], clientBuildRoot, i + 1));
                        result.StartedClients++;
                        await Task.Delay(750, linked.Token);
                    }
                    if (RunAutomation is not null)
                    {
                        await AwaitAutomationAsync(workspace, clients.Append(server).ToArray(), linked.Token);
                        CollectSessionDiagnostics(project, workspace, result);
                        return result;
                    }
                    await Task.WhenAll(clients.Select(x => x.WaitForExitAsync(linked.Token)));
                    var failedClient = clients.FirstOrDefault(x => SafeExitCode(x) != 0);
                    if (failedClient is not null)
                        throw new InvalidOperationException($"A Project Zomboid client exited unexpectedly (exit code {SafeExitCode(failedClient)}). Review the session output and isolated console.txt.");
                    await StopServerGracefullyAsync(server, linked.Token);
                }
                else
                {
                    var client = StartClient(profile, workspace.ClientCachePaths[0], clientBuildRoot, 1);
                    result.StartedClients = 1;
                    if (RunAutomation is not null)
                    {
                        await AwaitAutomationAsync(workspace, new[] { client }, linked.Token);
                        CollectSessionDiagnostics(project, workspace, result);
                        return result;
                    }
                    await client.WaitForExitAsync(linked.Token);
                    if (SafeExitCode(client) != 0)
                        throw new InvalidOperationException($"Project Zomboid exited unexpectedly (exit code {SafeExitCode(client)}). Review the session output and isolated console.txt.");
                }

                await Task.Run(() => CollectSessionDiagnostics(project, workspace, result), linked.Token);
                return result;
            }
            catch (OperationCanceledException) when (_stopCts.IsCancellationRequested)
            {
                result.WasStopped = true;
                CollectSessionDiagnostics(project, workspace, result);
                return result;
            }
            finally
            {
                try
                {
                    await StopAllProcessesAsync();
                    PlaytestWorkspaceService.Cleanup(workspace, profile.KeepSessionData);
                }
                finally
                {
                    _runCompleted.TrySetResult();
                }
            }
        }

        public async Task StopAsync()
        {
            _stopCts.Cancel();
            await StopAllProcessesAsync();
            if (_runStarted)
                await _runCompleted.Task;
        }

        private async Task AwaitAutomationAsync(PlaytestSessionWorkspace workspace, Process[] processes, CancellationToken ct)
        {
            using var monitoring = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var execution = RunAutomation!(workspace, monitoring.Token);
            var exited = Task.WhenAny(processes.Select(p => p.WaitForExitAsync(monitoring.Token)));
            if (await Task.WhenAny(execution, exited) != execution)
            {
                monitoring.Cancel();
                try { await execution; } catch (OperationCanceledException) { }
                ct.ThrowIfCancellationRequested();
                throw new InvalidOperationException("A game process exited before the test run completed.");
            }
            try { await execution; }
            finally { monitoring.Cancel(); }
        }

        private async Task DeployProfileAsync(ModProject project, PlaytestProfile profile,
            PlaytestSessionWorkspace workspace, CancellationToken cancellationToken)
        {
            var snapshot = await Task.Run(() => DeploymentSnapshotService.Capture(project, profile, workspace.RootPath, cancellationToken), cancellationToken);
            workspace.SourceSnapshotId = snapshot.Id;
            workspace.PayloadManifestPath = snapshot.ManifestPath;
            Emit($"Runtime snapshot {snapshot.Id}: {snapshot.Bytes:N0} bytes; manifest: {snapshot.ManifestPath}");
            if (profile.Mode == PlaytestMode.DedicatedServer && !profile.NoSteam && profile.Dependencies.Any(d => d.Enabled && !string.IsNullOrWhiteSpace(d.WorkshopId)))
                Emit("Steam Workshop dependencies remain outside the local runtime snapshot guarantee.");
            var caches = workspace.ClientCachePaths.Append(workspace.ServerCachePath).Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var cache in caches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var modsRoot = Path.Combine(cache, "mods");
                foreach (var payload in snapshot.Payloads)
                    await ProjectDeployer.DeploySnapshot(payload, modsRoot, cancellationToken);
                if (workspace.ClientCachePaths.Contains(cache, StringComparer.OrdinalIgnoreCase))
                    WriteClientModList(cache, profile, project);
            }
        }

        private Process StartServer(PlaytestProfile profile, PlaytestSessionWorkspace workspace, string clientBuildRoot)
        {
            var launcher = PlaytestServerLauncher.Resolve(profile.ServerInstallPath, clientBuildRoot);
            var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            var arguments = $"-cachedir={Quote(workspace.ServerCachePath)} -servername {Quote(profile.ServerName)} " +
                            $"-port {profile.ServerPort} -udpport {profile.ServerPort + 1} -adminusername PZToolsAdmin " +
                            $"-adminpassword {password} {(profile.NoSteam ? "-nosteam -modfolders mods" : "-modfolders workshop,steam,mods")} " +
                            (profile.LocalServerOnly ? "-ip 127.0.0.1 " : "") + profile.LaunchArguments;
            Emit($"Starting dedicated server '{profile.ServerName}' on 127.0.0.1:{profile.ServerPort}.");

            var start = PlaytestServerLauncher.Create(launcher, arguments, profile.NoSteam);
            start.RedirectStandardInput = true;
            return StartProcess(start, "SERVER", detectServerReady: true);
        }

        private Process StartClient(PlaytestProfile profile, string cachePath, string clientBuildRoot, int number)
        {
            var executable = Path.Combine(clientBuildRoot, "ProjectZomboid64.exe");
            if (!File.Exists(executable))
                throw new FileNotFoundException("Project Zomboid client executable was not found.", executable);
            var connect = profile.Mode == PlaytestMode.DedicatedServer && profile.AutoConnectClients && !profile.NoSteam
                ? $" +connect 127.0.0.1:{profile.ServerPort}"
                : "";
            var modFolders = profile.Mode == PlaytestMode.DedicatedServer && profile.NoSteam ? "mods" : "mods,steam,workshop";
            var arguments = $"{profile.LaunchArguments} -cachedir={Quote(cachePath)} -modfolders {modFolders}" +
                            (profile.Mode == PlaytestMode.DedicatedServer && profile.NoSteam ? " -nosteam" : "") + connect;
            Emit($"Starting client {number} with isolated cache '{cachePath}'.");
            var client = StartProcess(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                WorkingDirectory = clientBuildRoot
            }, $"CLIENT {number}", detectServerReady: false);
            ClientStarted?.Invoke(number, client);
            if (number == 1) FirstClientStarted?.Invoke(client);
            return client;
        }

        private Process StartProcess(ProcessStartInfo start, string label, bool detectServerReady)
        {
            start.UseShellExecute = false;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            start.CreateNoWindow = true;
            var process = new Process { StartInfo = start, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => HandleOutput(label, e.Data, detectServerReady);
            process.ErrorDataReceived += (_, e) => HandleOutput(label, e.Data, detectServerReady);
            process.Exited += (_, _) => Emit($"{label} exited with code {SafeExitCode(process)}.");
            if (!process.Start())
                throw new InvalidOperationException($"Windows did not start {label}.");
            lock (_processGate)
                _processes.Add(process);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return process;
        }

        private void HandleOutput(string label, string? line, bool detectServerReady)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;
            Emit($"[{label}] {line}");
            if (detectServerReady && (line.Contains("SERVER STARTED", StringComparison.OrdinalIgnoreCase) ||
                                      line.Contains("Steam server started", StringComparison.OrdinalIgnoreCase) ||
                                      line.Contains("listening on port", StringComparison.OrdinalIgnoreCase)))
                _serverReady.TrySetResult();
        }

        private async Task StopServerGracefullyAsync(Process server, CancellationToken ct)
        {
            if (server.HasExited) throw new InvalidOperationException("The server exited before it could save the fixture.");
            Emit("Clients closed. Saving and stopping the dedicated server...");
            await server.StandardInput.WriteLineAsync("quit");
            await server.StandardInput.FlushAsync(ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            try { await server.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            { throw new TimeoutException("The server did not finish saving. Session data was retained, but no fixture was captured."); }
            if (server.ExitCode != 0) throw new InvalidOperationException("The server did not close cleanly. No fixture was captured.");
        }

        private static void WriteClientModList(string cache, PlaytestProfile profile, ModProject project)
        {
            var ids = DependencyLoadOrder.Resolve(project, profile)
                .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (profile.Mode == PlaytestMode.DedicatedServer && profile.NoSteam && profile.AutoConnectClients)
                ids.Add(PlaytestLocalConnection.ModId);
            if (profile.Build >= 42)
                ids = ids.Select(x => x.StartsWith('\\') ? x : "\\" + x).ToList();
            var lines = new List<string> { "VERSION = 1,", "", "mods", "{" };
            lines.AddRange(ids.Select(x => $"    mod = {x},"));
            lines.AddRange(new[] { "}", "", "maps", "{", "}" });
            var modsDirectory = Path.Combine(cache, "mods");
            Directory.CreateDirectory(modsDirectory);
            File.WriteAllLines(Path.Combine(modsDirectory, "default.txt"), lines);

            var savesRoot = Path.Combine(cache, "Saves");
            if (!Directory.Exists(savesRoot))
                return;
            foreach (var saveDirectory in Directory.EnumerateDirectories(savesRoot, "*", SearchOption.AllDirectories)
                         .Where(x => File.Exists(Path.Combine(x, "map_ver.bin")) || File.Exists(Path.Combine(x, "players.db"))))
                File.WriteAllLines(Path.Combine(saveDirectory, "mods.txt"), lines);
        }

        private static void CollectSessionDiagnostics(ModProject project, PlaytestSessionWorkspace workspace, PlaytestSessionResult result)
        {
            foreach (var cache in workspace.ClientCachePaths.Append(workspace.ServerCachePath))
            {
                foreach (var name in new[] { "console.txt", "server-console.txt" })
                {
                    var log = Path.Combine(cache, name);
                    if (File.Exists(log))
                        result.Diagnostics.AddRange(GameLogAnalyzer.FindProjectIssues(project, new GameLogSession(log, 0, DateTime.UtcNow)));
                }
            }
        }

        private async Task StopAllProcessesAsync()
        {
            foreach (var process in ProcessSnapshot().Where(IsAlive))
                await StopProcessAsync(process);
        }

        private Process[] ProcessSnapshot()
        {
            lock (_processGate)
                return _processes.ToArray();
        }

        private static bool IsAlive(Process process)
        {
            try
            {
                return !process.HasExited;
            }
            catch (ObjectDisposedException) { return false; }
            catch (InvalidOperationException) { return false; }
        }

        private static async Task StopProcessAsync(Process process)
        {
            try
            {
                if (process.HasExited)
                    return;
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            catch { }
        }

        private static string Quote(string value) => "\"" + value.Replace("\"", "") + "\"";
        private static int SafeExitCode(Process process)
        {
            try
            {
                return process.ExitCode;
            }
            catch { return -1; }
        }
        private void Emit(string message) => Output?.Invoke(this, message);

        public void Dispose()
        {
            _stopCts.Cancel();
            foreach (var process in ProcessSnapshot())
                process.Dispose();
            _stopCts.Dispose();
        }
    }
}
