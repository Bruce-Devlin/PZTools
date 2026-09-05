using System.ComponentModel;
using ModelContextProtocol.Server;

namespace PZTools.Mcp;

[McpServerToolType]
public static class PzToolsTools
{
    [McpServerTool(Name = "pztools_capabilities")]
    [Description("Lists the active PZTools project, live editor connection, and capabilities enabled by the user. Call this first.")]
    public static Task<string> Capabilities(PzToolsPipeClient client, CancellationToken cancellationToken)
        => client.CallAsync("capabilities", cancellationToken: cancellationToken);

    [McpServerTool(Name = "pztools_get_editor_state")]
    [Description("Gets the live PZTools editor state, including the active project, open file, caret, selection, and document length.")]
    public static Task<string> GetEditorState(PzToolsPipeClient client, CancellationToken cancellationToken)
        => client.CallAsync("editor_state", cancellationToken: cancellationToken);

    [McpServerTool(Name = "pztools_list_files")]
    [Description("Lists files and directories below a project-relative directory. Results are capped at 5000 entries.")]
    public static Task<string> ListFiles(
        PzToolsPipeClient client,
        [Description("Project-relative directory. Omit or use an empty string for the project root.")] string path = "",
        [Description("Optional file search pattern such as *.lua. Uses Windows search-pattern syntax.")] string pattern = "*",
        CancellationToken cancellationToken = default)
        => client.CallAsync("list_files", new
        {
            path,
            pattern
        }, cancellationToken);

    [McpServerTool(Name = "pztools_read_file")]
    [Description("Reads a UTF-8 text file using a path relative to the active PZTools project.")]
    public static Task<string> ReadFile(
        PzToolsPipeClient client,
        [Description("Project-relative file path.")] string path,
        CancellationToken cancellationToken = default)
        => client.CallAsync("read_file", new
        {
            path
        }, cancellationToken);

    [McpServerTool(Name = "pztools_open_file")]
    [Description("Opens a project file in the live PZTools editor preview and optionally moves the caret to a line.")]
    public static Task<string> OpenFile(
        PzToolsPipeClient client,
        [Description("Project-relative file path.")] string path,
        [Description("Optional one-based line number.")] int? line = null,
        CancellationToken cancellationToken = default)
        => client.CallAsync("open_file", new
        {
            path,
            line
        }, cancellationToken);

    [McpServerTool(Name = "pztools_write_file")]
    [Description("Creates or replaces a UTF-8 project file, then refreshes the PZTools explorer and live preview.")]
    public static Task<string> WriteFile(
        PzToolsPipeClient client,
        [Description("Project-relative file path. Paths outside the active project are rejected.")] string path,
        [Description("Complete replacement content for the file.")] string content,
        CancellationToken cancellationToken = default)
        => client.CallAsync("write_file", new
        {
            path,
            content
        }, cancellationToken);

    [McpServerTool(Name = "pztools_test_lua")]
    [Description("Runs PZTools Lua syntax validation for one project-relative Lua file and returns structured error location data.")]
    public static Task<string> TestLua(
        PzToolsPipeClient client,
        [Description("Project-relative Lua file path.")] string path,
        CancellationToken cancellationToken = default)
        => client.CallAsync("test_lua", new
        {
            path
        }, cancellationToken);

    [McpServerTool(Name = "pztools_check_project")]
    [Description("Runs the complete PZTools Project Health analysis: Lua, ZedScript, metadata, translations, dependencies, conflicts, deployment, and runtime-log checks.")]
    public static Task<string> CheckProject(PzToolsPipeClient client, CancellationToken cancellationToken)
        => client.CallAsync("check_project", cancellationToken: cancellationToken);

    [McpServerTool(Name = "pztools_deploy_project")]
    [Description("Validates and deploys the active project to the local Project Zomboid Mods folder. Deployment is blocked when Project Health has errors.")]
    public static Task<string> DeployProject(PzToolsPipeClient client, CancellationToken cancellationToken)
        => client.CallAsync("deploy_project", cancellationToken: cancellationToken);

    [McpServerTool(Name = "pztools_start_game")]
    [Description("Starts Project Zomboid for a test/debug session after Project Health passes. Defaults to the -debug launch argument.")]
    public static Task<string> StartGame(
        PzToolsPipeClient client,
        [Description("Optional managed build folder name. Existing-install mode ignores this value.")] string? build = null,
        [Description("Project Zomboid launch arguments. Defaults to -debug.")] string? arguments = null,
        CancellationToken cancellationToken = default)
        => client.CallAsync("start_game", new
        {
            build,
            arguments
        }, cancellationToken);

    [McpServerTool(Name = "pztools_stop_game")]
    [Description("Stops the Project Zomboid process started by PZTools.")]
    public static Task<string> StopGame(PzToolsPipeClient client, CancellationToken cancellationToken)
        => client.CallAsync("stop_game", cancellationToken: cancellationToken);

    [McpServerTool(Name = "pztools_get_game_status")]
    [Description("Gets Project Zomboid start/running state, process ID, game directory, and console-log path.")]
    public static Task<string> GetGameStatus(PzToolsPipeClient client, CancellationToken cancellationToken)
        => client.CallAsync("game_status", cancellationToken: cancellationToken);

    [McpServerTool(Name = "pztools_get_game_log")]
    [Description("Reads the newest lines from the Project Zomboid console log for runtime debugging.")]
    public static Task<string> GetGameLog(
        PzToolsPipeClient client,
        [Description("Number of newest lines to return, from 1 to 2000.")] int lines = 200,
        CancellationToken cancellationToken = default)
        => client.CallAsync("game_log", new
        {
            lines
        }, cancellationToken);
}
