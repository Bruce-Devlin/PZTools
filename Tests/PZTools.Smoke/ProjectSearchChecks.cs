using PZTools.Core.Functions.Projects;

internal static class ProjectSearchChecks
{
    public static void Run(string testRoot)
    {
        var root = Path.Combine(testRoot, "SearchChecks");
        Directory.CreateDirectory(Path.Combine(root, "42", "media"));
        Directory.CreateDirectory(Path.Combine(root, ".pztools"));
        var file = Path.Combine(root, "42", "media", "Example.lua");
        File.WriteAllText(file, "-- opening\nlocal Needle = true\nreturn needle\n");
        File.WriteAllText(Path.Combine(root, ".pztools", "internal.lua"), "needle");
        File.WriteAllText(Path.Combine(root, "binary.txt"), "needle\0data");
        File.WriteAllText(Path.Combine(root, "large.txt"), new string('x', (int)ProjectSearchService.MaxFileBytes + 1));
        var result = ProjectSearchService.Search(root, "needle");
        Check(result.Matches.Count == 2, "literal matches exclude internal and binary content");
        Check(result.Matches[0].Line == 2 && result.Matches[0].Column == 7, "one-based source coordinates");
        Check(result.FilesSkipped == 2, "large and binary files reported");
        Check(ProjectSearchService.Search(root, "Needle", matchCase: true).Matches.Count == 1, "case-sensitive search");
        Check(ProjectSearchService.Search(root, "example.lua", fileNamesOnly: true).Matches.Single().FullPath == file, "file name search");
        Check(ProjectSearchService.Search(root, "42\\media", fileNamesOnly: true).Matches.Count == 1, "relative path search");
        Check(ProjectSearchService.Search(root, " ").Matches.Count == 0, "empty query");
        File.WriteAllText(Path.Combine(root, "many.txt"), string.Join('\n', Enumerable.Repeat("needle", 600)));
        var capped = ProjectSearchService.Search(root, "needle");
        Check(capped.LimitReached && capped.Matches.Count == ProjectSearchService.MaxResults, "result cap");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            ProjectSearchService.Search(root, "needle", cancellationToken: cancellation.Token);
            throw new InvalidOperationException("Search did not cancel.");
        }
        catch (OperationCanceledException) { Check(true, "search cancellation"); }
        Directory.Delete(root, recursive: true);
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("Search regression: " + name);
        Console.WriteLine("[PASS] " + name);
    }
}
