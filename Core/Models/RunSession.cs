namespace PZTools.Core.Models;

public sealed class SessionProcess
{
    public int Id { get; set; }
    public DateTime StartedUtc { get; set; }
    public string Endpoint { get; set; } = "";
    public string Executable { get; set; } = "";
    public string CachePath { get; set; } = "";
    public string LogPath { get; set; } = "";
    public List<string> SavePaths { get; set; } = new();
}

public sealed class RunSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? OperationId { get; set; }
    public string? ReportId { get; set; }
    public string ProjectPath { get; set; } = "";
    public string Kind { get; set; } = "";
    public string State { get; set; } = "preparing";
    public string Verdict { get; set; } = "not applicable";
    public string Retention { get; set; } = "retained";
    public string Shutdown { get; set; } = "not requested";
    public string SaveEvidence { get; set; } = "not observed";
    public string RestartVerification { get; set; } = "not performed";
    public string? RestartOf { get; set; }
    public string? PersistenceToken { get; set; }
    public Dictionary<string, string> SaveReceipts { get; set; } = new();
    public bool OwnsGame { get; set; }
    public int OwnerId { get; set; }
    public DateTime OwnerStartedUtc { get; set; }
    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? EndedUtc { get; set; }
    public string GameRoot { get; set; } = "";
    public PlaytestProfile? Profile { get; set; }
    public string WorkspacePath { get; set; } = "";
    public string ArtifactDirectory { get; set; } = "";
    public string? SourceSnapshotId { get; set; }
    public string? PayloadManifestPath { get; set; }
    public string? Error { get; set; }
    public List<SessionProcess> Processes { get; set; } = new();
    public string Display => $"{StartedUtc.ToLocalTime():g} · {Kind} · {State} · {Verdict} · {Retention} · {Profile?.Name ?? Id}";
}
