using System.IO;
using Newtonsoft.Json;
using PZTools.Core.Functions.Projects;
using PZTools.Core.Models;

namespace PZTools.Core.Functions.Tester;

public static class GameFixtureSetup
{
    public static async Task<PlaytestProfile> CaptureAsync(ModProject project, PlaytestProfile selected, string gameRoot,
        Action<string> progress, CancellationToken ct, Action<string>? artifacts = null)
    {
        var profile = JsonConvert.DeserializeObject<PlaytestProfile>(JsonConvert.SerializeObject(selected))!;
        // A new ID and workspace keep existing fixtures and reusable profiles intact.
        profile.Id = Guid.NewGuid();
        if (profile.Mode == PlaytestMode.DedicatedServer)
        {
            profile.SaveMode = PlaytestSaveMode.Empty;
            profile.SourceSessionPath = "";
        }
        profile.KeepSessionData = true;
        var checks = PlaytestSetupService.Check(project, profile, gameRoot, automated: false).Where(c => c.Blocking).ToArray();
        if (checks.Length > 0) throw new InvalidOperationException(string.Join("\n", checks.Select(c => c.Detail)));
        var health = await ProjectHealthService.AnalyzeAsync(project, true, ct);
        if (!health.IsReadyToDeploy) throw new InvalidOperationException("Project errors block setup. Open Health Dashboard to fix them first.");
        var report = ModTestService.CreateReport(project, "setup");
        artifacts?.Invoke(report.ArtifactDirectory);
        PlaytestSessionWorkspace? workspace = null;
        var ready = new HashSet<string>();
        using var runner = new PlaytestSessionRunner
        {
            PrepareAutomation = w => { workspace = w; GameTestService.Prepare(project, profile, w, Array.Empty<string>(), report, setupOnly: true); }
        };
        var logGate = new object();
        runner.Output += (_, line) =>
        {
            lock (logGate) File.AppendAllText(Path.Combine(report.ArtifactDirectory, "setup.log"), line + Environment.NewLine);
            if (line.StartsWith("Starting ") || line.StartsWith("Clients closed.")) progress(line);
        };
        using var session = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var run = runner.RunAsync(project, profile, gameRoot, session.Token);
        try
        {
            while (!run.IsCompleted)
            {
                Observe();
                await Task.WhenAny(run, Task.Delay(500, ct));
                ct.ThrowIfCancellationRequested();
            }
            var result = await run;
            Observe();
            if (result.WasStopped || result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                throw new InvalidOperationException("Setup ended with errors. Fix the session errors before capturing a fixture. " + report.ArtifactDirectory);
            var expected = profile.Mode == PlaytestMode.DedicatedServer ? profile.ClientCount + 1 : 1;
            if (workspace is null || ready.Count != expected)
                throw new InvalidOperationException($"Setup closed before every endpoint entered the world ({ready.Count}/{expected}). Create a character in each client and try again.");
            if (profile.Mode == PlaytestMode.SinglePlayer)
            {
                var saves = PlaytestSetupService.DiscoverSaves(workspace.ClientCachePaths[0]);
                if (saves.Count != 1) throw new InvalidOperationException("Create exactly one saved world in the setup session, then quit normally.");
                var errors = PlaytestSetupService.ValidateSave(saves[0].Path);
                if (errors.Count > 0) throw new InvalidDataException(string.Join("\n", errors));
                profile.SourceSavePath = saves[0].Path;
                profile.SaveMode = PlaytestSaveMode.FreshClone;
            }
            else
            {
                PlaytestFixtureService.Capture(profile, workspace);
                profile.SourceSessionPath = workspace.RootPath;
                profile.SaveMode = PlaytestSaveMode.FreshSessionClone;
                var errors = PlaytestFixtureService.Validate(profile);
                if (errors.Count > 0) throw new InvalidDataException(string.Join("\n", errors));
            }
            profile.Id = selected.Id;
            profile.KeepSessionData = selected.KeepSessionData;
            progress("Fixture captured. Future runs use a fresh copy; the original stays unchanged.");
            return profile;
        }
        finally
        {
            session.Cancel();
            // Always drain the runner before disposing it, including observation errors.
            if (!run.IsCompleted) { try { await run; } catch { } }
            if (workspace is not null) File.WriteAllText(Path.Combine(report.ArtifactDirectory, "workspace.txt"), workspace.RootPath);
        }

        void Observe()
        {
            if (workspace is null) return;
            var endpoints = workspace.ClientCachePaths.Select((p, i) => (Cache: p, Name: "client-" + (i + 1))).ToList();
            if (profile.Mode == PlaytestMode.DedicatedServer) endpoints.Add((workspace.ServerCachePath, "server"));
            foreach (var endpoint in endpoints)
            {
                var file = Path.Combine(endpoint.Cache, "Lua", "pztests-events.txt");
                if (!File.Exists(file)) continue;
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                var lines = reader.ReadToEnd().Split('\n');
                foreach (var line in lines.Take(lines.Length - 1))
                {
                    var fields = line.TrimEnd('\r').Split('\t');
                    if (fields.Length != 8 || fields[0] != report.RunId || fields[1] != endpoint.Name)
                        throw new InvalidDataException("Invalid setup companion event.");
                    if (fields[3] == "error") throw new InvalidOperationException(endpoint.Name + ": " + fields[6]);
                    if (fields[3] == "ready" && fields[6] == "World started" && ready.Add(endpoint.Name))
                    {
                        progress(endpoint.Name + " is in the world. " + ready.Count + "/" + endpoints.Count + " endpoints ready.");
                        if (ready.Count == endpoints.Count) progress("Everyone is ready. Quit to desktop normally in each client to save your fixture. The server will save and stop automatically.");
                    }
                }
            }
        }
    }
}
