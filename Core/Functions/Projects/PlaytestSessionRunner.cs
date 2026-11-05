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
        private RunSession? _session;
        private PlaytestSessionWorkspace? _workspace;
        public RunSession? Session { get; init; }
        public string SessionKind { get; init; } = "manual";
        internal ModProject? SourceProject { get; init; }
        public string? RestartOf { get; init; }
        public string? CurrentSessionId => _session?.Id;
        private bool _saving;
        private readonly TaskCompletionSource _serverSaved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, PlaytestSessionRunner> Active = new();
        public static async Task SaveAndShutdownAsync(string project, string sessionId)
        {
            var session = SessionRegistry.Default.Get(sessionId);
            if (!session.ProjectPath.Equals(Path.GetFullPath(project), StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("Session belongs to another project.");
            if (!Active.TryGetValue(session.Id, out var runner)) throw new InvalidOperationException("Session is not controlled by this editor. Quit normally in its game windows; no process was terminated.");
            if (runner.SessionKind == "automated") throw new InvalidOperationException("Finish automated exercise before saving. Start a retained playtest session for save/restart verification.");
            if (runner._saving) throw new InvalidOperationException("Save and shutdown is already requested.");
            runner._saving = true;
            SessionLifecycleService.Request(runner._session!);
            await runner._runCompleted.Task.WaitAsync(TimeSpan.FromSeconds(120));
        }

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
            profile = Newtonsoft.Json.JsonConvert.DeserializeObject<PlaytestProfile>(Newtonsoft.Json.JsonConvert.SerializeObject(profile))!;
            var validation = PlaytestProfileStore.Validate(profile);
            if (validation.Count > 0)
                throw new InvalidDataException(string.Join(Environment.NewLine, validation));
            if (!Directory.Exists(clientBuildRoot))
                throw new DirectoryNotFoundException(clientBuildRoot);
            var preflight = PlaytestSetupService.Check(project, profile, clientBuildRoot, automated: false);
            foreach (var check in preflight) Emit($"Preflight · {check.Title}: {check.Detail}");
            if (preflight.Any(c => c.Blocking)) throw new InvalidDataException(string.Join("\n", preflight.Where(c => c.Blocking).Select(c => c.Detail)));

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopCts.Token);
            _session = Session ?? SessionRegistry.Default.Begin(project.RootPath, SessionKind, true, profile);
            if (Session is not null) SessionRegistry.Default.ClaimGame(_session);
            _session.Profile = profile;
            _session.RestartOf = RestartOf;
            Active[_session.Id] = this;
            _session.GameRoot = clientBuildRoot;
            _session.Retention = "retained";
            SessionRegistry.Default.Save(_session);
            _runStarted = true;
            PlaytestSessionWorkspace? workspace = null;
            PlaytestSessionResult? result = null;
            string? failure = null;
            try
            {
                workspace = await Task.Run(() => PlaytestWorkspaceService.Prepare(project, profile, allocated =>
                {
                    _session.WorkspacePath = allocated.RootPath;
                    if (string.IsNullOrEmpty(_session.ArtifactDirectory)) _session.ArtifactDirectory = allocated.RootPath;
                    SessionRegistry.Default.Save(_session);
                }), linked.Token);
                _workspace = workspace;
                workspace.SessionId = _session.Id;
                result = new PlaytestSessionResult { Workspace = workspace };
                _session.WorkspacePath = workspace.RootPath;
                if (string.IsNullOrEmpty(_session.ArtifactDirectory)) _session.ArtifactDirectory = workspace.RootPath;
                SessionRegistry.Default.Save(_session);
                Emit($"Preparing isolated playtest workspace: {workspace.RootPath}");
                Emit($"Session {_session.Id}: retained. World and logs remain available in Test Explorer > Run history.");
                await DeployProfileAsync(SourceProject ?? project, profile, workspace, linked.Token);
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
                SessionLifecycleService.Install(profile, workspace, _session);

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
                        if (!_saving) return result;
                    }
                    await ObserveLifecycleUntilExitAsync(clients.ToArray(), linked.Token);
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
                        if (!_saving) return result;
                    }
                    await ObserveLifecycleUntilExitAsync(new[] { client }, linked.Token);
                    if (SafeExitCode(client) != 0)
                        throw new InvalidOperationException($"Project Zomboid exited unexpectedly (exit code {SafeExitCode(client)}). Review the session output and isolated console.txt.");
                }

                await Task.Run(() => CollectSessionDiagnostics(project, workspace, result), linked.Token);
                _session.Shutdown = "clean shutdown";
                if (_saving)
                {
                    var clientReceipts = _session.Processes.Where(p => p.Endpoint != "server").Select(p => SessionLifecycleService.Receipt(p.CachePath, _session.Id)).ToArray();
                    if (clientReceipts.All(r => r == "post-save:" + _session.PersistenceToken) &&
                        (profile.Mode != PlaytestMode.DedicatedServer || _serverSaved.Task.IsCompletedSuccessfully))
                    {
                        _session.SaveEvidence = "completed: native post-save receipts, no new reported Lua errors, clean process exits";
                        if (profile.Mode == PlaytestMode.DedicatedServer) PlaytestFixtureService.Capture(profile, workspace);
                        _session.Retention = "reusable fixture";
                    }
                    else _session.SaveEvidence = "unverified: clean exit, but save completion receipts were missing or reported errors";
                }
                return result;
            }
            catch (OperationCanceledException) when (_stopCts.IsCancellationRequested)
            {
                failure = "Stopped by user; saving was not implied.";
                if (result is null || workspace is null) throw;
                result.WasStopped = true;
                CollectSessionDiagnostics(project, workspace, result);
                return result;
            }
            catch (Exception ex) { failure = ex.Message; throw; }
            finally
            {
                try
                {
                    await StopAllProcessesAsync();
                    // Manual and interrupted worlds are never implicit cleanup candidates,
                    // including old profiles persisted with KeepSessionData=false.
                    var disposable = SessionKind == "automated" && failure is null && !linked.IsCancellationRequested &&
                        result is not null && !result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error) && !profile.KeepSessionData;
                    if (workspace is not null && disposable)
                    {
                        PlaytestWorkspaceService.Cleanup(workspace, false);
                        _session.Retention = "disposable (removed after completion)";
                    }
                    SessionRegistry.Default.Finish(_session, failure is not null || linked.IsCancellationRequested ? "interrupted" :
                        result?.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error) == true ? "failed" : "completed", failure);
                }
                finally
                {
                    Active.TryRemove(_session.Id, out _);
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

        private async Task ObserveLifecycleUntilExitAsync(Process[] processes, CancellationToken ct)
        {
            var exited = Task.WhenAll(processes.Select(p => p.WaitForExitAsync(ct)));
            while (!exited.IsCompleted)
            {
                ObserveLifecycle();
                await Task.WhenAny(exited, Task.Delay(1000, ct)); ct.ThrowIfCancellationRequested();
            }
            await exited;
            ObserveLifecycle();
        }

        private void ObserveLifecycle()
        {
            foreach (var process in _session!.Processes)
            {
                if (SessionLifecycleService.Receipt(process.CachePath, _session.Id) is { } receipt) _session.SaveReceipts[process.Endpoint] = receipt;
                if (!File.Exists(process.LogPath) && File.Exists(Path.Combine(process.CachePath, "console.txt"))) process.LogPath = Path.Combine(process.CachePath, "console.txt");
                var saves = Path.Combine(process.CachePath, "Saves");
                if (process.SavePaths.Count == 0 && Directory.Exists(saves))
                    process.SavePaths = Directory.EnumerateFiles(saves, "map_ver.bin", new EnumerationOptions { RecurseSubdirectories = true,
                        AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true }).Select(p => Path.GetDirectoryName(p)!).ToList();
            }
            if (_session.RestartOf is not null)
                _session.RestartVerification = _session.Processes.All(p => _session.SaveReceipts.GetValueOrDefault(p.Endpoint) == "restart-marker-verified") ?
                    "passed: persisted marker observed after process restart on every endpoint; gameplay state requires project assertions" :
                    _session.SaveReceipts.Values.Any(r => r == "restart-marker-missing") ? "failed: persisted marker missing after restart" : "waiting for every endpoint to load";
            SessionRegistry.Default.Save(_session);
        }

        private async Task AwaitAutomationAsync(PlaytestSessionWorkspace workspace, Process[] processes, CancellationToken ct)
        {
            using var monitoring = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var execution = RunAutomation!(workspace, monitoring.Token);
            var exited = Task.WhenAny(processes.Select(p => p.WaitForExitAsync(monitoring.Token)));
            try
            {
                while (!execution.IsCompleted && !exited.IsCompleted)
                {
                    ObserveLifecycle();
                    await Task.WhenAny(execution, exited, Task.Delay(1000, ct));
                    ct.ThrowIfCancellationRequested();
                }
                ObserveLifecycle();
                if (await Task.WhenAny(execution, exited) != execution)
                {
                    monitoring.Cancel();
                    try { await execution; } catch (OperationCanceledException) { }
                    if (_saving) return;
                    ct.ThrowIfCancellationRequested();
                    throw new InvalidOperationException("A game process exited before the test run completed.");
                }
                await execution;
            }
            finally
            {
                monitoring.Cancel();
                // Let the monitor checkpoint failures and capture owned windows before shutdown.
                try { await execution; } catch { /* The primary failure is propagated above. */ }
            }
        }

        private async Task DeployProfileAsync(ModProject project, PlaytestProfile profile,
            PlaytestSessionWorkspace workspace, CancellationToken cancellationToken)
        {
            var snapshot = await Task.Run(() => DeploymentSnapshotService.Capture(project, profile, workspace.RootPath, cancellationToken), cancellationToken);
            workspace.SourceSnapshotId = snapshot.Id;
            workspace.PayloadManifestPath = snapshot.ManifestPath;
            if (_session is not null)
            {
                _session.SourceSnapshotId = snapshot.Id; _session.PayloadManifestPath = snapshot.ManifestPath;
                SessionRegistry.Default.Save(_session);
            }
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
            if (_session is not null && _workspace is not null)
                SessionRegistry.Default.AddProcess(_session, process, detectServerReady ? "server" : label.ToLowerInvariant().Replace(' ', '-'),
                    detectServerReady ? _workspace.ServerCachePath : _workspace.ClientCachePaths[int.Parse(label.Split(' ')[1]) - 1]);
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
            if (_saving && detectServerReady && line.TrimEnd().EndsWith("Saving finish", StringComparison.Ordinal)) _serverSaved.TrySetResult();
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
            {
                if (_session is not null) _session.Shutdown = "forced termination";
                await StopProcessAsync(process);
            }
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
        private readonly object _logGate = new();
        private void Emit(string message)
        {
            if (_workspace is not null)
                lock (_logGate) File.AppendAllText(Path.Combine(_workspace.RootPath, "session.log"), $"{DateTime.UtcNow:O} {message}{Environment.NewLine}");
            Output?.Invoke(this, message);
        }

        public void Dispose()
        {
            _stopCts.Cancel();
            foreach (var process in ProcessSnapshot())
                process.Dispose();
            _stopCts.Dispose();
        }
    }
}
