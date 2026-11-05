using System.IO;
using PZTools.Core.Functions.Projects;
using PZTools.Core.Functions.Tester;
using PZTools.Core.Models;

namespace PZTools.Testing.Tests;

public sealed class LivePersistenceAcceptance
{
    [LiveGameFact("PZTOOLS_LIVE_GAME_ROOT", "PZTOOLS_LIVE_SOURCE_SAVE")]
    public async Task MarkerSurvivesNativeSaveCleanShutdownAndNewClientProcess()
    {
        var game = Environment.GetEnvironmentVariable("PZTOOLS_LIVE_GAME_ROOT");
        var sourceSave = Environment.GetEnvironmentVariable("PZTOOLS_LIVE_SOURCE_SAVE");
        if (string.IsNullOrWhiteSpace(game) || string.IsNullOrWhiteSpace(sourceSave)) return;
        var root = Path.Combine(Path.GetTempPath(), "PZToolsPersistenceAcceptance", Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "42"); Directory.CreateDirectory(target); Directory.CreateDirectory(Path.Combine(root, "common"));
        File.WriteAllText(Path.Combine(target, "mod.info"), "name=Persistence Acceptance\nid=PZToolsPersistenceAcceptance\ndescription=Isolated acceptance\n");
        Directory.CreateDirectory(Path.Combine(target, "media", "lua", "shared"));
        File.WriteAllText(Path.Combine(target, "media", "lua", "shared", "Acceptance.lua"), "-- Isolated native lifecycle acceptance\n");
        var project = new ModProject { RootPath = root, Name = "Persistence Acceptance", ModInfo = new() { Id = "PZToolsPersistenceAcceptance", Name = "Persistence Acceptance" },
            Targets = new() { new() { Path = target, Build = 42, IsPrimary = true } } };
        var profile = new PlaytestProfile { Build = 42, SaveMode = PlaytestSaveMode.FreshClone, SourceSavePath = sourceSave,
            LaunchArguments = "-nosteam", KeepSessionData = false, Name = "Save restart acceptance" };
        using var ct = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = GameTestService.RunAsync(project, profile, game, timeoutSeconds: 300, cancellationToken: ct.Token, keepAlive: true,
            endpointProgress: endpoints => { if (endpoints.Count > 0 && endpoints.All(e => e.IsReady(DateTime.UtcNow))) ready.TrySetResult(); });
        try
        {
            var first = await Task.WhenAny(ready.Task, run);
            if (first == run) Assert.Fail("Initial native session failed: " + string.Join("; ", (await run).Errors));
            await ready.Task.WaitAsync(TimeSpan.FromMinutes(5));
            var session = SessionRegistry.Default.List(root).First(s => s.OwnsGame);
            await PlaytestSessionRunner.SaveAndShutdownAsync(root, session.Id);
            var initial = await run;
            Assert.Empty(initial.Errors);
            session = SessionRegistry.Default.Get(session.Id);
            Assert.StartsWith("completed", session.SaveEvidence); Assert.Equal("clean shutdown", session.Shutdown);
            Assert.True(Directory.Exists(session.WorkspacePath));
            var restart = SessionLifecycleService.RestartProfile(session);
            Directory.CreateDirectory(Path.Combine(root, ".pztests"));
            File.WriteAllText(Path.Combine(root, ".pztests", "restart.game.lua"),
                "test('saved player marker survives restart', function(t) local p=t:setup(function() return t:livingCharacter(0,10) end); t:equal(p:getModData().PZToolsPersistenceToken, '" + session.PersistenceToken + "'); t:equal(t:observeApi('zombie.Lua.LuaManager.GlobalObject.getSpecificPlayer',function() return getSpecificPlayer(0) end),p) end)\n" +
                "test('unloaded fixture fails before exercise', function(t) t:setup(function() t:loadedSquare(-999999,-999999,0,0) end); t:step('MUST NOT EXERCISE', function() end) end)");
            var report = await GameTestService.RunAsync(project, restart, game, timeoutSeconds: 300, cancellationToken: ct.Token, restartOf: session.Id);
            Assert.Empty(report.Errors);
            Assert.Equal("passed", report.Tests[0].Status);
            var expectedFailure = report.Tests[1];
            Assert.Equal("setup", expectedFailure.FailureOrigin); Assert.Equal("Setup", expectedFailure.LastPhase);
            Assert.DoesNotContain("MUST NOT EXERCISE", expectedFailure.Steps);
            Assert.NotNull(expectedFailure.Diagnostic); Assert.NotNull(expectedFailure.Diagnostic.ProcessId);
            Assert.NotEmpty(expectedFailure.Diagnostic.LogExcerpt);
            Assert.NotEmpty(report.Tests[0].ObservedApis);
            var resumed = SessionRegistry.Default.Get(report.SessionId!);
            Assert.StartsWith("passed", resumed.RestartVerification);
            Assert.NotEqual(session.Processes[0].Id, resumed.Processes[0].Id);
            File.WriteAllText(Path.Combine(root, "accepted.txt"), $"Save session {session.Id}; restart report {report.RunId}; persisted marker verified by gameplay assertion in a new client process. Deliberate unloaded-square case failed during Setup with a diagnostic bundle.");
        }
        finally { ct.Cancel(); await run; }
    }
}
