using System.IO;

namespace PZTools.Core.Functions.Projects;

public sealed record ProjectSearchMatch(string FullPath, string RelativePath, int Line, int Column, string Preview);
public sealed record ProjectSearchResult(IReadOnlyList<ProjectSearchMatch> Matches, int FilesSearched, int FilesSkipped, bool LimitReached);

/// <summary>Bounded, cancellable literal search over author-owned text files.</summary>
public static class ProjectSearchService
{
    public const long MaxFileBytes = 2 * 1024 * 1024;
    public const int MaxResults = 500;
    private static readonly HashSet<string> ExcludedDirectories = new(
        [".git", ".vs", ".vscode", ".codex", ".pztools", "bin", "obj", "Workshop", "node_modules"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> TextExtensions = new(
        [".lua", ".txt", ".info", ".json", ".xml", ".ini", ".cfg", ".md", ".csv", ".properties", ".yml", ".yaml"], StringComparer.OrdinalIgnoreCase);

    public static ProjectSearchResult Search(string rootPath, string query, bool matchCase = false,
        bool fileNamesOnly = false, CancellationToken cancellationToken = default)
    {
        var matches = new List<ProjectSearchMatch>();
        if (string.IsNullOrWhiteSpace(query)) return new(matches, 0, 0, false);
        var root = Path.GetFullPath(rootPath);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("The project folder no longer exists.");
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var pending = new Stack<string>();
        pending.Push(root);
        var buffer = fileNamesOnly ? [] : new char[(int)MaxFileBytes + 1];
        int searched = 0, skipped = 0;
        while (pending.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] entries;
            try { entries = Directory.GetFileSystemEntries(directory); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; continue; }
            Array.Sort(entries, StringComparer.OrdinalIgnoreCase);
            foreach (var path in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) { skipped++; continue; }
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        if (!ExcludedDirectories.Contains(Path.GetFileName(path))) pending.Push(path);
                        continue;
                    }
                    var relative = Path.GetRelativePath(root, path);
                    if (fileNamesOnly)
                    {
                        searched++;
                        if (relative.Contains(query, comparison)) matches.Add(new(path, relative, 0, 0, "File name match"));
                    }
                    else
                    {
                        if (!TextExtensions.Contains(Path.GetExtension(path))) continue;
                        if (new FileInfo(path).Length > MaxFileBytes) { skipped++; continue; }
                        // Bound reads even if a file grows between the length check and read.
                        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
                        var length = reader.ReadBlock(buffer, 0, buffer.Length);
                        cancellationToken.ThrowIfCancellationRequested();
                        if (length > MaxFileBytes || Array.IndexOf(buffer, '\0', 0, length) >= 0) { skipped++; continue; }
                        searched++;
                        using var lines = new StringReader(new string(buffer, 0, length));
                        int lineNumber = 0;
                        while (lines.ReadLine() is { } line)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            lineNumber++;
                            var column = line.IndexOf(query, comparison);
                            if (column < 0) continue;
                            var start = Math.Max(0, column - 70);
                            var preview = (start > 0 ? "…" : "") + line.Substring(start, Math.Min(240, line.Length - start)).Trim();
                            matches.Add(new(path, relative, lineNumber, column + 1, preview));
                            if (matches.Count >= MaxResults) return new(matches, searched, skipped, true);
                        }
                    }
                    if (matches.Count >= MaxResults) return new(matches, searched, skipped, true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; }
            }
        }
        return new(matches, searched, skipped, false);
    }
}
