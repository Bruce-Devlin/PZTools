using System.Diagnostics;
using System.IO;
using PZTools.Core.Functions.Projects;
using PZTools.Core.Models;

namespace PZTools.Testing.Tests;

public sealed class LiveGameFactAttribute : FactAttribute
{
    public LiveGameFactAttribute(params string[] variables)
    {
        var missing = variables.Where(v => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(v))).ToArray();
        if (missing.Length > 0) Skip = "Opt-in native acceptance requires " + string.Join(", ", missing);
    }
}

public sealed class LiveServerAcceptance
{
    [LiveGameFact("PZTOOLS_LIVE_GAME_ROOT")]
    public async Task DeployedModLoadsOnInstalledDedicatedServerAndServerSavesCleanly()
    {
        var game = Environment.GetEnvironmentVariable("PZTOOLS_LIVE_GAME_ROOT");
        if (string.IsNullOrWhiteSpace(game)) return;
        var root = Path.Combine(Path.GetTempPath(), "PZToolsLiveAcceptance", Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "AcceptanceMod");
        Directory.CreateDirectory(Path.Combine(source, "common"));
        var lua = Path.Combine(source, "42", "media", "lua", "server"); Directory.CreateDirectory(lua);
        File.WriteAllText(Path.Combine(source, "42", "mod.info"), "name=PZTools Deployment Acceptance\nid=PZToolsDeploymentAcceptance\n");
        File.WriteAllText(Path.Combine(lua, "Acceptance.lua"), "Events.OnServerStarted.Add(function() print('PZTOOLS_DEPLOYMENT_ACCEPTED') end)\n");
        var project = new ModProject { RootPath = source, Name = "AcceptanceMod", ModInfo = new() { Id = "PZToolsDeploymentAcceptance" } };
        var profile = new PlaytestProfile { Mode = PlaytestMode.DedicatedServer, AutoConnectClients = false, LocalServerOnly = true,
            ServerName = "Acceptance_" + Guid.NewGuid().ToString("N")[..8], ServerPort = PlaytestSetupService.FindAvailablePort() };
        var workspace = PlaytestWorkspaceService.Prepare(project, profile);
        var session = SessionRegistry.Default.Begin(source, "live server acceptance", true, profile, artifacts: root);
        session.WorkspacePath = workspace.RootPath; SessionRegistry.Default.Save(session);
        Process? process = null;
        try
        {
            await ProjectDeployer.DeployProject(project, Path.Combine(workspace.ServerCachePath, "mods"));
            PlaytestServerConfig.Write(profile, workspace, project.ModInfo.Id, project);
            var launcher = PlaytestServerLauncher.Resolve("", game);
            var start = PlaytestServerLauncher.Create(launcher, $"-cachedir=\"{workspace.ServerCachePath}\" -servername {profile.ServerName} -port {profile.ServerPort} -udpport {profile.ServerPort + 1} -adminusername PZToolsAdmin -adminpassword AcceptanceOnly -nosteam -modfolders mods -ip 127.0.0.1", true);
            start.UseShellExecute = false; start.CreateNoWindow = true; start.RedirectStandardOutput = start.RedirectStandardError = start.RedirectStandardInput = true;
            process = new Process { StartInfo = start };
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var saved = false; var gate = new object();
            void Observe(string? line)
            {
                if (line is null) return;
                lock (gate) File.AppendAllText(Path.Combine(root, "server-output.log"), line + Environment.NewLine);
                if (line.Contains("PZTOOLS_DEPLOYMENT_ACCEPTED")) ready.TrySetResult();
                if (line.TrimEnd().EndsWith("Saving finish", StringComparison.Ordinal)) saved = true;
            }
            process.OutputDataReceived += (_, e) => Observe(e.Data); process.ErrorDataReceived += (_, e) => Observe(e.Data);
            Assert.True(process.Start()); SessionRegistry.Default.AddProcess(session, process, "server", workspace.ServerCachePath);
            process.BeginOutputReadLine(); process.BeginErrorReadLine();
            await ready.Task.WaitAsync(TimeSpan.FromMinutes(3));
            Assert.Empty(DeploymentManifestService.Verify(source, Path.Combine(workspace.ServerCachePath, "mods", "AcceptanceMod"), project.ModInfo.Id));
            await process.StandardInput.WriteLineAsync("quit"); await process.StandardInput.FlushAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(90));
            Assert.Equal(0, process.ExitCode); Assert.True(saved, "The native server must report Saving finish.");
            session.Shutdown = "clean shutdown"; session.SaveEvidence = "completed: native Saving finish and exit code 0";
            SessionRegistry.Default.Finish(session, "completed");
            File.WriteAllText(Path.Combine(root, "accepted.txt"), "Deployed mod executed OnServerStarted; manifest matched; native save completed; exit 0.");
        }
        catch (Exception ex) { SessionRegistry.Default.Finish(session, "failed", ex.Message + " Artifacts: " + root); throw; }
        finally
        {
            if (process is { HasExited: false }) { process.Kill(true); await process.WaitForExitAsync(); }
            process?.Dispose();
        }
    }
}
