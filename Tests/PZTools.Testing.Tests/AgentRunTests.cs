using Newtonsoft.Json.Linq;
using PZTools.Core.Functions.Agent;
using PZTools.Core.Models;

namespace PZTools.Testing.Tests;

public sealed class AgentRunTests
{
    private static JObject Snapshot(AgentRunTracker tracker) => JObject.FromObject(tracker.Snapshot());

    [Fact]
    public async Task CancellationWaitsForCleanupAndRejectsOtherRunIds()
    {
        await using var tracker = new AgentRunTracker();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        tracker.Start("game", async (_, ct) =>
        {
            started.SetResult();
            try { await Task.Delay(Timeout.Infinite, ct); }
            finally { await cleanup.Task; }
            return new { passed = true };
        });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Throws<InvalidOperationException>(() => tracker.Start("unit", (_, _) => Task.FromResult<object>(new { })));
        Assert.Throws<ArgumentException>(() => tracker.Cancel("wrong"));
        tracker.Cancel(Snapshot(tracker).Value<string>("runId")!);
        Assert.Equal("cancelling", Snapshot(tracker).Value<string>("state"));
        cleanup.SetResult();
        await tracker.DisposeAsync();
        Assert.Equal("cancelled", Snapshot(tracker).Value<string>("state"));
    }

    [Fact]
    public async Task PreservesFailedTestReportAndBoundsOutput()
    {
        var tracker = new AgentRunTracker();
        tracker.Start("unit", (log, _) =>
        {
            for (var i = 0; i < 2100; i++) log(i.ToString());
            return Task.FromResult<object>(new { Passed = false, Errors = new[] { "assertion failed" } });
        });
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (Snapshot(tracker).Value<string>("state") == "running" && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.Equal("completed", Snapshot(tracker).Value<string>("state"));
        var snapshot = JObject.FromObject(tracker.Snapshot(lines: 3000));
        Assert.False(snapshot["result"]!.Value<bool>("Passed"));
        Assert.Equal(2000, snapshot["output"]!.Count());
        Assert.Equal("100", snapshot["output"]![0]!.Value<string>());
        await tracker.DisposeAsync();
    }

    [Fact]
    public async Task ShutdownCancelsWorkAndCapturesRunnerErrors()
    {
        var tracker = new AgentRunTracker();
        tracker.Start("unit", (_, _) => throw new InvalidOperationException("fixture failure"));
        await tracker.DisposeAsync();
        Assert.Equal("failed", Snapshot(tracker).Value<string>("state"));
        Assert.Equal("fixture failure", Snapshot(tracker).Value<string>("error"));
        Assert.Throws<ObjectDisposedException>(() => tracker.Start("unit", (_, _) => Task.FromResult<object>(new { })));
    }

    [Fact]
    public void CatalogHonorsCombinedPermissionsAndKeepsCancellationAvailable()
    {
        var settings = new AppSettings { AgentMcpAllowTesting = true, AgentMcpAllowDeployment = false };
        Assert.Contains("pztools_start_test_run", AgentMcpConfiguration.GetEnabledTools(settings));
        Assert.DoesNotContain("pztools_start_playtest", AgentMcpConfiguration.GetEnabledTools(settings));
        settings.AgentMcpAllowDeployment = true;
        Assert.Contains("pztools_start_playtest", AgentMcpConfiguration.GetEnabledTools(settings));
        settings.AgentMcpAllowGameControl = false;
        Assert.DoesNotContain("pztools_start_playtest", AgentMcpConfiguration.GetEnabledTools(settings));
        settings.AgentMcpAllowTesting = false;
        var tools = AgentMcpConfiguration.GetEnabledTools(settings);
        Assert.DoesNotContain("pztools_start_test_run", tools);
        Assert.DoesNotContain("pztools_scaffold_tests", tools);
        Assert.Contains("pztools_cancel_run", tools);
    }
}
