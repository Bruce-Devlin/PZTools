using System.IO;
using PZTools.Core.Functions.Tester;
using PZTools.Core.Models.Test;

namespace PZTools.Testing.Tests;

public sealed class EndpointStateTests
{
    [Theory]
    [InlineData("awaiting_world|0|unknown|false|false|true", "Waiting for world entry")]
    [InlineData("awaiting_player|0|false|false|false|true", "No living playable character")]
    [InlineData("support_setup|2|false|true|false|true", "Support registration")]
    [InlineData("awaiting_ui|2|false|true|true|false", "Startup panels")]
    [InlineData("awaiting_barrier|2|true|true|true|true", "World is paused")]
    [InlineData("awaiting_barrier|0|unknown|true|true|true", "Simulation ticks")]
    public void UnrunnableStatesDoNotReleaseBarrier(string message, string reason)
    {
        var state = new GameTestEndpointState(); var now = DateTime.UtcNow;
        GameTestService.ApplyStatus(state, message, "", now);
        Assert.False(state.IsRunnable(now)); Assert.StartsWith(reason, state.BlockingReason);
    }

    [Fact]
    public void HeartbeatsDoNotHideStalledTicksAndDeathOrPauseAreReportedDuringCase()
    {
        var state = new GameTestEndpointState(); var now = DateTime.UtcNow;
        GameTestService.ApplyStatus(state, "awaiting_barrier|2|false|true|true|true", "", now);
        Assert.True(state.IsRunnable(now));
        GameTestService.ApplyStatus(state, "awaiting_barrier|2|false|true|true|true", "", now.AddSeconds(6));
        Assert.False(state.IsRunnable(now.AddSeconds(6))); Assert.Contains("ticks", state.BlockingReason);
        GameTestService.ApplyStatus(state, "running|3|true|true|true|true", "pause-sensitive", now.AddSeconds(7));
        Assert.Equal("pause-sensitive", state.CurrentCase); Assert.Equal("World is paused.", state.BlockingReason);
        GameTestService.ApplyStatus(state, "awaiting_player|4|false|false|true|true", "death case", now.AddSeconds(8));
        Assert.False(state.PlayerPresent); Assert.Contains("living", state.BlockingReason);
        state.Refresh(now.AddSeconds(20)); Assert.Contains("heartbeat stalled", state.BlockingReason);
    }

    [Fact]
    public void MalformedOrRegressingHeartbeatCannotClaimReadiness()
    {
        var state = new GameTestEndpointState { SimulationTicks = 10 };
        Assert.Throws<InvalidDataException>(() => GameTestService.ApplyStatus(state, "awaiting_barrier|9|false|true|true|true", "", DateTime.UtcNow));
        Assert.Throws<InvalidDataException>(() => GameTestService.ApplyStatus(state, "invented|11|false|true|true|true", "", DateTime.UtcNow));
    }
}
