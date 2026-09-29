using System.IO;
using PZTools.Core.Functions.Projects;
using PZTools.Core.Models;

namespace PZTools.Testing.Tests;

public sealed class DeploymentTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PZToolsDeployment-" + Guid.NewGuid().ToString("N"));
    private ModProject Project => new() { RootPath = Path.Combine(_root, "Source"), Name = "Source", ModInfo = new() { Id = "Fixture" } };
    private string Destination => Path.Combine(_root, "mods");
    private string Deployed => Path.Combine(Destination, "Source");

    public DeploymentTests()
    {
        Directory.CreateDirectory(Path.Combine(Project.RootPath, "42", "media"));
        File.WriteAllText(Path.Combine(Project.RootPath, "42", "media", "fixture.txt"), "original");
    }

    [Fact]
    public async Task RefusesOverlappingDestinationsWithoutChangingSource()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => ProjectDeployer.DeployProject(Project, _root));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ProjectDeployer.DeployProject(Project, Path.Combine(Project.RootPath, "output")));
        Assert.Equal("original", File.ReadAllText(Path.Combine(Project.RootPath, "42", "media", "fixture.txt")));
        Assert.False(Directory.Exists(Path.Combine(Project.RootPath, "output")));
    }

    [Fact]
    public async Task IsolatedDeploymentPrunesCachesAndAgentConfiguration()
    {
        Directory.CreateDirectory(Path.Combine(Project.RootPath, ".codex"));
        File.WriteAllText(Path.Combine(Project.RootPath, ".codex", "config.toml"), "private tooling config");
        var isolated = Path.Combine(Project.RootPath, ".pztools", "playtests", "mods");
        await ProjectDeployer.DeployProject(Project, isolated);
        var deployed = Path.Combine(isolated, "Source");
        Assert.False(Directory.Exists(Path.Combine(deployed, ".codex")));
        Assert.False(Directory.Exists(Path.Combine(deployed, ".pztools")));
        Assert.Empty(DeploymentManifestService.Verify(Project.RootPath, deployed, "Fixture"));
    }

    [Fact]
    public async Task DetectsUnexpectedFilesAndRedeploymentRemovesThem()
    {
        await ProjectDeployer.DeployProject(Project, Destination);
        File.WriteAllText(Path.Combine(Deployed, "42", "media", "stale.lua"), "return true");
        Assert.Contains(DeploymentManifestService.Verify(Project.RootPath, Deployed, "Fixture"), x => x.Code == "PZD014");
        await ProjectDeployer.DeployProject(Project, Destination);
        Assert.Empty(DeploymentManifestService.Verify(Project.RootPath, Deployed, "Fixture"));
    }

    [Theory]
    [InlineData("{\"Files\":[null]}")]
    [InlineData("{\"Files\":[{\"Path\":null}]}")]
    [InlineData("{\"Files\":[{\"Path\":\"../outside.txt\"}]}")]
    public async Task MalformedManifestProducesDiagnostic(string json)
    {
        await ProjectDeployer.DeployProject(Project, Destination);
        File.WriteAllText(Path.Combine(Deployed, DeploymentManifestService.ManifestFileName), json);
        Assert.Contains(DeploymentManifestService.Verify(Project.RootPath, Deployed, "Fixture"), x => x.Code == "PZD002");
    }

    [Fact]
    public async Task FailedStagingPreservesPreviousDeploymentAndCleansTemporaryFiles()
    {
        await ProjectDeployer.DeployProject(Project, Destination);
        using (var locked = new FileStream(Path.Combine(Project.RootPath, "42", "media", "fixture.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await Assert.ThrowsAsync<IOException>(() => ProjectDeployer.DeployProject(Project, Destination));
        Assert.Empty(DeploymentManifestService.Verify(Project.RootPath, Deployed, "Fixture"));
        Assert.Empty(Directory.EnumerateDirectories(Destination, ".*"));
    }

    [Fact]
    public async Task ConcurrentRequestsLeaveOneCompleteDeployment()
    {
        await Task.WhenAll(ProjectDeployer.DeployProject(Project, Destination), ProjectDeployer.DeployProject(Project, Destination));
        Assert.Empty(DeploymentManifestService.Verify(Project.RootPath, Deployed, "Fixture"));
        Assert.Empty(Directory.EnumerateDirectories(Destination, ".*"));
    }

    [Fact]
    public async Task CancellationPreservesPreviousDeployment()
    {
        await ProjectDeployer.DeployProject(Project, Destination);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ProjectDeployer.DeployProject(Project, Destination, cancellation.Token));
        Assert.Empty(DeploymentManifestService.Verify(Project.RootPath, Deployed, "Fixture"));
        Assert.Empty(Directory.EnumerateDirectories(Destination, ".*"));
    }

    public void Dispose() => Directory.Delete(_root, true);
}
