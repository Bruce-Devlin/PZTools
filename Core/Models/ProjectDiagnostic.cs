using System.IO;

namespace PZTools.Core.Models
{
    public enum DiagnosticSeverity
    {
        Info,
        Warning,
        Error
    }

    public sealed class ProjectDiagnostic
    {
        public DiagnosticSeverity Severity { get; init; }
        public string Code { get; init; } = "";
        public string Target { get; init; } = "";
        public string Message { get; init; } = "";
        public string Recommendation { get; init; } = "";
        public string? FilePath { get; init; }
        public int? Line { get; init; }

        public string SeverityIcon => Severity switch
        {
            DiagnosticSeverity.Error => "ERROR",
            DiagnosticSeverity.Warning => "WARN",
            _ => "INFO"
        };

        public string Location => Line > 0
            ? $"{Path.GetFileName(FilePath)}:{Line}"
            : string.IsNullOrWhiteSpace(FilePath) ? Target : Path.GetFileName(FilePath);
    }

    public sealed class ProjectHealthReport
    {
        public required ModProject Project { get; init; }
        public List<ProjectDiagnostic> Diagnostics { get; } = new();
        public int LuaFileCount { get; set; }
        public int ScriptFileCount { get; set; }
        public long ContentBytes { get; set; }
        public DateTime CheckedAt { get; } = DateTime.Now;

        public int ErrorCount => Diagnostics.Count(x => x.Severity == DiagnosticSeverity.Error);
        public int WarningCount => Diagnostics.Count(x => x.Severity == DiagnosticSeverity.Warning);
        public bool IsReadyToDeploy => ErrorCount == 0;
        public string Summary => ErrorCount > 0
            ? $"{ErrorCount} error(s), {WarningCount} warning(s)"
            : WarningCount > 0 ? $"Ready with {WarningCount} warning(s)" : "Ready to deploy";
    }
}
