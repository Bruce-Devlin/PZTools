using System.IO;
using System.Text;
using PZTools.Core.Models;

namespace PZTools.Core.Functions.Projects
{
    public static class ProjectHealthReportMarkdown
    {
        public static string Format(ProjectHealthReport report)
        {
            ArgumentNullException.ThrowIfNull(report);

            var builder = new StringBuilder();
            builder.AppendLine($"# PZTools Project Health: {EscapeText(report.Project.Name)}");
            builder.AppendLine();
            builder.AppendLine($"- Status: **{EscapeText(report.Summary)}**");
            builder.AppendLine($"- Checked: {report.CheckedAt:yyyy-MM-dd HH:mm:ss}");
            builder.AppendLine($"- Build targets: {report.Project.Targets.Count}");
            builder.AppendLine($"- Lua files: {report.LuaFileCount}");
            builder.AppendLine($"- ZedScript files: {report.ScriptFileCount}");
            builder.AppendLine($"- Content size: {FormatSize(report.ContentBytes)}");
            builder.AppendLine();
            builder.AppendLine("## Findings");
            builder.AppendLine();

            if (report.Diagnostics.Count == 0)
            {
                builder.AppendLine("No structural, content, or Lua syntax problems were found.");
                return builder.ToString();
            }

            builder.AppendLine("| Level | Code | Target | Location | Problem | Recommended action |");
            builder.AppendLine("| --- | --- | --- | --- | --- | --- |");
            foreach (var diagnostic in report.Diagnostics
                         .OrderByDescending(x => x.Severity)
                         .ThenBy(x => x.Target)
                         .ThenBy(x => x.Code))
            {
                builder.Append("| ").Append(EscapeCell(diagnostic.Severity.ToString()))
                    .Append(" | ").Append(EscapeCell(diagnostic.Code))
                    .Append(" | ").Append(EscapeCell(diagnostic.Target))
                    .Append(" | ").Append(EscapeCell(GetLocation(report.Project.RootPath, diagnostic)))
                    .Append(" | ").Append(EscapeCell(diagnostic.Message))
                    .Append(" | ").Append(EscapeCell(diagnostic.Recommendation))
                    .AppendLine(" |");
            }

            return builder.ToString();
        }

        private static string GetLocation(string projectRoot, ProjectDiagnostic diagnostic)
        {
            if (string.IsNullOrWhiteSpace(diagnostic.FilePath))
                return diagnostic.Target;

            string location;
            try
            {
                var fullRoot = Path.GetFullPath(projectRoot);
                var fullPath = Path.GetFullPath(diagnostic.FilePath);
                location = Path.GetRelativePath(fullRoot, fullPath);
                if (Path.IsPathRooted(location) ||
                    location.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                    location.Equals("..", StringComparison.Ordinal))
                    location = Path.GetFileName(fullPath);
            }
            catch
            {
                location = Path.GetFileName(diagnostic.FilePath);
            }

            return diagnostic.Line > 0 ? $"{location}:{diagnostic.Line}" : location;
        }

        private static string EscapeText(string value)
            => value.Replace("\\", "\\\\").Replace("`", "\\`");

        private static string EscapeCell(string value)
            => value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ").Trim();

        private static string FormatSize(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB" };
            double value = bytes;
            var unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            return $"{value:0.#} {units[unit]}";
        }
    }
}
