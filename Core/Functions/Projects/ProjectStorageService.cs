using System.Diagnostics;
using System.IO;

namespace PZTools.Core.Functions.Projects;

public sealed record ProjectStorageStatus(string Path, bool InBuildOutput, string GitRoot, int Tracked, int Untracked, int Ignored, string Detail);

public static class ProjectStorageService
{
    private static string Setting => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PZTools", "projects-location.txt");
    public static string DefaultRoot => File.Exists(Setting) && Directory.Exists(File.ReadAllText(Setting).Trim()) ? File.ReadAllText(Setting).Trim() :
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PZTools", "Projects");
    public static void RememberRoot(string root)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Setting)!);
        File.WriteAllText(Setting, Path.GetFullPath(root));
    }
    internal static (int Exit, string Output) Git(string root, params string[] args)
        => GitInput(root, null, args);
    private static (int Exit, string Output) GitInput(string root, string? input, params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = input is not null };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("Git could not be started.");
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        if (input is not null) { process.StandardInput.Write(input); process.StandardInput.Close(); }
        if (!process.WaitForExit(10000)) { process.Kill(true); throw new TimeoutException("Git inspection timed out."); }
        return (process.ExitCode, output.GetAwaiter().GetResult() + (process.ExitCode == 0 ? "" : error.GetAwaiter().GetResult()));
    }
    public static ProjectStorageStatus Inspect(string root)
    {
        root = Path.GetFullPath(root);
        var build = root.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(p => p.Equals("bin", StringComparison.OrdinalIgnoreCase) || p.Equals("obj", StringComparison.OrdinalIgnoreCase));
        try
        {
            var repository = Git(root, "rev-parse", "--show-toplevel");
            if (repository.Exit != 0) return new(root, build, "", 0, 0, 0, "Not inside a Git repository. Source is not covered by version control.");
            var sources = Tester.TestEvidenceService.SourceFiles(root).Select(p => Path.GetRelativePath(root, p).Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var tracked = Git(root, "ls-files", "-z", "--", ".").Output.Split('\0', StringSplitOptions.RemoveEmptyEntries).Count(sources.Contains);
            var ignored = sources.Count == 0 ? 0 : GitInput(root, string.Join('\0', sources) + '\0', "check-ignore", "--stdin", "-z").Output.Split('\0', StringSplitOptions.RemoveEmptyEntries).Length;
            var untracked = sources.Count - tracked - ignored;
            return new(root, build, repository.Output.Trim(), tracked, untracked, ignored,
                $"Source coverage: {tracked} tracked; {untracked} untracked; {ignored} ignored. Generated artifacts are excluded. Tracking does not establish a commit or remote backup.");
        }
        catch (Exception ex) { return new(root, build, "", 0, 0, 0, "Git coverage could not be checked: " + ex.Message); }
    }
    public static IReadOnlyList<string> RelocationFiles(string source) => Tester.TestEvidenceService.SourceFiles(source)
        .Select(p => Path.GetRelativePath(source, p)).ToArray();
    public static void CopySource(string source, string destination)
    {
        source = Path.GetFullPath(source); destination = Path.GetFullPath(destination);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Choose a new destination folder. Existing folders will not be overwritten.");
        if (destination.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Choose a destination outside the source project.");
        ProjectDeployer.RejectLinkedPath(destination);
        var directories = Tester.TestEvidenceService.SourceDirectories(source).ToArray();
        var files = RelocationFiles(source);
        Directory.CreateDirectory(destination);
        foreach (var relative in directories) Directory.CreateDirectory(Path.Combine(destination, relative));
        foreach (var relative in files)
        {
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Path.Combine(source, relative), target, false);
            if (Tester.TestEvidenceService.Hash(target) != Tester.TestEvidenceService.Hash(Path.Combine(source, relative)))
                throw new IOException("Copy verification failed; original source retained: " + relative);
        }
    }
}
