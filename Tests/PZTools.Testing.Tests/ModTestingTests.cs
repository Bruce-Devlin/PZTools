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

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
