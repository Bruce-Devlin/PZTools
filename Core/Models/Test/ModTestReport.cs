namespace PZTools.Core.Models.Test;

public sealed class ModTestCase
{
    public List<string> ObservedApis { get; set; } = new();
    public string File { get; set; } = "";
    public string Name { get; set; } = "";
    public string Endpoint { get; set; } = "unit";
    public string Status { get; set; } = "running";
    public string Message { get; set; } = "";
    public string? FailureOrigin { get; set; }
    public double DurationSeconds { get; set; }
    public List<string> Steps { get; set; } = new();
    public string LastPhase { get; set; } = "Setup";
    public List<string> Phases { get; set; } = new();
    public CaseDiagnostic? Diagnostic { get; set; }
}

public sealed class CaseDiagnostic
{
    public string Assertion { get; set; } = "";
    public string Phase { get; set; } = "";
    public GameTestEndpointState? Endpoint { get; set; }
    public DateTime CapturedUtc { get; set; } = DateTime.UtcNow;
    public int? ProcessId { get; set; }
    public string? Window { get; set; }
    public string? Screenshot { get; set; }
    public string CaptureNote { get; set; } = "No live client window available.";
    public string LogPath { get; set; } = "";
    public string LogExcerpt { get; set; } = "";
}

public sealed class ModTestReport
{
    public string RunId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
    public string Project { get; set; } = "";
    public string Mode { get; set; } = "unit";
    public string GameRoot { get; set; } = "";
    public string GameBinaryFingerprint { get; set; } = "";
    public string Profile { get; set; } = "";
    public string ArtifactDirectory { get; set; } = "";
    public string? SourceSnapshotId { get; set; }
    public string? PayloadManifestPath { get; set; }
    public string? SessionId { get; set; }
    public string? OperationId { get; set; }
    public string OperationState { get; set; } = "running";
    public string Verdict => Tests.Count == 0 ? "untested" : Tests.Any(t => t.Status == "failed") ? "failed" :
        Tests.All(t => t.Status == "passed") ? "passed" : "incomplete";
    public string ProjectPath { get; set; } = "";
    public string SourceDirectory { get; set; } = "";
    public Dictionary<string, string> SourceHashes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> SourceChangedDuringRun { get; set; } = new();
    public List<string> Conditions { get; set; } = new();
    public List<ModTestCase> Tests { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public List<GameTestEndpointState> Endpoints { get; set; } = new();
    public List<CaseDiagnostic> StartupDiagnostics { get; set; } = new();
    public bool Passed => Tests.Count > 0 && Errors.Count == 0 && Tests.All(t => t.Status == "passed");
}
