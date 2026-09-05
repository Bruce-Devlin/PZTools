using System.IO;
using System.Text.RegularExpressions;
using PZTools.Core.Functions.Zomboid;
using PZTools.Core.Models;

namespace PZTools.Core.Functions.Projects
{
    public sealed record GameLogSession(string LogPath, long StartOffset, DateTime StartedAtUtc);

    public static class GameLogAnalyzer
    {
        private const int MaxLines = 12000;
        private const int MaxFindings = 100;

        public static IReadOnlyList<ProjectDiagnostic> FindProjectIssues(ModProject project)
        {
            var logPath = Path.Combine(ZomboidGame.GameUserDirectory, "console.txt");
            if (!File.Exists(logPath))
                return Array.Empty<ProjectDiagnostic>();

            string[] lines;
            try
            {
                lines = ReadTailLines(logPath, MaxLines);
            }
            catch { return Array.Empty<ProjectDiagnostic>(); }

            return FindProjectIssues(project, lines, logPath, Math.Max(0, lines.Length - MaxLines), existingConsoleLog: true);
        }

        public static GameLogSession BeginSession()
            => BeginSession(Path.Combine(ZomboidGame.GameUserDirectory, "console.txt"));

        public static GameLogSession BeginSession(string logPath)
        {
            long offset = 0;
            try
            {
                if (File.Exists(logPath))
                    offset = new FileInfo(logPath).Length;
            }
            catch { }
            return new GameLogSession(logPath, offset, DateTime.UtcNow);
        }

        public static IReadOnlyList<ProjectDiagnostic> FindProjectIssues(ModProject project, GameLogSession session)
        {
            ArgumentNullException.ThrowIfNull(project);
            ArgumentNullException.ThrowIfNull(session);
            if (!File.Exists(session.LogPath))
                return Array.Empty<ProjectDiagnostic>();

            string[] lines;
            try
            {
                lines = ReadLinesFromOffset(session.LogPath, session.StartOffset);
            }
            catch { return Array.Empty<ProjectDiagnostic>(); }
            return FindProjectIssues(project, lines, session.LogPath, lineOffset: 0, existingConsoleLog: false);
        }

        private static IReadOnlyList<ProjectDiagnostic> FindProjectIssues(
            ModProject project, string[] lines, string logPath, int lineOffset, bool existingConsoleLog)
        {
            var terms = BuildProjectTerms(project);

            var findings = new Dictionary<string, (string Message, int LastLine, int Count)>(StringComparer.Ordinal);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var mentionsProject = terms.Any(term => line.Contains(term, StringComparison.OrdinalIgnoreCase));
                var currentModLoaderError = !existingConsoleLog && Regex.IsMatch(line, @"\bERROR\s*:\s*Mod\b", RegexOptions.IgnoreCase);
                if (!LooksLikeProblem(line) || (!mentionsProject && !currentModLoaderError))
                    continue;

                var message = TrimLogPrefix(line);
                if (findings.TryGetValue(message, out var existing))
                    findings[message] = (message, i, existing.Count + 1);
                else if (findings.Count < MaxFindings)
                    findings.Add(message, (message, i, 1));
            }

            return findings.Values.Select(finding =>
                new ProjectDiagnostic
                {
                    // Runtime errors should inform the author, not prevent a new deployment.
                    Severity = DiagnosticSeverity.Warning,
                    Code = "PZL001",
                    Target = existingConsoleLog ? "Previous game log" : "Current playtest",
                    Message = FormatFindingMessage(finding.Message, finding.Count, existingConsoleLog),
                    Recommendation = existingConsoleLog
                        ? "This came from the existing console.txt and may predate the current PZTools session. Open the game log for context, then reproduce it in a new playtest before treating it as current."
                        : "This came from the current PZTools playtest. Open the isolated game console log for context, then inspect the project or dependency named by the error.",
                    FilePath = logPath,
                    Line = lineOffset > 0 ? lineOffset + finding.LastLine + 1 : null
                })
                .ToList();
        }

        private static string FormatFindingMessage(string message, int count, bool existingConsoleLog)
        {
            var prefix = existingConsoleLog ? "Previous game log: " : "";
            var repetitions = count > 1 ? $" (repeated {count} times)" : "";
            return prefix + message + repetitions;
        }

        private static HashSet<string> BuildProjectTerms(ModProject project)
        {
            var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { project.Name, project.ModInfo.Id };
            foreach (var target in project.Targets)
            {
                if (!Directory.Exists(target.Path))
                    continue;
                try
                {
                    foreach (var lua in Directory.EnumerateFiles(target.Path, "*.lua", SearchOption.AllDirectories))
                        terms.Add(Path.GetFileName(lua));
                }
                catch { }
            }
            terms.RemoveWhere(string.IsNullOrWhiteSpace);
            return terms;
        }

        private static string[] ReadTailLines(string path, int maxLines)
        {
            // console.txt can be large; retain only the bounded tail while streaming.
            var queue = new Queue<string>(maxLines);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
            {
                if (queue.Count == maxLines)
                    queue.Dequeue();
                queue.Enqueue(line);
            }
            return queue.ToArray();
        }

        private static string[] ReadLinesFromOffset(string path, long requestedOffset)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            stream.Seek(requestedOffset <= stream.Length ? requestedOffset : 0, SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            var lines = new List<string>();
            while (lines.Count < MaxLines && reader.ReadLine() is { } line)
                lines.Add(line);
            return lines.ToArray();
        }

        private static bool LooksLikeProblem(string line)
            => Regex.IsMatch(line, @"\b(?:ERROR|WARN)\b", RegexOptions.IgnoreCase) ||
               line.Contains("Exception", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("attempted index", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("stack traceback", StringComparison.OrdinalIgnoreCase);

        private static string TrimLogPrefix(string line)
        {
            var value = line.Trim();
            return value.Length <= 500 ? value : value[..500] + "...";
        }
    }
}
