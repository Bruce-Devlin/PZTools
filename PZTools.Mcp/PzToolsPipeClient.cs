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
        var id = Guid.NewGuid().ToString("N");
        await pipe.WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            id,
            method,
            arguments
        }, JsonOptions) + "\n"), cancellationToken);
        var line = await reader.ReadLineAsync(cancellationToken)
            ?? throw new IOException("PZTools closed the Agent MCP connection without a response.");
        PipeResponse? response;
        try
        {
            // Web options deserialize names case-insensitively, including replies from
            // older editors that wrote Id/Result/Error using Newtonsoft's defaults.
            response = JsonSerializer.Deserialize<PipeResponse>(line, JsonOptions);
        }
        catch (JsonException)
        {
            return Error("PZTools returned an invalid Agent MCP response.");
        }
        if (response == null)
            return Error("PZTools returned an empty Agent MCP response.");
        if (response.Id != null && response.Id != id)
            return Error("PZTools returned an Agent MCP response for a different request.");
        if (response.Error != null)
            return Error(response.Error);
        if (response.Result.ValueKind == JsonValueKind.Undefined)
            return Error("PZTools returned an Agent MCP response without a result or error.");
        return response.Result.GetRawText();
    }

    private sealed class PipeResponse
    {
        public string? Id { get; init; }
        public JsonElement Result { get; init; }
        public string? Error { get; init; }
    }

    private static string Error(string message)
        => JsonSerializer.Serialize(new
        {
            ok = false,
            error = message
        }, JsonOptions);
}
