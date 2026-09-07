using Newtonsoft.Json.Linq;

namespace PZTools.Core.Functions.Agent;

/// <summary>Owns one background operation for an editor bridge, including shutdown and bounded output.</summary>
public sealed class AgentRunTracker : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly Queue<string> output = new();
    private CancellationTokenSource? cancellation;
    private Task? task;
    private string? id;
    private string? kind;
    private string state = "idle";
    private JToken? result;
    private string? error;
    private bool disposed;

    public object Start(string runKind, Func<Action<string>, CancellationToken, Task<object>> operation)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (task is { IsCompleted: false }) throw new InvalidOperationException("An MCP run is already active. Poll or cancel it before starting another.");
            cancellation?.Dispose();
            cancellation = new CancellationTokenSource();
            var token = cancellation.Token;
            id = Guid.NewGuid().ToString("N"); kind = runKind; state = "running";
            result = null; error = null; output.Clear();
            task = Task.Run(async () =>
            {
                try
                {
                    var value = await operation(Append, token);
                    lock (gate) { result = JToken.FromObject(value); state = token.IsCancellationRequested ? "cancelled" : "completed"; }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { lock (gate) state = "cancelled"; }
                catch (Exception ex) { lock (gate) { error = ex.Message; state = "failed"; } }
            });
            return Snapshot();
        }
    }

    public object Snapshot(string? runId = null, int lines = 200)
    {
        lock (gate)
        {
            CheckId(runId);
            return new { runId = id, kind, state, result = result?.DeepClone(), error, output = output.TakeLast(Math.Clamp(lines, 1, 2000)).ToArray() };
        }
    }

    public object Cancel(string runId)
    {
        lock (gate)
        {
            CheckId(runId);
            if (state is "running" or "cancelling") { state = "cancelling"; cancellation!.Cancel(); }
            return Snapshot();
        }
    }

    private void CheckId(string? runId)
    {
        if (runId is not null && runId != id) throw new ArgumentException("Unknown run ID. Only the latest MCP run is retained by this editor.");
    }

    private void Append(string line)
    {
        lock (gate) { output.Enqueue(line); while (output.Count > 2000) output.Dequeue(); }
    }

    public async ValueTask DisposeAsync()
    {
        Task? pending;
        lock (gate) { if (disposed) return; disposed = true; cancellation?.Cancel(); pending = task; }
        if (pending is not null) await pending;
        cancellation?.Dispose();
    }
}
