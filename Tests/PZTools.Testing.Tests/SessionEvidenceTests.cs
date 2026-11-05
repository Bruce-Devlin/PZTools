using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;
using PZTools.Core.Functions.Projects;
using PZTools.Core.Functions.Tester;
using PZTools.Core.Models;

namespace PZTools.Testing.Tests;

public sealed class SessionEvidenceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "PZToolsSessionEvidence", Guid.NewGuid().ToString("N"));
    public SessionEvidenceTests() => Directory.CreateDirectory(root);
    private ModProject Project => new() { RootPath = root, Name = "Evidence", ModInfo = new() { Id = "Evidence" } };
    private void Write(string relative, string content)
    {
        var path = Path.Combine(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, content);
    }

    [Fact]
    public void CompetingLaunchCannotReplaceOwnershipAndInterruptedRunKeepsArtifacts()
    {
        var registry = new SessionRegistry(Path.Combine(root, "registry"));
        var first = registry.Begin(root, "manual", true, new PlaytestProfile { KeepSessionData = false });
        first.WorkspacePath = Path.Combine(root, "world"); Directory.CreateDirectory(first.WorkspacePath);
        File.WriteAllText(Path.Combine(first.WorkspacePath, "players.db"), "established character");
        registry.Save(first);
        Assert.Throws<InvalidOperationException>(() => registry.Begin(root, "automated", true));
        Assert.Single(registry.List());
        first.OwnerId = int.MaxValue; registry.Save(first);
        var restarted = new SessionRegistry(Path.Combine(root, "registry"));
        var recovered = Assert.Single(restarted.List());
        Assert.Equal("interrupted", recovered.State); Assert.Equal("retained", recovered.Retention);
        Assert.Equal("established character", File.ReadAllText(Path.Combine(first.WorkspacePath, "players.db")));
        Assert.NotNull(restarted.Begin(root, "automated", true));
    }

    [Fact]
    public void OrphanedLiveProcessStillBlocksLaunchButReusedPidDoesNot()
    {
        var registry = new SessionRegistry(Path.Combine(root, "registry"));
        using var current = Process.GetCurrentProcess();
        var record = registry.Begin(root, "manual", true);
        record.OwnerId = int.MaxValue;
        record.Processes.Add(new SessionProcess { Id = current.Id, StartedUtc = current.StartTime.ToUniversalTime() }); registry.Save(record);
        Assert.Throws<InvalidOperationException>(registry.AssertAvailable);
        record.Processes[0].StartedUtc = current.StartTime.ToUniversalTime().AddDays(-1); registry.Save(record);
        registry.AssertAvailable();
    }

    [Fact]
    public async Task CrashedManualRunnerRetainsWorldEvenWithLegacyDisposableProfile()
    {
        var projectRoot = Path.Combine(root, "project"); Directory.CreateDirectory(projectRoot);
        File.WriteAllText(Path.Combine(projectRoot, "mod.info"), "name=CrashFixture\nid=CrashFixture\n");
        var install = Path.Combine(root, "installation"); Directory.CreateDirectory(install);
        File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"), Path.Combine(install, "ProjectZomboid64.exe"));
        var project = new ModProject { RootPath = projectRoot, Name = "CrashFixture", ModInfo = new() { Id = "CrashFixture" } };
        using var runner = new PlaytestSessionRunner();
        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(project,
            new PlaytestProfile { KeepSessionData = false, LaunchArguments = "/c exit 23" }, install));
        var session = SessionRegistry.Default.Get(runner.CurrentSessionId!);
        Assert.Equal("interrupted", session.State); Assert.Equal("retained", session.Retention);
        Assert.True(Directory.Exists(session.WorkspacePath));
        Assert.True(File.Exists(Path.Combine(session.WorkspacePath, "session.log")));
    }

    [Fact]
    public void CorruptOwnershipReceiptFailsClosedAndIsPreserved()
    {
        var folder = Path.Combine(root, "registry"); Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "broken.json"), "{broken");
        var registry = new SessionRegistry(folder);
        Assert.Contains("broken.json", Assert.Throws<IOException>(registry.AssertAvailable).Message);
        Assert.Equal("{broken", File.ReadAllText(Path.Combine(folder, "broken.json")));
    }

    [Fact]
    public async Task SetupFailureSkipsExerciseRetainsCleanupAndAttachesOneCaseEvidence()
    {
        Write(".pztests/setup.unit.lua", """
            beforeEach(function(t)
                t:cleanup(function() t:step("cleanup observed", function() end) end)
                t:prerequisite("living test character", function() return false end, 0)
            end)
            test("requires character", function(t) t:step("MUST NOT EXERCISE", function() end) end)
            """);
        var report = await ModTestService.RunUnitAsync(Project);
        var test = Assert.Single(report.Tests);
        Assert.Equal("failed", test.Status); Assert.Equal("setup", test.FailureOrigin); Assert.Equal("Setup", test.LastPhase);
        Assert.DoesNotContain("Exercise", test.Phases); Assert.Contains("Cleanup", test.Phases);
        Assert.DoesNotContain("MUST NOT EXERCISE", test.Steps); Assert.Contains("cleanup observed", test.Steps);
        Assert.Contains("living test character", test.Diagnostic!.Assertion);
        Assert.Equal("completed", report.OperationState); Assert.Equal("failed", report.Verdict); Assert.Empty(report.Errors);
        Assert.True(File.Exists(Path.Combine(report.SourceDirectory, ".pztests", "setup.unit.lua")));
        Write(".pztests/setup.unit.lua", "test('changed', function(t) t:truthy(true) end)");
        Assert.Contains(".pztests/setup.unit.lua", TestEvidenceService.ChangedFiles(report));
        Assert.Equal(report.RunId, SessionRegistry.Default.Get(report.SessionId!).ReportId);
    }

    [Fact]
    public async Task InstalledKahluaLabelsCompilationAndRejectsInvalidSyntax()
    {
        var installation = GameInstallation.Discover().FirstOrDefault(p => File.Exists(Path.Combine(p, "projectzomboid.jar")) && File.Exists(Path.Combine(p, "jre64", "bin", "java.exe")));
        // This acceptance check is also run explicitly against the selected installation.
        if (installation is null) return;
        Write("valid.lua", "local value = 1 + 2"); Write("invalid.lua", "local =");
        var result = await NativeLuaValidator.CompileAsync(installation, new[] { Path.Combine(root, "valid.lua"), Path.Combine(root, "invalid.lua") });
        Assert.True(result[0].Result.Ok); Assert.False(result[1].Result.Ok);
        Assert.Contains("no API behavior", result[0].Result.Scope); Assert.Equal(64, result[0].Result.BinaryFingerprint!.Length);
    }

    [Fact]
    public void RestartRequiresSaveProofAndNeverAcceptsForcedTermination()
    {
        var session = new RunSession { Profile = new PlaytestProfile(), Shutdown = "forced termination", SaveEvidence = "completed" };
        Assert.Throws<InvalidOperationException>(() => SessionLifecycleService.RestartProfile(session));
        session.Shutdown = "clean shutdown"; session.SaveEvidence = "not observed";
        Assert.Throws<InvalidOperationException>(() => SessionLifecycleService.RestartProfile(session));
    }

    [Fact]
    public async Task FailedReplacementKeepsPrivateStagingAndPreviousInstallation()
    {
        var source = Path.Combine(root, "source"); var mods = Path.Combine(root, "cache", "mods");
        Directory.CreateDirectory(source); File.WriteAllText(Path.Combine(source, "mod.info"), "name=Evidence\nid=Evidence");
        var project = new ModProject { RootPath = source, ModInfo = new() { Id = "Evidence" } };
        await ProjectDeployer.DeployProject(project, mods);
        var installed = Path.Combine(mods, "source", "mod.info");
        using var held = new FileStream(installed, FileMode.Open, FileAccess.Read, FileShare.Read);
        File.WriteAllText(Path.Combine(source, "mod.info"), "name=Updated\nid=Evidence");
        var error = await Assert.ThrowsAsync<IOException>(() => ProjectDeployer.DeployProject(project, mods));
        Assert.Contains("replacement", error.Message);
        Assert.DoesNotContain(Directory.EnumerateDirectories(mods), p => Path.GetFileName(p).Contains("staging"));
        var receipts = Directory.EnumerateFiles(Path.Combine(root, "cache", ".pztools-deployment"), "deployment.json", SearchOption.AllDirectories)
            .Select(p => Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(p))).ToArray();
        var failed = Assert.Single(receipts, r => r.Value<string>("Error") is not null);
        Assert.True(Directory.Exists(failed.Value<string>("Staging")));
        using var reader = new StreamReader(held); Assert.Contains("name=Evidence", reader.ReadToEnd());
    }

    [Fact]
    public void RelocationCopiesAndVerifiesWithoutOverwritingOriginalOrDestination()
    {
        Write("mod.info", "id=Evidence"); Write(".pztests/test.unit.lua", "-- test"); Write(".pztools/important-save", "world");
        Directory.CreateDirectory(Path.Combine(root, "common"));
        var destination = Path.Combine(Path.GetTempPath(), "PZToolsRelocation", Guid.NewGuid().ToString("N"));
        try
        {
            ProjectStorageService.CopySource(root, destination);
            Assert.Equal("id=Evidence", File.ReadAllText(Path.Combine(destination, "mod.info")));
            Assert.True(Directory.Exists(Path.Combine(destination, "common")));
            Assert.Equal("world", File.ReadAllText(Path.Combine(root, ".pztools", "important-save")));
            Assert.Throws<IOException>(() => ProjectStorageService.CopySource(root, destination));
        }
        finally { if (Directory.Exists(destination)) Directory.Delete(destination, true); }
    }
    [Fact]
    public void StorageCoverageCountsIgnoredSourceWithoutCountingRunArtifacts()
    {
        Write("mod.info", "id=Evidence"); Write("media/ignored.lua", "-- source"); Write(".pztools/log.txt", "runtime artifact");
        Write(".gitignore", "media/\n.pztools/\n");
        Assert.Equal(0, ProjectStorageService.Git(root, "init").Exit);
        Assert.Equal(0, ProjectStorageService.Git(root, "add", "mod.info").Exit);
        var status = ProjectStorageService.Inspect(root);
        Assert.Equal(1, status.Tracked); Assert.Equal(1, status.Ignored);
        Assert.Contains("Source coverage", status.Detail);
    }
    [Fact]
    public void WorkspaceLocationIsRecordedBeforeCloningCanFail()
    {
        PlaytestSessionWorkspace? allocated = null;
        var project = new ModProject { RootPath = root };
        var profile = new PlaytestProfile { SaveMode = PlaytestSaveMode.FreshClone, SourceSavePath = Path.Combine(root, "missing-save") };
        try
        {
            Assert.Throws<DirectoryNotFoundException>(() => PlaytestWorkspaceService.Prepare(project, profile, w => allocated = w));
            Assert.NotNull(allocated);
            Assert.True(Directory.Exists(allocated.RootPath));
        }
        finally { if (allocated is not null) PlaytestWorkspaceService.Cleanup(allocated, false); }
    }
    [Fact]
    public void RetainedServerKeepsResetIdentityWhenRewritingManagedOptions()
    {
        var profile = new PlaytestProfile { Mode = PlaytestMode.DedicatedServer, SaveMode = PlaytestSaveMode.FreshSessionClone };
        var server = Path.Combine(root, "server"); Directory.CreateDirectory(Path.Combine(server, "Server"));
        var ini = Path.Combine(server, "Server", profile.ServerName + ".ini");
        File.WriteAllText(ini, "ResetID=197263\nMods=OldMod\n");
        PlaytestServerConfig.Write(profile, new PlaytestSessionWorkspace { RootPath = root, ServerCachePath = server, ClientCachePaths = Array.Empty<string>() }, "NewMod");
        Assert.Contains("ResetID=197263", File.ReadAllText(ini)); Assert.DoesNotContain("OldMod", File.ReadAllText(ini));
    }
    [Fact]
    public void EndpointLogChunksPreserveUnicodeAndIdentifyTruncationAndOwnership()
    {
        var session = SessionRegistry.Default.Begin(root, "log acceptance", false);
        using var process = Process.GetCurrentProcess();
        SessionRegistry.Default.AddProcess(session, process, "client-2", root);
        Write("console.txt", "αβγδε\nend");
        long offset = 0; var text = "";
        while (offset < new FileInfo(Path.Combine(root, "console.txt")).Length)
        {
            var chunk = TestEvidenceService.ReadLog(root, session.Id, "client-2", offset, 5);
            Assert.Equal(process.Id, chunk.ProcessId); Assert.True(chunk.NextOffset > offset);
            offset = chunk.NextOffset; text += chunk.Text;
        }
        Assert.Equal("αβγδε\nend", text);
        Write("console.txt", "new");
        Assert.True(TestEvidenceService.ReadLog(root, session.Id, "client-2", offset).Reset);
        Assert.Throws<UnauthorizedAccessException>(() => TestEvidenceService.ReadLog(Path.Combine(root, "other"), session.Id, "client-2"));
        SessionRegistry.Default.Finish(session, "completed");
    }
    [Fact]
    public async Task KnowledgeIndexReloadsBinaryProvenanceEvenWhenSourceCacheMatches()
    {
        Write("zombie/Example.java", "package zombie; public class Example { public void call() {} }");
        var origin = new GameInstallationIdentity("installed", "Steam build 123", "projectzomboid.jar", new string('A', 64));
        Write(".pztools-reference-origin.json", JsonConvert.SerializeObject(origin));
        var index = await PZTools.Core.Functions.Decompile.GameKnowledgeBase.LoadOrBuildAsync(root, "42");
        Assert.Equal(origin, index.Origin);
        origin = origin with { Sha256 = new string('B', 64) };
        Write(".pztools-reference-origin.json", JsonConvert.SerializeObject(origin));
        var cached = await PZTools.Core.Functions.Decompile.GameKnowledgeBase.LoadOrBuildAsync(root, "42");
        Assert.Equal(origin, cached.Origin);
    }
    public void Dispose()
    {
        if (!Directory.Exists(root)) return;
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(root, true);
    }
}
