using System.IO;
using System.Xml.Linq;
using MoonSharp.Interpreter;
using PZTools.Core.Functions.Projects;
using PZTools.Core.Functions.Tester;
using PZTools.Core.Models;

namespace PZTools.Testing.Tests;

public sealed class ModTestingTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "PZToolsTesting", Guid.NewGuid().ToString("N"));
    private ModProject Project => new() { RootPath = root, Name = "TestFixture", ModInfo = new() { Id = "TestFixture" } };
    public ModTestingTests() => Directory.CreateDirectory(Path.Combine(root, ".pztests"));
    private void Write(string file, string content)
    {
        var path = Path.Combine(root, file); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, content);
    }

    [Fact]
    public async Task RunsAssertionsHooksAndProducesJUnit()
    {
        Write(".pztests/main.unit.lua", """
            local counter = 0
            beforeEach(function(t) counter = counter + 1 end)
            afterEach(function(t) counter = counter + 10 end)
            test("first", function(t)
                t:equal(counter, 1)
                t:step("observable step", function() t:truthy(true) end)
                t:cleanup(function() counter = counter + 100 end)
            end)
            test("second", function(t) t:equal(counter, 112) end)
            """);
        var report = await ModTestService.RunUnitAsync(Project);
        Assert.True(report.Passed, string.Join("\n", report.Errors.Concat(report.Tests.Select(t => t.Message))));
        Assert.Equal(2, report.Tests.Count);
        Assert.Single(report.Tests[0].Steps);
        Assert.Equal("2", XDocument.Load(Path.Combine(report.ArtifactDirectory, "junit.xml")).Root!.Attribute("tests")!.Value);
    }

    [Fact]
    public async Task FailureDoesNotPreventLaterTestsAndCleanupRuns()
    {
        Write(".pztests/fail.unit.lua", """
            local cleaned = false
            test("intentional failure", function(t)
                t:cleanup(function() cleaned = true end)
                t:equal("actual", "expected")
            end)
            test("after failure", function(t) t:truthy(cleaned) end)
            """);
        var report = await ModTestService.RunUnitAsync(Project);
        Assert.False(report.Passed);
        Assert.Empty(report.Errors);
        Assert.Equal("failed", report.Tests[0].Status);
        Assert.Equal("passed", report.Tests[1].Status);
    }

    [Fact]
    public async Task ProtectedFailuresAndCleanupFinalizeOnceAndKeepOriginalMessage()
    {
        Write(".pztests/contained.unit.lua", """
            local cleaned = 0
            test("assertion", function(t)
                t:cleanup(function() cleaned = cleaned + 1; error('cleanup broke') end)
                t:equal(1, 2, 'original failure')
                error('must not continue')
            end)
            test("after assertion", function(t) t:equal(cleaned, 1) end)
            test("native", function(t) t:phase('engine call', function() error('native broke') end) end)
            test("after native", function(t) t:truthy(true) end)
            test("wait", function(t) t:waitUntil(function() return false end, 0) end)
            test("after wait", function(t) t:truthy(true) end)
            """);
        var report = await ModTestService.RunUnitAsync(Project);
        Assert.Empty(report.Errors);
        Assert.Equal(new[] { "failed", "passed", "failed", "passed", "failed", "passed" }, report.Tests.Select(t => t.Status));
        Assert.Contains("original failure", report.Tests[0].Message);
        Assert.Contains("cleanup broke", report.Tests[0].Message);
        Assert.DoesNotContain("must not continue", report.Tests[0].Message);
        Assert.Equal("assertion", report.Tests[0].FailureOrigin);
        Assert.Equal("native_phase", report.Tests[2].FailureOrigin);
    }

    [Fact]
    public async Task LoggedNativeErrorsCannotPassProtectedOrUnprotectedCases()
    {
        Write(".pztests/logged.unit.lua", """
            local count = 0
            local escaped = false
            PZT.errorCount = function() return count end
            PZT.lastError = function() return 'native exception logged by host' end
            test('protected', function(t)
                t:phase('native', function() count = count + 1 end)
                escaped = true
            end)
            test('after protected', function(t) t:equal(escaped, false) end)
            test('expected throw', function(t) t:throws(function() count = count + 1 end, 'native exception') end)
            test('unprotected', function(t) count = count + 1 end)
            test('must not run', function(t) error('unsafe') end)
            """);
        var report = await ModTestService.RunUnitAsync(Project);
        Assert.Equal(new[] { "failed", "passed", "passed", "failed" }, report.Tests.Select(t => t.Status));
        Assert.Equal("native_phase", report.Tests[0].FailureOrigin);
        Assert.Equal("harness", report.Tests[3].FailureOrigin);
        Assert.Single(report.Errors);
        Assert.Contains("native exception logged", report.Tests[0].Message);
    }

    [Fact]
    public async Task DamagedCoroutineStopsOnceWithoutRunningNextCase()
    {
        Write(".pztests/damaged.unit.lua", """
            local cleanups = 0
            test('damaged', function(t)
                t:cleanup(function() cleanups = cleanups + 1; assert(cleanups == 1) end)
                PZT.current.thread = coroutine.running()
                PZT.current.resuming = true
                -- Emulate resume escaping into the event host, leaving the guard set.
                PZT.watchdog()
            end)
            test('must not execute', function(t) error('unexpected') end)
            """);
        var report = await ModTestService.RunUnitAsync(Project);
        Assert.Single(report.Tests);
        Assert.Equal("failed", report.Tests[0].Status);
        Assert.Equal("harness", report.Tests[0].FailureOrigin);
        Assert.Single(report.Errors);
        Assert.Contains("cannot safely continue", report.Errors[0]);
    }

    [Fact]
    public async Task ModulesLoadFromProjectAndFilesHaveSeparateGlobals()
    {
        Write("common/media/lua/shared/Rules.lua", "return { value = 42 }");
        Write(".pztests/a.unit.lua", "leak = 1; test('module', function(t) t:equal(require('Rules').value, 42) end)");
        Write(".pztests/b.unit.lua", "test('isolation', function(t) t:equal(leak, nil) end)");
        var report = await ModTestService.RunUnitAsync(Project);
        Assert.True(report.Passed, string.Join("\n", report.Errors.Concat(report.Tests.Select(t => t.Message))));
    }

    [Fact]
    public async Task EmptySyntaxErrorAndCancellationCannotPass()
    {
        Assert.False((await ModTestService.RunUnitAsync(Project)).Passed);
        Write(".pztests/bad.unit.lua", "test('broken', function(");
        var syntax = await ModTestService.RunUnitAsync(Project);
        Assert.NotEmpty(syntax.Errors);
        var cancelled = await ModTestService.RunUnitAsync(Project, cancellationToken: new CancellationToken(true));
        Assert.Contains("Run cancelled.", cancelled.Errors);
    }

    [Fact]
    public async Task EventualAssertionYieldsAndTimesOut()
    {
        Write(".pztests/wait.unit.lua", """
            test("wait succeeds", function(t)
                local n = 0
                t:waitUntil(function() n = n + 1; return n == 3 end, 1)
            end)
            test("wait fails", function(t) t:waitUntil(function() return false end, 0.01) end)
            """);
        var report = await ModTestService.RunUnitAsync(Project);
        Assert.Equal("passed", report.Tests[0].Status);
        Assert.Equal("failed", report.Tests[1].Status);
        Assert.Contains("timed out", report.Tests[1].Message);
    }

    [Fact]
    public void DriverClearsStartupButDoesNotRepairDeadPlayersOrOverrideTestsAfterRelease()
    {
        var script = new Script();
        script.DoString("""
            require = function() end
            output = ''; clock = 100000; ticks = 0; present = false; dead = false; speed = 0
            PZT = { tick = function() ticks = ticks + 1 end }
            PZTestSupport = { function() supportRegistered = true end }; PZTestManifest = {}
            local config = { 'run', 'client-1', '', '', 'tests' }
            function getFileReader(path)
                if path == 'pztests-go.txt' then
                    if gateRun then return { readLine = function() return gateRun end, close = function() end } end
                    return nil
                end
                local i = 0; return { readLine = function() i=i+1; return config[i] end, close = function() end }
            end
            function getFileWriter() return { write = function(_, text) output = output .. text end, close = function() end } end
            function getTimestampMs() clock = clock + 1000; return clock end
            function isServer() return false end
            function getSpecificPlayer() if present then return { isDead = function() return dead end } end end
            UIManager = { getSpeedControls = function() return { getCurrentGameSpeed = function() return speed end } end }
            Events = setmetatable({}, { __index = function(t, name)
                local handlers = {}; local event = { Add = function(fn) table.insert(handlers, fn) end,
                    fire = function() for _, fn in ipairs(handlers) do fn() end end }; t[name] = event; return event
            end })
            """);
        CompanionStartupTests.Install(script);
        script.DoString(ModTestService.Resource("Driver.lua"));
        script.DoString("Events.OnTickEvenPaused.fire(); Events.OnGameStart.fire()");
        Assert.Contains("awaiting_player|0|true|false|false", script.Globals.Get("output").String);
        script.DoString("present = true; Events.OnTick.fire(); Events.OnTickEvenPaused.fire()");
        Assert.True(script.Globals.Get("supportRegistered").Boolean);
        Assert.Equal(0, script.Globals.Get("ticks").Number);
        script.DoString("dead = true; Events.OnTickEvenPaused.fire()");
        Assert.Contains("|false|false|true", script.Globals.Get("output").String);
        Assert.Equal(1, script.Globals.Get("speed").Number);
        Assert.True(script.Globals.Get("dead").Boolean);
        script.DoString("dead = false; speed = 1; gateRun = 'run'; Events.OnTick.fire(); dead = true; Events.OnTickEvenPaused.fire()");
        Assert.Contains("running|", script.Globals.Get("output").String);
        script.DoString("speed = 0; guideVisible = true; characterVisible = true; Events.OnTickEvenPaused.fire()");
        Assert.Equal(0, script.Globals.Get("speed").Number);
        Assert.True(script.Globals.Get("guideVisible").Boolean);
        Assert.True(script.Globals.Get("characterVisible").Boolean);
        Assert.Contains("|false|false|true", script.Globals.Get("output").String);
    }

    [Fact]
    public void ScaffoldPreservesUserFilesAndDeploymentExcludesTests()
    {
        Write(".pztests/example.unit.lua", "user content");
        ModTestService.Scaffold(Project);
        Assert.Equal("user content", File.ReadAllText(Path.Combine(root, ".pztests/example.unit.lua")));
        Assert.False(ProjectDeployer.ShouldIncludePath(".pztests/example.unit.lua"));
        Assert.True(ProjectDeployer.ShouldIncludePath("42/media/lua/client/MyMod.lua"));
    }

    [Fact]
    public void CompanionPayloadIsValidAndMatchesAcrossEndpoints()
    {
        Write(".pztests/one.game.lua", "test('client', function(t) t:truthy(true) end)");
        Write(".pztests/two.server.lua", "test('server', function(t) t:truthy(true) end)");
        Write(".pztests/support/adapter.lua", "PZTest.register('query', function() return 42 end)");
        var profile = new PlaytestProfile { Mode = PlaytestMode.DedicatedServer, ClientCount = 2 };
        var workspace = PlaytestWorkspaceService.Prepare(Project, profile);
        foreach (var cache in workspace.ClientCachePaths)
        {
            Directory.CreateDirectory(Path.Combine(cache, "mods"));
            File.WriteAllText(Path.Combine(cache, "mods/default.txt"), "VERSION = 1,\nmods\n{\n mod = \\TestFixture,\n}\nmaps\n{\n}");
            PlaytestClientConfig.Configure(cache, profile);
            Assert.True(File.Exists(Path.Combine(cache, "mods/reset-mods-42_00.txt")));
        }
        PlaytestServerConfig.Write(profile, workspace, "TestFixture");
        var report = ModTestService.CreateReport(Project, "game");
        GameTestService.Prepare(Project, profile, workspace, ModTestService.Discover(Project, "game"), report);
        var script = new Script();
        foreach (var file in Directory.EnumerateFiles(workspace.ClientCachePaths[0], "*.lua", SearchOption.AllDirectories)) script.LoadString(File.ReadAllText(file));
        Assert.Empty(ClientServerParityService.Compare(Path.Combine(workspace.ServerCachePath, "mods"), workspace.ClientCachePaths.Select(c => Path.Combine(c, "mods"))));
        Assert.Contains("PZToolsTesting", File.ReadAllText(Path.Combine(workspace.ClientCachePaths[0], "mods/default.txt")));
        Assert.Contains("PZToolsTesting", File.ReadAllText(Path.Combine(workspace.ServerCachePath, "Server/PZToolsTest.ini")));
    }

    [Theory]
    [InlineData(41, false)]
    [InlineData(42, true)]
    [InlineData(42.20, true)]
    public void ClientSetupPreventsBuild42FirstLaunchFromClearingModSelection(double build, bool expectsMarker)
    {
        Write("cache/mods/default.txt", "VERSION = 1,\nmods { mod = TestFixture, }\nmaps {}\n");
        var selection = File.ReadAllText(Path.Combine(root, "cache/mods/default.txt"));
        PlaytestClientConfig.Configure(Path.Combine(root, "cache"), new PlaytestProfile { Build = build });
        Assert.Equal(expectsMarker, File.Exists(Path.Combine(root, "cache/mods/reset-mods-42_00.txt")));
        Assert.Equal(selection, File.ReadAllText(Path.Combine(root, "cache/mods/default.txt")));
    }

    [Theory]
    [InlineData(true, 240, true)]
    [InlineData(false, 240, false)]
    [InlineData(true, 239, false)]
    public void CompanionOnlyContinuesCompatibleLivingCharacterSaves(bool alive, int version, bool continues)
    {
        var script = new Script();
        script.Globals["alive"] = alive;
        script.Globals["version"] = version;
        script.DoString("""
            require = function() end
            PZT = {}
            output = ""
            continued = false
            local config = { "run", "client-1", "Sandbox", "fixture" }
            function getFileReader()
                local i = 0
                return { readLine = function() i = i + 1; return config[i] end, close = function() end }
            end
            function getFileWriter()
                return { write = function(_, s) output = output .. s end, close = function() end }
            end
            function isServer() return false end
            Events = setmetatable({}, { __index = function(t, k)
                local e = { Add = function(fn) t[k].callback = fn end }; t[k] = e; return e
            end })
            local world = { setGameMode = function(_, mode) assert(mode == "Sandbox") end,
                setWorld = function(_, name) assert(name == "fixture") end }
            function getWorld() return world end
            function checkSavePlayerExists() return alive end
            function getSaveInfo() return { worldVersion = version } end
            IsoWorld = { getWorldVersion = function() return 240 end }
            MainScreen = { continueLatestSave = function(mode, name)
                assert(mode == "Sandbox" and name == "fixture"); continued = true
            end }
            """);
        CompanionStartupTests.Install(script);
        script.DoString(ModTestService.Resource("Driver.lua"));
        script.DoString("Events.OnMainMenuEnter.callback()");
        Assert.Equal(continues, script.Globals.Get("continued").Boolean);
        var output = script.Globals.Get("output").String;
        if (!continues) Assert.Contains(alive ? "conversion" : "no living character", output);
        else Assert.DoesNotContain("\terror\t", output);
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
