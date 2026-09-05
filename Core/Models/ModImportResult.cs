namespace PZTools.Core.Models
{
    public enum ModImportIssueSeverity
    {
        Information,
        Warning
    }

    public sealed class ModImportIssue
    {
        public ModImportIssueSeverity Severity { get; init; }
        public string Message { get; init; } = "";
    }

    public sealed class ModImportInspection
    {
        public string SourcePath { get; init; } = "";
        public string ContentRootPath { get; init; } = "";
        public string SuggestedProjectName { get; init; } = "";
        public List<ModImportIssue> Issues { get; } = new();
    }

    public sealed class ModImportResult
    {
        public ModProject Project { get; init; } = new();
        public List<ModImportIssue> Issues { get; } = new();

        public int RepairCount => Issues.Count(x => x.Severity == ModImportIssueSeverity.Information);
        public int WarningCount => Issues.Count(x => x.Severity == ModImportIssueSeverity.Warning);
    }
}
