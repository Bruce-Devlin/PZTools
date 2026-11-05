using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using PZTools.Core.Functions.Agent;
using PZTools.Core.Models;

namespace PZTools.Core.Functions.Projects;

/// <summary>Durable ownership receipts. Recovery observes processes; it never replays a launch or deletes a world.</summary>
public sealed class SessionRegistry
{
    public static SessionRegistry Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PZTools", "sessions"));
    private readonly string root;
    private readonly string mutexName;
    public SessionRegistry(string root)
    {
        this.root = Path.GetFullPath(root);
        mutexName = "Local\\PZToolsSessions-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(this.root.ToUpperInvariant())))[..24];
    }
    private T Locked<T>(Func<T> action)
    {
        using var mutex = new Mutex(false, mutexName);
        try { if (!mutex.WaitOne(TimeSpan.FromSeconds(10))) throw new IOException("Session registry is busy. Retry after the other launch finishes reserving its session."); }
        catch (AbandonedMutexException) { }
        try { Directory.CreateDirectory(root); return action(); }
        finally { mutex.ReleaseMutex(); }
    }
    public static bool IsAlive(int id, DateTime started)
    {
        try { using var p = Process.GetProcessById(id); return !p.HasExited && p.StartTime.ToUniversalTime() == started; }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        // Access denied is uncertainty, not permission to replace an owned session.
        catch (System.ComponentModel.Win32Exception) { return true; }
    }
    private List<RunSession> ReadAll()
    {
        var sessions = new List<RunSession>();
        foreach (var path in Directory.EnumerateFiles(root, "*.json"))
        {
            RunSession session;
            try { session = JsonConvert.DeserializeObject<RunSession>(File.ReadAllText(path)) ?? throw new InvalidDataException("Empty receipt"); }
            catch (Exception ex) { throw new IOException($"Cannot read session ownership receipt '{path}'. Preserve it and repair it before launching: {ex.Message}", ex); }
            if (session.State is "preparing" or "running" && !IsAlive(session.OwnerId, session.OwnerStartedUtc))
            {
                session.State = "interrupted"; session.Retention = "retained";
                session.Error = "Editor exited before recording completion. Artifacts retained; no actions repeated.";
                SaveCore(session);
            }
            sessions.Add(session);
        }
        return sessions;
    }
    private static bool Competes(RunSession s) => s.OwnsGame &&
        ((s.State is "preparing" or "running" && IsAlive(s.OwnerId, s.OwnerStartedUtc)) ||
         s.Processes.Any(p => IsAlive(p.Id, p.StartedUtc)));
    private void AssertAvailableCore()
    {
        var active = ReadAll().FirstOrDefault(Competes);
        if (active is not null) throw new InvalidOperationException(
            $"Session {active.Id} ({active.Kind}, {active.Profile?.Name}, {active.State}) still owns game processes or is preparing. " +
            $"Open Test Explorer > Run history to inspect it. Its world is retained at '{active.WorkspacePath}'. Stop that session before launching another.");
    }
    public void AssertAvailable() => Locked(() => { AssertAvailableCore(); return true; });
    public void ClaimGame(RunSession session) => Locked(() =>
    {
        AssertAvailableCore(); session.OwnsGame = true; SaveCore(session); return true;
    });
    public RunSession Begin(string project, string kind, bool ownsGame, PlaytestProfile? profile = null, string? reportId = null, string? artifacts = null)
        => Locked(() =>
        {
            if (ownsGame) AssertAvailableCore();
            using var owner = Process.GetCurrentProcess();
            var session = new RunSession { ProjectPath = Path.GetFullPath(project), Kind = kind, OwnsGame = ownsGame,
                OperationId = AgentRunTracker.CurrentOperationId.Value, ReportId = reportId, ArtifactDirectory = artifacts ?? "",
                Profile = profile is null ? null : JsonConvert.DeserializeObject<PlaytestProfile>(JsonConvert.SerializeObject(profile)),
                OwnerId = owner.Id, OwnerStartedUtc = owner.StartTime.ToUniversalTime() };
            SaveCore(session); return session;
        });
    public IReadOnlyList<RunSession> List(string? project = null) => Locked(() => ReadAll()
        .Where(s => project is null || s.ProjectPath.Equals(Path.GetFullPath(project), StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(s => s.StartedUtc).ToArray());
    public RunSession Get(string id) => List().FirstOrDefault(s => s.Id == id || s.OperationId == id || s.ReportId == id)
        ?? throw new ArgumentException("Unknown session, operation or report ID: " + id);
    public void Save(RunSession session) => Locked(() => { SaveCore(session); return true; });
    private void SaveCore(RunSession session)
    {
        if (!Guid.TryParseExact(session.Id, "N", out _)) throw new InvalidDataException("Invalid session ID.");
        var path = Path.Combine(root, session.Id + ".json");
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonConvert.SerializeObject(session, Formatting.Indented));
        File.Move(temporary, path, true);
    }
    public void AddProcess(RunSession session, Process process, string endpoint, string cache)
    {
        string executable;
        try { executable = process.StartInfo.FileName; }
        catch (InvalidOperationException) { executable = process.MainModule?.FileName ?? "Unknown attached process executable"; }
        session.Processes.Add(new SessionProcess { Id = process.Id, StartedUtc = process.StartTime.ToUniversalTime(), Endpoint = endpoint,
            Executable = executable, CachePath = cache,
            LogPath = Path.Combine(cache, endpoint == "server" ? "server-console.txt" : "console.txt") });
        session.State = "running"; Save(session);
    }
    public void Finish(RunSession session, string state, string? error = null)
    {
        session.State = state; session.Error = error; session.EndedUtc = DateTime.UtcNow;
        if (state != "completed") session.Retention = "retained";
        Save(session);
    }
}
