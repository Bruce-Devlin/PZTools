using System.IO;
using MoonSharp.Interpreter;
using PZTools.Core.Functions.Projects;
using PZTools.Core.Functions.Tester;
using PZTools.Core.Models;

namespace PZTools.Testing.Tests;

public sealed class PlaytestSetupTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "PZToolsSetupTests", Guid.NewGuid().ToString("N"));
    private ModProject Project => new() { RootPath = root, Name = "Test", ModInfo = new() { Id = "Test" } };
    private void Write(string path, string text = "fixture")
    {
        var full = Path.Combine(root, path); Directory.CreateDirectory(Path.GetDirectoryName(full)!); File.WriteAllText(full, text);
    }

    [Fact]
    public void DefaultsCreateIndependentProfilesAndRetainDependencyChoices()
    {
        var original = new PlaytestProfile { ClientCount = 6, LaunchArguments = "-debug", SourceSavePath = "existing" };
        original.Dependencies.Add(new() { ModId = "Optional", SourcePath = "path" });
        var profile = PlaytestSetupService.Create(Project, PlaytestMode.DedicatedServer, original);
        Assert.NotEqual(original.Id, profile.Id);
        Assert.Equal(2, profile.ClientCount); Assert.True(profile.LocalServerOnly);
        Assert.True(profile.NoSteam && profile.AutoConnectClients);
        Assert.DoesNotContain("-debug", profile.LaunchArguments);
        profile.Dependencies[0].ModId = "changed";
        Assert.Equal("Optional", original.Dependencies[0].ModId);
        Assert.Equal(6, original.ClientCount); Assert.Equal("existing", original.SourceSavePath);
    }

    [Fact]
    public void AddingGuidedProfilePreservesOldInvalidProfilesAndUnknownMetadata()
    {
        var old = Newtonsoft.Json.Linq.JObject.FromObject(new PlaytestProfile { Name = "Old fixture", SaveMode = PlaytestSaveMode.FreshClone, SourceSavePath = "missing" });
        old["FutureSetting"] = "keep me";
        Write(".pztools/playtest-profiles.json", new Newtonsoft.Json.Linq.JArray(old).ToString());
        var profile = PlaytestSetupService.Create(Project, PlaytestMode.DedicatedServer);
        PlaytestProfileStore.Add(Project, profile);
        var saved = Newtonsoft.Json.Linq.JArray.Parse(File.ReadAllText(PlaytestProfileStore.GetPath(Project)));
        Assert.Equal(2, saved.Count);
        Assert.True(Newtonsoft.Json.Linq.JToken.DeepEquals(Newtonsoft.Json.Linq.JObject.Parse(old.ToString()), saved[0]));
        Assert.Equal(profile.Id.ToString(), (string?)saved[1]["Id"]);
    }

    [Fact]
    public async Task HostOnlyReleasesTestsAfterAllEndpointsAndNamesMissingClientsOnCancel()
    {
        var profile = new PlaytestProfile { Mode = PlaytestMode.DedicatedServer, ClientCount = 2 };
        var workspace = PlaytestWorkspaceService.Prepare(Project, profile);
        var report = ModTestService.CreateReport(Project, "game");
        var endpoints = workspace.ClientCachePaths.Select((p, i) => (Cache: p, Name: "client-" + (i + 1))).Append((Cache: workspace.ServerCachePath, Name: "server")).ToArray();
        foreach (var endpoint in endpoints) Directory.CreateDirectory(Path.Combine(endpoint.Cache, "Lua"));
        void Emit(int index, int seq, string kind, string message = "") => File.AppendAllText(Path.Combine(endpoints[index].Cache, "Lua/pztests-events.txt"),
            $"{report.RunId}\t{endpoints[index].Name}\t{seq}\t{kind}\t\t\t{message}\t{(kind == "status" ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() : 0)}\n");
        Emit(0, 1, "status", "awaiting_barrier|2|false|true|true"); Emit(2, 1, "status", "awaiting_barrier|2|unknown|true|true");
        Emit(2, 2, "ready", "World started");
        using var cancel = new CancellationTokenSource();
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = GameTestService.MonitorAsync(workspace, profile, report,
            line => { if (line.Contains("[server] ready")) observed.TrySetResult(); }, () => null, cancel.Token);
        await observed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.All(endpoints, e => Assert.False(File.Exists(Path.Combine(e.Cache, "Lua/pztests-go.txt"))));
        cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Contains(report.Errors, e => e.StartsWith("Still waiting for: client-2."));

        Emit(1, 1, "status", "awaiting_barrier|2|false|true|true");
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        run = GameTestService.MonitorAsync(workspace, profile, report,
            line => { if (line.StartsWith("Every endpoint")) released.TrySetResult(); }, () => null, timeout.Token);
        await released.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.All(endpoints, e => Assert.Equal(report.RunId, File.ReadAllText(Path.Combine(e.Cache, "Lua/pztests-go.txt")).Trim()));
        for (var i = 0; i < endpoints.Length; i++) Emit(i, i == 2 ? 3 : 2, "complete");
        await run;
    }

    [Fact]
    public void SavesAreSpecificWorldsAndDoNotClaimCharacterCompatibility()
    {
        Write("user/Saves/Sandbox/Living/players.db"); Write("user/Saves/Sandbox/Living/map_ver.bin");
        Write("user/Saves/Multiplayer/Local/players.db"); Write("user/Saves/Multiplayer/Local/map_ver.bin");
        Write("user/Saves/Sandbox/Incomplete/players.db");
        var saves = PlaytestSetupService.DiscoverSaves(Path.Combine(root, "user"));
        Assert.Single(saves); Assert.EndsWith("Living", saves[0].Path);
        Assert.Empty(PlaytestSetupService.ValidateSave(saves[0].Path));
        Assert.NotEmpty(PlaytestSetupService.ValidateSave(Path.Combine(root, "user/Saves")));
        Write("user/Saves/Sandbox/Living/nested/players.db");
        Assert.NotEmpty(PlaytestSetupService.ValidateSave(saves[0].Path));
    }

    [Fact]
    public void SessionClonesRetainCharactersAndLoginButExcludeLogsAndOldMods()
    {
        var profile = new PlaytestProfile { Mode = PlaytestMode.DedicatedServer, ClientCount = 2 };
        var fixture = PlaytestWorkspaceService.Prepare(Project, profile);
        foreach (var cache in fixture.ClientCachePaths.Append(fixture.ServerCachePath))
        {
            Directory.CreateDirectory(Path.Combine(cache, "Saves/Multiplayer/world"));
            File.WriteAllText(Path.Combine(cache, "Saves/Multiplayer/world/players.db"), "character");
            Directory.CreateDirectory(Path.Combine(cache, "db")); File.WriteAllText(Path.Combine(cache, "db/accounts.db"), "accounts");
            Directory.CreateDirectory(Path.Combine(cache, "Lua")); File.WriteAllText(Path.Combine(cache, "Lua/pztools-local-login.txt"), "server\n16261\nPZTools1\npassword\n");
            File.WriteAllText(Path.Combine(cache, "console.txt"), "old log");
        }
        Directory.CreateDirectory(Path.Combine(fixture.ServerCachePath, "Server"));
        File.WriteAllText(Path.Combine(fixture.ServerCachePath, "Server/PZToolsTest_SandboxVars.lua"), "settings");
        PlaytestFixtureService.Capture(profile, fixture);
        profile.SourceSessionPath = fixture.RootPath; profile.SaveMode = PlaytestSaveMode.FreshSessionClone;
        Assert.Empty(PlaytestFixtureService.Validate(profile));
        var copy = PlaytestWorkspaceService.Prepare(Project, profile);
        Assert.NotEqual(fixture.RootPath, copy.RootPath);
        foreach (var cache in copy.ClientCachePaths.Append(copy.ServerCachePath))
        {
            Assert.Equal("character", File.ReadAllText(Path.Combine(cache, "Saves/Multiplayer/world/players.db")));
            Assert.True(File.Exists(Path.Combine(cache, "db/accounts.db")));
            Assert.True(File.Exists(Path.Combine(cache, "Lua/pztools-local-login.txt")));
            Assert.False(File.Exists(Path.Combine(cache, "console.txt")));
        }
        Assert.True(File.Exists(Path.Combine(copy.ServerCachePath, "Server/PZToolsTest_SandboxVars.lua")));
        File.WriteAllText(Path.Combine(copy.ClientCachePaths[0], "Saves/Multiplayer/world/players.db"), "modified");
        Assert.Equal("character", File.ReadAllText(Path.Combine(fixture.ClientCachePaths[0], "Saves/Multiplayer/world/players.db")));
        profile.Build = 41; Assert.NotEmpty(PlaytestFixtureService.Validate(profile));
        profile.Build = 42; profile.ClientCount = 3; Assert.NotEmpty(PlaytestFixtureService.Validate(profile));
        profile.ClientCount = 2; profile.ServerPort++; Assert.NotEmpty(PlaytestFixtureService.Validate(profile));
    }

    [Fact]
    public void IncompleteFixtureAndMissingConfiguredServerAreRejected()
    {
        var profile = new PlaytestProfile { Mode = PlaytestMode.DedicatedServer, SaveMode = PlaytestSaveMode.FreshSessionClone, SourceSessionPath = root };
        Assert.NotEmpty(PlaytestFixtureService.Validate(profile));
        Write("game/StartServer64.bat");
        Assert.Throws<FileNotFoundException>(() => PlaytestServerLauncher.Resolve(Path.Combine(root, "missing"), Path.Combine(root, "game")));
        Assert.EndsWith("StartServer64.bat", PlaytestServerLauncher.Resolve("", Path.Combine(root, "game")));
    }

    [Fact]
    public void LocalOnlyServerDisablesRouterPortMappingAndWorkshopDownloads()
    {
        var profile = new PlaytestProfile { Mode = PlaytestMode.DedicatedServer, LocalServerOnly = true, NoSteam = true };
        profile.Dependencies.Add(new() { ModId = "Example", WorkshopId = "123" });
        var workspace = PlaytestWorkspaceService.Prepare(Project, profile);
        var ini = File.ReadAllText(PlaytestServerConfig.Write(profile, workspace, "Test"));
        Assert.Contains("UPnP=false", ini); Assert.DoesNotContain("WorkshopItems=123", ini);
        profile.AdditionalServerOptions = "UPnP=true";
        Assert.Contains(PlaytestProfileStore.Validate(profile), e => e.Contains("UPnP port forwarding"));
    }

    [Fact]
    public void BundledServerUsesInstallationJvmAndPreservesServerArguments()
    {
        Write("game/ProjectZomboid64.json", """{"vmArgs":["--enable-native-access=ALL-UNNAMED","-Xmx2048m","-Dzomboid.steam=1"],"classpath":[".","projectzomboid.jar"]}""");
        Write("game/projectzomboid.jar"); Write("game/jre64/bin/java.exe"); Write("game/ProjectZomboidServer.bat");
        var launcher = PlaytestServerLauncher.Resolve("", Path.Combine(root, "game"));
        var command = PlaytestServerLauncher.Create(launcher, "-cachedir=isolated -port 17261 -ip 127.0.0.1", true);
        Assert.EndsWith("java.exe", command.FileName);
        Assert.Contains("--enable-native-access=ALL-UNNAMED", command.Arguments);
        Assert.Contains("-Dzomboid.steam=0", command.Arguments);
        Assert.DoesNotContain("-Dzomboid.steam=1", command.Arguments);
        Assert.Contains("zombie.network.GameServer -cachedir=isolated -port 17261 -ip 127.0.0.1", command.Arguments);
    }

    [Fact]
    public void LocalLoginIsDistinctAndSurvivesFixtureCopyAndPayloadsMatch()
    {
        var profile = new PlaytestProfile { Mode = PlaytestMode.DedicatedServer, ClientCount = 2 };
        var workspace = PlaytestWorkspaceService.Prepare(Project, profile);
        PlaytestLocalConnection.Prepare(profile, workspace);
        var first = File.ReadAllLines(Path.Combine(workspace.ClientCachePaths[0], "Lua/pztools-local-login.txt"));
        var second = File.ReadAllLines(Path.Combine(workspace.ClientCachePaths[1], "Lua/pztools-local-login.txt"));
        Assert.Equal("PZTools1", first[2]); Assert.Equal("PZTools2", second[2]); Assert.NotEqual(first[3], second[3]);
        PlaytestLocalConnection.Prepare(profile, workspace);
        Assert.Equal(first[3], File.ReadAllLines(Path.Combine(workspace.ClientCachePaths[0], "Lua/pztools-local-login.txt"))[3]);
        Assert.Empty(ClientServerParityService.Compare(Path.Combine(workspace.ServerCachePath, "mods"), workspace.ClientCachePaths.Select(p => Path.Combine(p, "mods"))));
        var script = new Script(); script.LoadString(ModTestService.Resource("LocalConnect.lua"));
    }

    [Theory]
    [InlineData("tests", true)]
    [InlineData("setup", false)]
    public void CompanionWaitsForMatchingRunBarrierAndSetupNeverExecutesTests(string purpose, bool runs)
    {
        var script = new Script(); script.Globals["purpose"] = purpose;
        script.DoString("""
            require = function() end
            output = ""; ticks = 0; now = 0; gateRun = "old-run"
            PZT = { tick = function() ticks = ticks + 1 end }
            PZTestManifest = {}; PZTestSupport = {}
            local config = { "run", "client-1", "", "", purpose }
            function getFileReader(path)
                if path == "pztests-go.txt" then return { readLine = function() return gateRun end, close = function() end } end
                local i = 0; return { readLine = function() i = i + 1; return config[i] end, close = function() end }
            end
            function getFileWriter() return { write = function(_, s) output = output .. s end, close = function() end } end
            function getTimestampMs() now = now + 1000; return now end
            function isServer() return false end
            function getSpecificPlayer() return { isDead = function() return false end } end
            Events = setmetatable({}, { __index = function(t, k)
                local e = { Add = function(fn) t[k].callback = fn end }; t[k] = e; return e
            end })
            """);
        script.DoString(ModTestService.Resource("Driver.lua"));
        script.DoString("Events.OnGameStart.callback(); if Events.OnTick.callback then Events.OnTick.callback() end");
        Assert.Contains("World started", script.Globals.Get("output").String);
        Assert.Equal(0, script.Globals.Get("ticks").Number);
        script.DoString("gateRun = 'run'; if Events.OnTick.callback then Events.OnTick.callback() end");
        Assert.Equal(runs ? 1 : 0, script.Globals.Get("ticks").Number);
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
