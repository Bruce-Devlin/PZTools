using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace PZTools.Mcp;

public sealed class PzToolsPipeClient(McpLaunchOptions options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<string> CallAsync(string method, object? arguments = null, CancellationToken cancellationToken = default)
    {
        await using var pipe = new NamedPipeClientStream(
            ".", options.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        try
        {
            await pipe.ConnectAsync(3000, cancellationToken);
        }
        catch (TimeoutException)
        {
            return Error($"PZTools is not available for '{options.ProjectRoot}'. Open this project in PZTools and enable Agent MCP in Settings.");
        }

        using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
        var id = Guid.NewGuid().ToString("N");
        await writer.WriteLineAsync(JsonSerializer.Serialize(new
        {
            id,
            method,
            arguments
        }, JsonOptions));
        var line = await reader.ReadLineAsync(cancellationToken)
            ?? throw new IOException("PZTools closed the Agent MCP connection without a response.");
        using var response = JsonDocument.Parse(line);
        if (response.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
            return Error(error.GetString() ?? "PZTools rejected the request.");
        if (!response.RootElement.TryGetProperty("result", out var result))
            return "null";
        return result.GetRawText();
    }

    private static string Error(string message)
        => JsonSerializer.Serialize(new
        {
            ok = false,
            error = message
        }, JsonOptions);
}
