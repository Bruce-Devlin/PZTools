namespace PZTools.Core.Models.Test;

public sealed class ModTestCase
{
    public string File { get; set; } = "";
    public string Name { get; set; } = "";
    public string Endpoint { get; set; } = "unit";
    public string Status { get; set; } = "running";
    public string Message { get; set; } = "";
    public double DurationSeconds { get; set; }
    public List<string> Steps { get; set; } = new();
}

public sealed class ModTestReport
{
    public string RunId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
    public string Project { get; set; } = "";
    public string Mode { get; set; } = "unit";
    public string GameRoot { get; set; } = "";
    public string Profile { get; set; } = "";
    public string ArtifactDirectory { get; set; } = "";
    public List<ModTestCase> Tests { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public bool Passed => Tests.Count > 0 && Errors.Count == 0 && Tests.All(t => t.Status == "passed");
}
