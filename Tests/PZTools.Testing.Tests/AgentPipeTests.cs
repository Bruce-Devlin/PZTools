using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Newtonsoft.Json.Linq;
using PZTools.Core.Functions.Agent;
using PZTools.Mcp;

namespace PZTools.Testing.Tests;

public sealed class AgentPipeTests
{
    [Fact]
    public async Task ReturnsCapabilitiesAndProfilesAcrossSeparateConnections()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var pipeName = NewPipeName();
        var client = CreateClient(pipeName);
        var calls = new List<string>();
        foreach (var method in new[] { "capabilities", "playtest_profiles", "capabilities" })
        {
            await using var server = CreateServer(pipeName);
            var serving = ServeAsync(server, (request, _) =>
            {
                calls.Add(request.Method);
                Assert.Equal("test", request.Arguments?.Value<string>("source"));
                return Task.FromResult<object?>(request.Method == "capabilities"
                    ? new { editorConnected = true, project = "Pipe test" }
                    : new[] { new { profile = new { Name = "Local test", Enabled = true } } });
            }, timeout.Token);

            using var result = JsonDocument.Parse(await client.CallAsync(method, new { source = "test" }, timeout.Token));
            if (method == "capabilities")
                Assert.True(result.RootElement.GetProperty("editorConnected").GetBoolean());
            else
                Assert.Equal("Local test", result.RootElement[0].GetProperty("profile").GetProperty("Name").GetString());
            // The client has closed its one-call pipe. Cleanup must not fail or block the next call.
            await serving;
        }
        Assert.Equal(new[] { "capabilities", "playtest_profiles", "capabilities" }, calls);
    }

    [Fact]
    public async Task ReturnsEditorErrorsInsteadOfNull()
    {
        var result = await CallEditorAsync((_, _) => throw new UnauthorizedAccessException("Testing is disabled."));
        AssertError(result, "Testing is disabled.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EditorUsesLowercaseEnvelopeAndPreservesRequestId(bool fail)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var pipeName = NewPipeName();
        await using var server = CreateServer(pipeName);
        var serving = ServeAsync(server, (_, _) => fail
            ? throw new InvalidOperationException("rejected")
            : Task.FromResult<object?>(new { Passed = false }), timeout.Token);

        string response;
        await using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            await client.ConnectAsync(timeout.Token);
            await WriteLineAsync(client, "{\"id\":\"request-1\",\"method\":\"capabilities\"}", timeout.Token);
            using var reader = new StreamReader(client, Encoding.UTF8, false, 4096, leaveOpen: true);
            response = (await reader.ReadLineAsync(timeout.Token))!;
        }
        await serving;
        using var json = JsonDocument.Parse(response);
        var root = json.RootElement;
        Assert.Equal("request-1", root.GetProperty("id").GetString());
        Assert.False(root.TryGetProperty("Result", out _));
        if (fail)
            Assert.Equal("rejected", root.GetProperty("error").GetString());
        else
            Assert.False(root.GetProperty("result").GetProperty("Passed").GetBoolean());
    }

    [Theory]
    [InlineData("{\"Id\":null,\"Result\":{\"editorConnected\":true},\"Error\":null}")]
    [InlineData("{\"id\":null,\"result\":{\"editorConnected\":true},\"error\":null}")]
    public async Task ClientAcceptsLegacyAndCurrentEditorReplies(string reply)
    {
        using var result = JsonDocument.Parse(await CallWithReplyAsync(reply));
        Assert.True(result.RootElement.GetProperty("editorConnected").GetBoolean());
    }

    [Fact]
    public async Task ClientPreservesLegacyErrorsWithoutRequestId()
    {
        AssertError(await CallWithReplyAsync("{\"Id\":null,\"Result\":null,\"Error\":\"Testing is disabled.\"}"),
            "Testing is disabled.");
    }

    [Theory]
    [InlineData("{}", "without a result or error")]
    [InlineData("{\"Error\":null}", "without a result or error")]
    [InlineData("null", "empty Agent MCP response")]
    [InlineData("[]", "invalid Agent MCP response")]
    [InlineData("not json", "invalid Agent MCP response")]
    [InlineData("{\"result\":{},\"error\":true}", "invalid Agent MCP response")]
    [InlineData("{\"id\":\"wrong-request\",\"result\":{}}", "different request")]
    public async Task InvalidRepliesAreErrorsRatherThanNull(string reply, string message)
    {
        AssertError(await CallWithReplyAsync(reply), message);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    public async Task PreservesExplicitEmptyAndNullResults(string result)
    {
        Assert.Equal(result, await CallWithReplyAsync("{\"result\":" + result + "}"));
    }

    [Fact]
    public async Task DisconnectDuringDispatchDoesNotFailCleanupOrReplayOperation()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var pipeName = NewPipeName();
        await using var server = CreateServer(pipeName);
        var dispatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatchCount = 0;
        var serving = ServeAsync(server, async (_, ct) =>
        {
            dispatchCount++;
            dispatched.SetResult();
            await disconnected.Task.WaitAsync(ct);
            return new { completed = true };
        }, timeout.Token);

        await using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            await client.ConnectAsync(timeout.Token);
            await WriteLineAsync(client, "{\"id\":\"cancelled-call\",\"method\":\"write_file\"}", timeout.Token);
            await dispatched.Task.WaitAsync(timeout.Token);
        }
        disconnected.SetResult();
        await serving;
        Assert.Equal(1, dispatchCount);
    }

    private static async Task<string> CallEditorAsync(
        Func<AgentMcpEditorServer.AgentRequest, CancellationToken, Task<object?>> dispatch)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var pipeName = NewPipeName();
        await using var server = CreateServer(pipeName);
        var serving = ServeAsync(server, dispatch, timeout.Token);
        var result = await CreateClient(pipeName).CallAsync("capabilities", cancellationToken: timeout.Token);
        await serving;
        return result;
    }

    private static async Task<string> CallWithReplyAsync(string reply)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var pipeName = NewPipeName();
        await using var server = CreateServer(pipeName);
        async Task ReplyAsync()
        {
            await server.WaitForConnectionAsync(timeout.Token);
            using var reader = new StreamReader(server, Encoding.UTF8, false, 4096, leaveOpen: true);
            Assert.NotNull(await reader.ReadLineAsync(timeout.Token));
            await WriteLineAsync(server, reply, timeout.Token);
        }
        var serving = ReplyAsync();
        var result = await CreateClient(pipeName).CallAsync("capabilities", cancellationToken: timeout.Token);
        await serving;
        return result;
    }

    private static async Task ServeAsync(NamedPipeServerStream server,
        Func<AgentMcpEditorServer.AgentRequest, CancellationToken, Task<object?>> dispatch, CancellationToken cancellationToken)
    {
        await server.WaitForConnectionAsync(cancellationToken);
        await AgentMcpEditorServer.HandleClientAsync(server, dispatch, cancellationToken);
    }

    private static void AssertError(string result, string message)
    {
        using var json = JsonDocument.Parse(result);
        Assert.False(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.Contains(message, json.RootElement.GetProperty("error").GetString());
    }

    private static string NewPipeName() => "PZTools.Agent.Test." + Guid.NewGuid().ToString("N");

    private static NamedPipeServerStream CreateServer(string pipeName) => new(pipeName, PipeDirection.InOut, 1,
        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private static PzToolsPipeClient CreateClient(string pipeName) => new(new McpLaunchOptions
    {
        PipeName = pipeName,
        ProjectRoot = Path.GetTempPath()
    });

    private static ValueTask WriteLineAsync(Stream stream, string line, CancellationToken cancellationToken)
        => stream.WriteAsync(Encoding.UTF8.GetBytes(line + "\n"), cancellationToken);
}
