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

    [McpServerTool(Name = "pztools_discover_tests")]
    [Description("Lists .pztests files as project-relative paths. Unit tests run in MoonSharp; game includes game, e2e and server tests.")]
    public static Task<string> DiscoverTests(PzToolsPipeClient client, string kind = "unit", string? filter = null, CancellationToken cancellationToken = default)
        => client.CallAsync("discover_tests", new { kind, filter }, cancellationToken);

    [McpServerTool(Name = "pztools_scaffold_tests")]
    [Description("Creates missing test examples and harness documentation in .pztests, preserving existing files. Requires testing and project writes.")]
    public static Task<string> ScaffoldTests(PzToolsPipeClient client, CancellationToken cancellationToken)
        => client.CallAsync("scaffold_tests", cancellationToken: cancellationToken);

    [McpServerTool(Name = "pztools_get_playtest_profiles")]
    [Description("Reads saved Playtest Lab profiles, IDs, dependencies and validation errors. Use a unique name or ID when starting a run. Configure profiles in Playtest Lab.")]
    public static Task<string> PlaytestProfiles(PzToolsPipeClient client, CancellationToken cancellationToken)
        => client.CallAsync("playtest_profiles", cancellationToken: cancellationToken);

    [McpServerTool(Name = "pztools_start_test_run")]
    [Description("Starts background unit or game tests and returns a run ID immediately. Poll get_run_status for the final structured report and artifact directory. Unit runs write test artifacts; game runs additionally require deployment and game control. Runs use saved files. A completed operation does not imply tests passed: inspect result.Passed and Errors.")]
    public static Task<string> StartTestRun(PzToolsPipeClient client,
        [Description("unit or game.")] string kind = "unit",
        [Description("Optional substring of a path relative to .pztests, e.g. example.unit.lua.")] string? filter = null,
        [Description("Required for game tests: unique saved Playtest Lab profile name or ID.")] string? profile = null,
        [Description("Game-test timeout, 10 to 86400 seconds. Unit tests use the existing per-file timeout.")] int timeoutSeconds = 300,
        CancellationToken cancellationToken = default)
        => client.CallAsync("start_test_run", new { kind, filter, profile, timeoutSeconds }, cancellationToken);

    [McpServerTool(Name = "pztools_start_playtest")]
    [Description("Starts an isolated Playtest Lab session from a saved profile after Project Health passes. Supports single player and dedicated server with multiple clients. Requires testing, deployment and game control. Returns a run ID; poll status for output and final diagnostics, or cancel to stop owned processes.")]
    public static Task<string> StartPlaytest(PzToolsPipeClient client, string profile, CancellationToken cancellationToken = default)
        => client.CallAsync("start_playtest", new { profile }, cancellationToken);

    [McpServerTool(Name = "pztools_get_run_status")]
    [Description("Gets the latest MCP run: running/cancelling/completed/cancelled/failed, bounded recent output, final result and runner error. Only the latest run is retained in memory; test reports persist in .pztools/test-results. Live desktop-initiated runs are not included.")]
    public static Task<string> RunStatus(PzToolsPipeClient client, string? runId = null, int lines = 200, CancellationToken cancellationToken = default)
        => client.CallAsync("run_status", new { runId, lines }, cancellationToken);

    [McpServerTool(Name = "pztools_cancel_run")]
    [Description("Requests cancellation of the specified MCP run and cleanup of its owned processes. Poll until cancellation completes. Available even after launch permissions are revoked while MCP remains enabled.")]
    public static Task<string> CancelRun(PzToolsPipeClient client, string runId, CancellationToken cancellationToken = default)
        => client.CallAsync("cancel_run", new { runId }, cancellationToken);

    [McpServerTool(Name = "pztools_verify_deployment")]
    [Description("Checks the local deployed mod against the project's deployment manifest without changing files. Returns structured missing/stale-file diagnostics.")]
    public static Task<string> VerifyDeployment(PzToolsPipeClient client, CancellationToken cancellationToken)
        => client.CallAsync("verify_deployment", cancellationToken: cancellationToken);
    [McpServerTool(Name = "pztools_get_test_reports")]
    [Description("Lists up to 100 newest saved test reports, including runs from Test Explorer. Supply a report runId to read its complete results. Report IDs differ from background operation IDs returned by start_test_run.")]
    public static Task<string> TestReports(PzToolsPipeClient client, string? runId = null, CancellationToken cancellationToken = default)
        => client.CallAsync("test_reports", new { runId }, cancellationToken);

    [McpServerTool(Name = "pztools_search_project")]
    [Description("Searches saved project text or file names using the editor's bounded literal search. Returns locations and previews, up to 500 matches; skips generated folders, links and oversized files. Requires editor control.")]
    public static Task<string> SearchProject(PzToolsPipeClient client, string query, bool matchCase = false,
        bool fileNamesOnly = false, CancellationToken cancellationToken = default)
        => client.CallAsync("search_project", new { query, matchCase, fileNamesOnly }, cancellationToken);

}
