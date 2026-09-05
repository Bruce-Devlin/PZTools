using System.IO.Pipes;
using System.Security.Principal;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PZTools.Mcp;

public sealed class PzToolsEditorLifetimeMonitor(
    McpLaunchOptions options,
    IHostApplicationLifetime applicationLifetime,
    ILogger<PzToolsEditorLifetimeMonitor> logger) : BackgroundService
{
    private const int StartupAttempts = 10;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lifetimePipeName = options.PipeName + ".Lifetime";
        for (var attempt = 1; attempt <= StartupAttempts && !stoppingToken.IsCancellationRequested; attempt++)
        {
            await using var pipe = new NamedPipeClientStream(
                ".",
                lifetimePipeName,
                PipeDirection.In,
                PipeOptions.Asynchronous,
                TokenImpersonationLevel.Identification);
            try
            {
                await pipe.ConnectAsync(500, stoppingToken);
                await WaitForEditorShutdownAsync(pipe, stoppingToken);
                return;
            }
            catch (TimeoutException) when (attempt < StartupAttempts)
            {
                await Task.Delay(250, stoppingToken);
            }
            catch (IOException) when (attempt < StartupAttempts)
            {
                await Task.Delay(250, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }

        if (!stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("The PZTools editor lifetime bridge is unavailable; stopping the MCP companion.");
            applicationLifetime.StopApplication();
        }
    }

    private async Task WaitForEditorShutdownAsync(Stream pipe, CancellationToken stoppingToken)
    {
        var buffer = new byte[1];
        try
        {
            while (await pipe.ReadAsync(buffer, stoppingToken) > 0) { }
        }
        catch (IOException)
        {
            // Closing PZTools tears down the lifetime pipe and can surface as either EOF or a broken pipe.
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        if (!stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("PZTools closed; stopping the MCP companion.");
            applicationLifetime.StopApplication();
        }
    }
}
