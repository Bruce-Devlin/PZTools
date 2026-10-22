using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using PZTools.Core.Functions.Agent;
using PZTools.Mcp;

namespace PZTools.Testing.Tests;

public sealed class AgentMcpIntegrationTests
{
    [Fact]
    public async Task StdioToolsReturnEditorResultsAndErrorsThroughThePipe()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var ct = timeout.Token;
        var pipeName = "PZTools.Agent.StdioTest." + Guid.NewGuid().ToString("N");
        await using var lifetime = new NamedPipeServerStream(pipeName + ".Lifetime", PipeDirection.Out, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var lifetimeConnection = lifetime.WaitForConnectionAsync(ct);
        var calls = new List<string>();
        async Task ServeCallsAsync()
        {
            for (var i = 0; i < 8; i++)
            {
                await using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(ct);
                await AgentMcpEditorServer.HandleClientAsync(server, (request, _) =>
                {
                    calls.Add(request.Method);
                    if (request.Method == "start_playtest")
                    {
                        Assert.Equal("Local test", request.Arguments!.Value<string>("profile"));
                        var unattended = request.Arguments.Value<bool>("unattended");
                        Assert.Equal(unattended ? 600 : 45, request.Arguments.Value<int>("timeoutSeconds"));
                        return Task.FromResult<object?>(new { runId = "test-run", unattended });
                    }
                    return Task.FromResult<object?>(request.Method switch
                    {
                        "capabilities" => new { editorConnected = true, project = "Stdio test" },
                        "playtest_profiles" => new[] { new { profile = new { Name = "Local test" } } },
                        "run_status" => new { runId = "test-run", state = "running", ready = true },
                        "cancel_run" => new { runId = "test-run", state = "cancelling", ready = false },
                        "read_file" => throw new UnauthorizedAccessException("Editor rejected this request."),
                        _ => throw new InvalidOperationException("Unexpected method: " + request.Method)
                    });
                }, ct);
            }
        }
        var serving = ServeCallsAsync();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        // The WPF test output resolves some companion dependencies from WindowsDesktop.
        // Use this output's runtime/dependency manifests when launching its copied entry point.
        var testAssembly = typeof(AgentMcpIntegrationTests).Assembly.Location;
        foreach (var argument in new[] { "exec",
            "--runtimeconfig", Path.ChangeExtension(testAssembly, ".runtimeconfig.json"),
            "--depsfile", Path.ChangeExtension(testAssembly, ".deps.json"),
            typeof(PzToolsPipeClient).Assembly.Location,
            "--pipe", pipeName, "--project-root", Path.GetTempPath() })
            process.StartInfo.ArgumentList.Add(argument);
        Assert.True(process.Start());
        var stderr = process.StandardError.ReadToEndAsync();
        var nextId = 0;

        async Task<JsonElement> RequestAsync(string method, object parameters)
        {
            var id = ++nextId;
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new
                { jsonrpc = "2.0", id, method, @params = parameters }).AsMemory(), ct);
            await process.StandardInput.FlushAsync(ct);
            while (true)
            {
                var line = await process.StandardOutput.ReadLineAsync(ct);
                if (line == null)
                    throw new InvalidOperationException("The MCP companion closed stdout: " + await stderr.WaitAsync(ct));
                using var message = JsonDocument.Parse(line);
                if (!message.RootElement.TryGetProperty("id", out var replyId) || replyId.GetInt32() != id)
                    continue;
                Assert.False(message.RootElement.TryGetProperty("error", out _), line);
                return message.RootElement.GetProperty("result").Clone();
            }
        }

        async Task<JsonElement> CallAsync(string name, object? arguments = null)
        {
            var result = await RequestAsync("tools/call", new { name, arguments = arguments ?? new { } });
            Assert.False(result.TryGetProperty("isError", out var error) && error.GetBoolean(), result.GetRawText());
            var content = Assert.Single(result.GetProperty("content").EnumerateArray());
            Assert.Equal("text", content.GetProperty("type").GetString());
            using var json = JsonDocument.Parse(content.GetProperty("text").GetString()!);
            return json.RootElement.Clone();
        }

        try
        {
            await RequestAsync("initialize", new
            {
                protocolVersion = "2025-03-26", capabilities = new { },
                clientInfo = new { name = "PZTools regression test", version = "1.0" }
            });
            await process.StandardInput.WriteLineAsync(
                "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}".AsMemory(), ct);
            await process.StandardInput.FlushAsync(ct);
            await lifetimeConnection;
            var tools = await RequestAsync("tools/list", new { });
            Assert.Contains(tools.GetProperty("tools").EnumerateArray(),
                tool => tool.GetProperty("name").GetString() == "pztools_capabilities");
            Assert.True((await CallAsync("pztools_capabilities")).GetProperty("editorConnected").GetBoolean());
            var profiles = await CallAsync("pztools_get_playtest_profiles");
            Assert.Equal("Local test", profiles[0].GetProperty("profile").GetProperty("Name").GetString());
            var rejected = await CallAsync("pztools_read_file", new { path = "missing.txt" });
            Assert.False(rejected.GetProperty("ok").GetBoolean());
            Assert.Equal("Editor rejected this request.", rejected.GetProperty("error").GetString());
            Assert.True((await CallAsync("pztools_capabilities")).GetProperty("editorConnected").GetBoolean());
            var automatic = await CallAsync("pztools_start_playtest", new { profile = "Local test" });
            Assert.True(automatic.GetProperty("unattended").GetBoolean());
            var manual = await CallAsync("pztools_start_playtest", new { profile = "Local test", unattended = false, timeoutSeconds = 45 });
            Assert.False(manual.GetProperty("unattended").GetBoolean());
            Assert.True((await CallAsync("pztools_get_run_status", new { runId = "test-run" })).GetProperty("ready").GetBoolean());
            Assert.False((await CallAsync("pztools_cancel_run", new { runId = "test-run" })).GetProperty("ready").GetBoolean());
            await serving;
            Assert.Equal(new[] { "capabilities", "playtest_profiles", "read_file", "capabilities", "start_playtest", "start_playtest", "run_status", "cancel_run" }, calls);
        }
        finally
        {
            timeout.Cancel();
            process.StandardInput.Close();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (TimeoutException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            await stderr;
            try { await serving; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            try { await lifetimeConnection; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        }
    }
}
