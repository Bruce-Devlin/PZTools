using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using PZTools.Mcp;

var options = McpLaunchOptions.Parse(args);
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(console => console.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<PzToolsPipeClient>();
builder.Services.AddHostedService<PzToolsEditorLifetimeMonitor>();
builder.Services
    .AddMcpServer(server =>
    {
        server.ServerInfo = new()
        {
            Name = "PZTools",
            Version = "1.1.0"
        };
        server.ServerInstructions = "Use pztools_capabilities first. This server controls the PZTools project open in the desktop editor. Paths must be relative to that project. Run pztools_check_project before deployment or starting a debug session. Use pztools_start_playtest for unattended fixture startup; poll pztools_get_run_status until ready=true before operating. Game tests handle startup automatically. Test and Playtest Lab starts return background operation IDs: poll status and inspect the final result, or use pztools_cancel_run and wait for cleanup. Completed execution does not imply tests passed. Read saved reports with pztools_get_test_reports. Respect capability errors; the user controls them in PZTools Settings > Agent MCP.";
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
