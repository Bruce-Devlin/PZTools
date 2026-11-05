using System.IO;
using Newtonsoft.Json;
using PZTools.Core.Functions.Tester;
using PZTools.Core.Models;

namespace PZTools.Core.Functions.Projects;

public static class SessionLifecycleService
{
    internal const string ModId = "PZToolsLifecycle";
    internal static void Install(PlaytestProfile profile, PlaytestSessionWorkspace workspace, RunSession session)
    {
        var expected = session.RestartOf is null ? "" : SessionRegistry.Default.Get(session.RestartOf).PersistenceToken ?? "";
        foreach (var cache in workspace.ClientCachePaths.Append(workspace.ServerCachePath))
        {
            var root = Path.Combine(cache, "mods", ModId);
            var ownership = Path.Combine(root, ".pztools-lifecycle-owned");
            if (Directory.Exists(root) && !File.Exists(ownership)) throw new IOException("Reserved lifecycle companion folder already exists: " + root);
            var mod = profile.Build >= 42 ? Path.Combine(root, "42") : root;
            if (profile.Build >= 42) Directory.CreateDirectory(Path.Combine(root, "common"));
            var lua = Path.Combine(mod, "media", "lua", "shared"); Directory.CreateDirectory(lua);
            File.WriteAllText(ownership, session.Id);
            File.WriteAllText(Path.Combine(mod, "mod.info"), "name=PZTools Session Lifecycle\nid=" + ModId + "\n");
            File.WriteAllText(Path.Combine(lua, "PZToolsLifecycle.lua"), ModTestService.Resource("Lifecycle.lua"));
            var save = PlaytestSetupService.DiscoverSaves(cache).FirstOrDefault()?.Path;
            var state = Path.Combine(cache, "Lua"); Directory.CreateDirectory(state);
            foreach (var stale in new[] { "pztools-lifecycle-command.txt", "pztools-lifecycle-status.txt" })
                if (File.Exists(Path.Combine(state, stale))) File.Delete(Path.Combine(state, stale));
            File.WriteAllLines(Path.Combine(state, "pztools-lifecycle.txt"), new[] { session.Id, expected,
                save is null ? "" : new DirectoryInfo(Path.GetDirectoryName(save)!).Name, save is null ? "" : Path.GetFileName(save) });
            var id = profile.Build >= 42 ? "\\" + ModId : ModId;
            var lists = Directory.Exists(Path.Combine(cache, "Saves")) ? Directory.EnumerateFiles(Path.Combine(cache, "Saves"), "mods.txt", SearchOption.AllDirectories) : Array.Empty<string>();
            foreach (var list in lists.Append(Path.Combine(cache, "mods", "default.txt")).Where(File.Exists))
            {
                var text = File.ReadAllText(list); var brace = text.IndexOf('{');
                if (brace < 0) throw new InvalidDataException("Invalid mod list: " + list);
                File.WriteAllText(list, text.Insert(brace + 1, "\n    mod = " + id + ","));
            }
            var ini = Path.Combine(cache, "Server", profile.ServerName + ".ini");
            if (File.Exists(ini)) File.WriteAllLines(ini, File.ReadAllLines(ini).Select(l => l.StartsWith("Mods=") ? l + ";" + id : l).ToArray());
        }
    }
    internal static string? Receipt(string cache, string session)
    {
        var path = Path.Combine(cache, "Lua", "pztools-lifecycle-status.txt");
        if (!File.Exists(path)) return null;
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(input);
        return reader.ReadLine() == session ? reader.ReadLine() : null;
    }
    internal static void Request(RunSession session)
    {
        session.PersistenceToken = Guid.NewGuid().ToString("N"); session.SaveEvidence = "save requested; completion not yet observed";
        SessionRegistry.Default.Save(session);
        foreach (var process in session.Processes)
        {
            var path = Path.Combine(process.CachePath, "Lua", "pztools-lifecycle-command.txt");
            File.WriteAllLines(path + ".tmp", new[] { session.Id, session.PersistenceToken });
            File.Move(path + ".tmp", path, true);
        }
    }
    public static PlaytestProfile RestartProfile(RunSession session)
    {
        if (session.Profile is null) throw new InvalidOperationException("This launch has no isolated profile to restart.");
        if (session.Processes.Any(p => SessionRegistry.IsAlive(p.Id, p.StartedUtc))) throw new InvalidOperationException("The session still has running processes.");
        if (session.Shutdown != "clean shutdown" || !session.SaveEvidence.StartsWith("completed", StringComparison.Ordinal))
            throw new InvalidOperationException("A verified save and clean shutdown are required for persistence verification. Retained crash data remains available for manual recovery.");
        var profile = JsonConvert.DeserializeObject<PlaytestProfile>(JsonConvert.SerializeObject(session.Profile))!;
        profile.Id = Guid.NewGuid(); profile.KeepSessionData = true;
        if (profile.Mode == PlaytestMode.SinglePlayer)
        {
            profile.SourceSavePath = PlaytestSetupService.DiscoverSaves(Path.Combine(session.WorkspacePath, "client-1")).Single().Path;
            profile.SaveMode = PlaytestSaveMode.FreshClone;
        }
        else
        {
            profile.SourceSessionPath = session.WorkspacePath; profile.SaveMode = PlaytestSaveMode.FreshSessionClone;
        }
        return profile;
    }
}
