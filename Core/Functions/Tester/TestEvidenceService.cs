using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using PZTools.Core.Functions.Projects;
using PZTools.Core.Models.Test;

namespace PZTools.Core.Functions.Tester;

public static class TestEvidenceService
{
    public static IEnumerable<string> SourceFiles(string root)
    {
        var payload = ProjectDeployer.EnumeratePayloadFiles(root);
        var tests = Path.Combine(root, ".pztests");
        if (Directory.Exists(tests)) ProjectDeployer.RejectLinkedPath(tests);
        return payload.Concat(Directory.Exists(tests) ? Directory.EnumerateFiles(tests, "*", new EnumerationOptions
        { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false }) : Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
    }
    public static string Hash(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    public static IEnumerable<string> SourceDirectories(string root)
    {
        var directories = new Stack<string>(); directories.Push(root);
        while (directories.TryPop(out var parent))
            foreach (var child in Directory.EnumerateDirectories(parent))
            {
                var relative = Path.GetRelativePath(root, child);
                if (!ProjectDeployer.ShouldIncludePath(relative) && !relative.Equals(".pztests", StringComparison.OrdinalIgnoreCase) &&
                    !relative.StartsWith(".pztests" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                ProjectDeployer.RejectLinkedPath(child);
                yield return relative;
                directories.Push(child);
            }
    }
    public static void CaptureSource(ModTestReport report)
    {
        report.SourceDirectory = Path.Combine(report.ArtifactDirectory, "source");
        report.SourceSnapshotId = Guid.NewGuid().ToString("N");
        foreach (var relative in SourceDirectories(report.ProjectPath)) Directory.CreateDirectory(Path.Combine(report.SourceDirectory, relative));
        foreach (var file in SourceFiles(report.ProjectPath))
        {
            var relative = Path.GetRelativePath(report.ProjectPath, file);
            var target = Path.Combine(report.SourceDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using (var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = File.Create(target)) input.CopyTo(output);
            report.SourceHashes[relative.Replace('\\', '/')] = Hash(target);
        }
    }
    public static IReadOnlyList<string> ChangedFiles(ModTestReport report)
    {
        if (report.SourceHashes.Count == 0 || !Directory.Exists(report.ProjectPath)) return new[] { "Source comparison unavailable for this report." };
        var current = SourceFiles(report.ProjectPath).ToDictionary(p => Path.GetRelativePath(report.ProjectPath, p).Replace('\\', '/'), Hash, StringComparer.OrdinalIgnoreCase);
        return report.SourceHashes.Keys.Union(current.Keys, StringComparer.OrdinalIgnoreCase)
            .Where(p => !current.TryGetValue(p, out var hash) || !report.SourceHashes.TryGetValue(p, out var original) || original != hash)
            .OrderBy(p => p).ToArray();
    }
    public static string Summary(ModTestReport report) =>
        $"Operation: {report.OperationState} | Test verdict: {report.Verdict} | Runner errors: {report.Errors.Count}\n" +
        $"Session: {report.SessionId} | Operation ID: {report.OperationId} | Report: {report.RunId}\n" +
        $"Profile: {report.Profile} | Installation: {report.GameRoot}\nSnapshot: {report.SourceSnapshotId}\n" +
        $"Captured source: {report.SourceDirectory}\nPayload manifest: {report.PayloadManifestPath}\n" +
        "Conditions: " + string.Join("; ", report.Conditions) + "\n" +
        "Scope: only the listed cases and endpoints were exercised. A passed case does not establish save/restart, multiplayer or other unlisted behavior.\n" +
        "Changed since this run:\n" + string.Join("\n", ChangedFiles(report)) + "\nRunner errors:\n" + string.Join("\n", report.Errors) +
        (report.StartupDiagnostics.Count == 0 ? "" : "\nEndpoint startup diagnostics:\n" + JsonConvert.SerializeObject(report.StartupDiagnostics, Formatting.Indented));
    public static void AttachDiagnostic(ModTestReport report, ModTestCase test, string? cache = null, Process? process = null)
    {
        if (test.Diagnostic is not null) return;
        var log = cache is null ? Path.Combine(report.ArtifactDirectory, "unit.log") :
            Path.Combine(cache, test.Endpoint == "server" ? "server-console.txt" : "console.txt");
        if (!File.Exists(log) && cache is not null) log = Path.Combine(cache, "console.txt");
        var diagnostic = new CaseDiagnostic { Assertion = test.Message, Phase = test.LastPhase, LogPath = log,
            Endpoint = report.Endpoints.FirstOrDefault(e => e.Endpoint == test.Endpoint) is { } state ?
                JsonConvert.DeserializeObject<GameTestEndpointState>(JsonConvert.SerializeObject(state)) : null };
        try
        {
            if (File.Exists(log))
            {
                using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                stream.Position = Math.Max(0, stream.Length - 32000);
                using var reader = new StreamReader(stream);
                diagnostic.LogExcerpt = reader.ReadToEnd();
            }
            if (process is not null) FailureWindowCapture.Capture(process, report.ArtifactDirectory, diagnostic);
        }
        catch (Exception ex) { diagnostic.CaptureNote = "Diagnostic capture: " + ex.Message; }
        test.Diagnostic = diagnostic;
    }
    public sealed record LogChunk(string SessionId, string Endpoint, int ProcessId, string Path, long NextOffset, bool Reset, string Text);
    public static LogChunk ReadLog(string project, string sessionId, string endpoint, long offset = 0, int bytes = 32768)
    {
        var session = SessionRegistry.Default.Get(sessionId);
        if (!session.ProjectPath.Equals(Path.GetFullPath(project), StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("Session belongs to another project.");
        var process = session.Processes.LastOrDefault(p => p.Endpoint == endpoint) ?? throw new ArgumentException("Unknown endpoint: " + endpoint);
        var path = process.LogPath;
        if (!File.Exists(path)) path = Path.Combine(process.CachePath, "console.txt");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var reset = offset < 0 || offset > stream.Length;
        stream.Position = reset ? 0 : offset;
        var buffer = new byte[Math.Clamp(bytes, 4, 262144)];
        var count = stream.Read(buffer);
        // Do not split UTF-8 continuation bytes across chunks.
        if (stream.Position < stream.Length && count > 0)
        {
            var end = count - 1;
            while (end >= 0 && (buffer[end] & 0xc0) == 0x80) end--;
            var required = end < 0 ? 0 : buffer[end] >= 0xf0 ? 4 : buffer[end] >= 0xe0 ? 3 : buffer[end] >= 0xc0 ? 2 : 1;
            if (end >= 0 && count - end < required) { stream.Position -= count - end; count = end; }
        }
        return new(session.Id, endpoint, process.Id, path, stream.Position, reset, Encoding.UTF8.GetString(buffer, 0, count));
    }
}
