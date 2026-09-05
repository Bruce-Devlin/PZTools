namespace PZTools.Mcp;

public sealed class McpLaunchOptions
{
    public required string PipeName { get; init; }
    public required string ProjectRoot { get; init; }

    public static McpLaunchOptions Parse(string[] args)
    {
        string? pipe = null;
        string? projectRoot = null;
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] == "--pipe" && index + 1 < args.Length)
                pipe = args[++index];
            else if (args[index] == "--project-root" && index + 1 < args.Length)
                projectRoot = args[++index];
        }

        if (string.IsNullOrWhiteSpace(pipe))
            throw new ArgumentException("PZTools MCP requires --pipe <name>. Use PZTools Settings > Agent MCP to configure Codex.");
        if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot))
            throw new ArgumentException("PZTools MCP requires an existing --project-root <path>.");

        return new McpLaunchOptions { PipeName = pipe, ProjectRoot = Path.GetFullPath(projectRoot) };
    }
}
