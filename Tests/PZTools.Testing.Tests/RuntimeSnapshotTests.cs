using System.IO;
using PZTools.Core.Functions.Projects;
using PZTools.Core.Models;

namespace PZTools.Testing.Tests;

public sealed class RuntimeSnapshotTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "PZToolsSnapshots", Guid.NewGuid().ToString("N"));
    private ModProject Project => new() { RootPath = Path.Combine(root, "Source"), ModInfo = new() { Id = "Fixture" } };
    private string Write(string relative, string text)
    {
        var path = Path.Combine(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text); return path;
    }

    [Fact]
    public async Task ResearchWritesAreExcludedAndAllEndpointsUseCapturedProjectAndDependency()
    {
        var runtime = Write("Source/media/run.lua", "original");
        var dependency = Write("Dependency/media/dep.lua", "dependency original");
        Write("Source/.agents/workspace/old.java", "research");
        Write("Source/media/valid.class", "legitimate payload");
        var profile = new PlaytestProfile { Mode = PlaytestMode.DedicatedServer, ClientCount = 2 };
        profile.Dependencies.Add(new() { ModId = "Dep", SourcePath = Path.Combine(root, "Dependency") });
        var snapshot = DeploymentSnapshotService.Capture(Project, profile, Path.Combine(root, "workspace"), default,
            () => { for (var i = 0; i < 20; i++) Write($"Source/.agents/workspace/{i}.class", "research"); });
        File.WriteAllText(runtime, "source changed after capture");
        File.WriteAllText(dependency, "dependency changed after capture");
        foreach (var endpoint in new[] { "server", "client1", "client2" })
            foreach (var payload in snapshot.Payloads)
                await ProjectDeployer.DeploySnapshot(payload, Path.Combine(root, endpoint), default);
        Assert.Empty(ClientServerParityService.Compare(Path.Combine(root, "server"), new[] { Path.Combine(root, "client1"), Path.Combine(root, "client2") }));
        Assert.Equal("original", File.ReadAllText(Path.Combine(root, "client2/Source/media/run.lua")));
        Assert.Equal("dependency original", File.ReadAllText(Path.Combine(root, "client2/Dep/media/dep.lua")));
        Assert.False(Directory.Exists(Path.Combine(root, "client1/Source/.agents")));
        Assert.True(File.Exists(Path.Combine(root, "client1/Source/media/valid.class")));
        Assert.Contains(snapshot.Id, File.ReadAllText(Path.Combine(root, "client1/Source", DeploymentManifestService.ManifestFileName)));
        Assert.Contains(DeploymentSnapshotService.Preview(Project.RootPath), p => p.Path == ".agents" && !p.Included);
        File.WriteAllText(Path.Combine(root, "client2/Source/media/run.lua"), "tampered");
        Assert.NotEmpty(ClientServerParityService.Compare(Path.Combine(root, "server"), new[] { Path.Combine(root, "client2") }));
    }

    [Fact]
    public void RuntimeAdditionDuringCaptureFailsWithPathBeforeDeployment()
    {
        Write("Source/media/run.lua", "original");
        var ex = Assert.Throws<IOException>(() => DeploymentSnapshotService.Capture(Project, new(), Path.Combine(root, "workspace"), default,
            () => Write("Source/media/added.lua", "new runtime")));
        Assert.Contains("added.lua", ex.Message);
    }

    [Fact]
    public void ActiveRuntimeWriterFailsWithPathAndCaptureLocksPreventMixedContent()
    {
        var runtime = Write("Source/media/run.lua", "original");
        using (var writer = new FileStream(runtime, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
        {
            var ex = Assert.Throws<IOException>(() => DeploymentSnapshotService.Capture(Project, new(), Path.Combine(root, "workspace"), default));
            Assert.Contains(Path.GetFullPath(runtime), ex.Message);
        }
        var snapshot = DeploymentSnapshotService.Capture(Project, new(), Path.Combine(root, "workspace"), default,
            () => Assert.Throws<IOException>(() => File.WriteAllText(runtime, "changed")));
        Assert.Equal("original", File.ReadAllText(Path.Combine(snapshot.Payloads[0].RootPath, "media/run.lua")));
    }

    [Fact]
    public async Task TamperedSnapshotIsRejectedWithoutReplacingPreviousEndpoint()
    {
        Write("Source/media/run.lua", "original");
        var snapshot = DeploymentSnapshotService.Capture(Project, new(), Path.Combine(root, "workspace"), default);
        await ProjectDeployer.DeploySnapshot(snapshot.Payloads[0], Path.Combine(root, "client"), default);
        File.WriteAllText(Path.Combine(snapshot.Payloads[0].RootPath, "media/run.lua"), "tampered");
        await Assert.ThrowsAsync<IOException>(() => ProjectDeployer.DeploySnapshot(snapshot.Payloads[0], Path.Combine(root, "client"), default));
        Assert.Equal("original", File.ReadAllText(Path.Combine(root, "client/Source/media/run.lua")));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
