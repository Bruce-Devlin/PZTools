using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using PZTools.Core.Functions.Projects;
using PZTools.Core.Models;
using PZTools.Core.Models.Test;

namespace PZTools.Core.Functions.Tester;

public static class GameTestService
{
    private const string CompanionId = "PZToolsTesting";
    public static async Task<ModTestReport> RunAsync(ModProject project, PlaytestProfile selected, string gameRoot,
        string? filter = null, int timeoutSeconds = 300, Action<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var report = ModTestService.CreateReport(project, "game");
        report.GameRoot = gameRoot; report.Profile = selected.Name;
        PlaytestSessionWorkspace? workspace = null;
        try
        {
            if (timeoutSeconds is < 10 or > 86400) throw new ArgumentException("Run timeout must be between 10 and 86400 seconds.");
            var files = ModTestService.Discover(project, "game", filter);
            if (files.Count == 0) throw new InvalidOperationException("No game tests found. Add .pztests/*.game.lua, *.e2e.lua, or *.server.lua.");
            var profile = JsonConvert.DeserializeObject<PlaytestProfile>(JsonConvert.SerializeObject(selected))!;
            if (profile.SaveMode == PlaytestSaveMode.ReuseProfileData) throw new InvalidOperationException("Automated tests require a fresh workspace. Choose FreshClone or Empty in the playtest profile.");
            if (profile.Mode == PlaytestMode.SinglePlayer && profile.SaveMode != PlaytestSaveMode.FreshClone)
                throw new InvalidOperationException("Unattended single-player tests require a FreshClone profile with a saved, living character. Configure a source save in Playtest Lab.");
            if (profile.Mode == PlaytestMode.SinglePlayer && files.Any(f => f.EndsWith(".server.lua", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Server tests require a dedicated-server profile.");
            profile.KeepSessionData = true;
            var health = await ProjectHealthService.AnalyzeAsync(project, true, cancellationToken);
            if (!health.IsReadyToDeploy) throw new InvalidOperationException("Project health errors block game tests. Open Health Dashboard.");
            var validation = PlaytestProfileStore.Validate(profile, project);
            if (validation.Count > 0) throw new InvalidDataException(string.Join("\n", validation));
            File.WriteAllText(Path.Combine(report.ArtifactDirectory, "profile.json"), JsonConvert.SerializeObject(profile, Formatting.Indented));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            using var runner = new PlaytestSessionRunner
            {
                PrepareAutomation = w => { workspace = w; Prepare(project, profile, w, files, report); },
                RunAutomation = (w, ct) => MonitorAsync(w, profile, report, progress, ct)
            };
            var logGate = new object();
            runner.Output += (_, line) =>
            {
                lock (logGate) File.AppendAllText(Path.Combine(report.ArtifactDirectory, "session.log"), line + Environment.NewLine);
                progress?.Invoke(line);
            };
            var result = await runner.RunAsync(project, profile, gameRoot, timeout.Token);
            foreach (var diagnostic in result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)) report.Errors.Add(diagnostic.Message);
            if (report.Tests.Count == 0) report.Errors.Add("The companion completed without executing tests.");
        }
        catch (OperationCanceledException) { report.Errors.Add(cancellationToken.IsCancellationRequested ? "Run cancelled." : "Game test run timed out before completion. Inspect session.log and the isolated cache logs."); }
        catch (Exception ex) { report.Errors.Add(ex.Message); }
        finally
        {
            if (workspace is not null) File.WriteAllText(Path.Combine(report.ArtifactDirectory, "workspace.txt"), workspace.RootPath);
            ModTestService.SaveReport(report);
        }
        return report;
    }

    private static string LuaString(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";

    internal static void Prepare(ModProject project, PlaytestProfile profile, PlaytestSessionWorkspace workspace,
        IReadOnlyList<string> files, ModTestReport report)
    {
        var sources = new Dictionary<string, string>();
        var manifest = new StringBuilder("PZTestManifest = {\n");
        for (var i = 0; i < files.Count; i++)
        {
            var role = files[i].EndsWith(".server.lua", StringComparison.OrdinalIgnoreCase) ? "server" : "client-1";
            manifest.Append("{endpoint=").Append(LuaString(role)).Append(",file=").Append(LuaString(Path.GetRelativePath(project.RootPath, files[i])))
                .Append(",body=function()\n").Append(File.ReadAllText(files[i])).Append("\nend},\n");
        }
        manifest.Append("}\n");
        sources["PZToolsTesting/Harness.lua"] = ModTestService.Resource("Harness.lua");
        // Support modules and multiplayer adapter registrations run on every endpoint.
        var supportRoot = Path.Combine(project.RootPath, ModTestService.TestFolder, "support");
        manifest.Append("PZTestSupport = {\n");
        if (Directory.Exists(supportRoot))
        {
            foreach (var file in Directory.EnumerateFiles(supportRoot, "*.lua", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }).OrderBy(f => f))
                manifest.Append("function()\n").Append(File.ReadAllText(file)).Append("\nend,\n");
        }
        manifest.Append("}\n");
        sources["PZToolsTesting/Manifest.lua"] = manifest.ToString();
        var endpoints = workspace.ClientCachePaths.Select((p, i) => (Cache: p, Name: "client-" + (i + 1))).ToList();
        if (profile.Mode == PlaytestMode.DedicatedServer) endpoints.Add((workspace.ServerCachePath, "server"));
        foreach (var endpoint in endpoints)
        {
            var container = Path.Combine(endpoint.Cache, "mods", CompanionId);
            if (Directory.Exists(container)) throw new IOException("A mod already uses the reserved PZToolsTesting folder.");
            var mod = profile.Build >= 42 ? Path.Combine(container, "42") : container;
            Directory.CreateDirectory(mod);
            if (profile.Build >= 42) Directory.CreateDirectory(Path.Combine(container, "common"));
            File.WriteAllText(Path.Combine(mod, "mod.info"), "name=PZTools Test Companion\nid=" + CompanionId + "\ndescription=Isolated test session only\n");
            foreach (var source in sources)
            {
                var path = Path.Combine(mod, "media", "lua", "shared", source.Key.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, source.Value, new UTF8Encoding(false));
            }
            foreach (var side in new[] { "client", "server" })
            {
                var dir = Path.Combine(mod, "media", "lua", side); Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "ZZPZToolsTesting.lua"), ModTestService.Resource("Driver.lua"));
            }
            var lua = Path.Combine(endpoint.Cache, "Lua"); Directory.CreateDirectory(lua);
            var mode = ""; var name = "";
            if (profile.Mode == PlaytestMode.SinglePlayer)
            {
                var saves = Path.Combine(endpoint.Cache, "Saves");
                var save = Directory.EnumerateFiles(saves, "players.db", SearchOption.AllDirectories).Select(Path.GetDirectoryName).SingleOrDefault()
                    ?? throw new InvalidOperationException("The cloned save must contain exactly one players.db with a living character.");
                mode = new DirectoryInfo(Path.GetDirectoryName(save)!).Name; name = Path.GetFileName(save);
            }
            File.WriteAllLines(Path.Combine(lua, "pztests-config.txt"), new[] { report.RunId, endpoint.Name, mode, name });
            var id = profile.Build >= 42 ? "\\" + CompanionId : CompanionId;
            var lists = Directory.EnumerateFiles(endpoint.Cache, "mods.txt", SearchOption.AllDirectories).Append(Path.Combine(endpoint.Cache, "mods", "default.txt"));
            foreach (var list in lists.Where(File.Exists))
            {
                var text = File.ReadAllText(list);
                var pos = text.IndexOf('{');
                if (pos < 0) throw new InvalidDataException("Invalid mod list: " + list);
                File.WriteAllText(list, text.Insert(pos + 1, "\n    mod = " + id + ","));
            }
            if (endpoint.Name == "server")
            {
                var ini = Path.Combine(endpoint.Cache, "Server", profile.ServerName + ".ini");
                File.WriteAllLines(ini, File.ReadAllLines(ini).Select(line => line.StartsWith("Mods=", StringComparison.Ordinal) ? line + ";" + id : line).ToArray());
            }
        }
        File.WriteAllText(Path.Combine(report.ArtifactDirectory, "manifest.lua"), manifest.ToString());
    }

    private static async Task MonitorAsync(PlaytestSessionWorkspace workspace, PlaytestProfile profile, ModTestReport report, Action<string>? progress, CancellationToken ct)
    {
        var endpoints = workspace.ClientCachePaths.Select((p, i) => (Path: Path.Combine(p, "Lua", "pztests-events.txt"), Name: "client-" + (i + 1))).ToList();
        if (profile.Mode == PlaytestMode.DedicatedServer) endpoints.Add((Path.Combine(workspace.ServerCachePath, "Lua", "pztests-events.txt"), "server"));
        var sequences = endpoints.ToDictionary(e => e.Name, _ => 0);
        var completed = new HashSet<string>();
        while (completed.Count < endpoints.Count)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var endpoint in endpoints)
            {
                if (!File.Exists(endpoint.Path)) continue;
                string contents;
                using (var stream = new FileStream(endpoint.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream)) contents = await reader.ReadToEndAsync(ct);
                // Ignore an incomplete final record until the writer closes it.
                var lines = contents.Split('\n');
                foreach (var raw in lines.Take(lines.Length - 1))
                {
                    var fields = raw.TrimEnd('\r').Split('\t');
                    if (fields.Length != 8 || fields[0] != report.RunId || fields[1] != endpoint.Name) throw new InvalidDataException("Invalid companion event envelope.");
                    if (!int.TryParse(fields[2], out var sequence)) throw new InvalidDataException("Invalid companion sequence.");
                    if (sequence <= sequences[endpoint.Name]) continue;
                    if (sequence != sequences[endpoint.Name] + 1) throw new InvalidDataException("Missing companion event.");
                    sequences[endpoint.Name] = sequence;
                    if (completed.Contains(endpoint.Name)) throw new InvalidDataException("Event after endpoint completion.");
                    if (!double.TryParse(fields[7], NumberStyles.Float, CultureInfo.InvariantCulture, out var duration) || !double.IsFinite(duration) || duration < 0) throw new InvalidDataException("Invalid test duration.");
                    ModTestService.ApplyEvent(report, endpoint.Name, fields[3], Decode(fields[4]), Decode(fields[5]), Decode(fields[6]), duration);
                    progress?.Invoke($"[{endpoint.Name}] {fields[3]} {Decode(fields[5])} {Decode(fields[6])}");
                    if (fields[3] == "complete") completed.Add(endpoint.Name);
                }
            }
            await Task.Delay(100, ct);
        }
    }

    private static string Decode(string text) => text.Replace("%09", "\t").Replace("%0D", "\r").Replace("%0A", "\n").Replace("%25", "%");
}
