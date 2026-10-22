namespace PZTools.Core.Models.Test;

public sealed class GameTestEndpointState
{
    public string Endpoint { get; set; } = "";
    public string Phase { get; set; } = "awaiting_companion";
    public DateTime? LastHeartbeat { get; set; }
    public DateTime? LastSimulationTick { get; set; }
    public long SimulationTicks { get; set; }
    public bool? Paused { get; set; }
    public bool? PlayerPresent { get; set; }
    public bool SupportReady { get; set; }
    public bool UiReady { get; set; }
    public string CurrentCase { get; set; } = "";
    public string BlockingReason { get; set; } = "No companion heartbeat; screen state unknown.";

    public bool IsRunnable(DateTime now) => Phase == "awaiting_barrier" && IsReady(now);

    public bool IsReady(DateTime now) => (Phase is "awaiting_barrier" or "running") && SupportReady && UiReady &&
        PlayerPresent == true && Paused != true && SimulationTicks >= 2 &&
        LastHeartbeat >= now.AddSeconds(-5) && LastSimulationTick >= now.AddSeconds(-5);

    public void Refresh(DateTime now)
    {
        BlockingReason = Phase is "complete" or "failed" ? "" :
            LastHeartbeat is null ? "No companion heartbeat; screen state unknown." :
            LastHeartbeat < now.AddSeconds(-5) ? "Companion heartbeat stalled; screen state unknown." :
            Phase == "awaiting_world" ? "Waiting for world entry; screen state unknown." :
            PlayerPresent == false ? "No living playable character." :
            !SupportReady ? "Support registration has not completed." :
            !UiReady ? "Startup panels have not been cleared." :
            Paused == true ? "World is paused." :
            LastSimulationTick is null || LastSimulationTick < now.AddSeconds(-5) ? "Simulation ticks are not advancing." :
            SimulationTicks < 2 ? "Waiting for demonstrated simulation tick progress." : "";
    }
}
