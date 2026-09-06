using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Loaders;
using Newtonsoft.Json;
using PZTools.Core.Models;
using PZTools.Core.Models.Test;

namespace PZTools.Core.Functions.Tester;

public static class ModTestService
{
    public const string TestFolder = ".pztests";
    public static string Resource(string name)
    {
        using var stream = typeof(ModTestService).Assembly.GetManifestResourceStream("PZTools.Resources.Testing." + name)
            ?? throw new FileNotFoundException(name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static IReadOnlyList<string> Discover(ModProject project, string kind, string? filter = null)
    {
        if (kind is not ("unit" or "game")) throw new ArgumentException("Expected unit or game.");
        var root = Path.Combine(project.RootPath, TestFolder);
        if (!Directory.Exists(root)) return Array.Empty<string>();
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
        return Directory.EnumerateFiles(root, "*.lua", options)
            .Where(f => kind == "unit" ? f.EndsWith(".unit.lua", StringComparison.OrdinalIgnoreCase)
                : f.EndsWith(".game.lua", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".e2e.lua", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".server.lua", StringComparison.OrdinalIgnoreCase))
            .Where(f => string.IsNullOrWhiteSpace(filter) || Path.GetRelativePath(root, f).Replace('\\', '/').Contains(filter.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static ModTestReport CreateReport(ModProject project, string mode)
    {
        var report = new ModTestReport { Project = project.Name, Mode = mode };
        report.ArtifactDirectory = Path.Combine(project.RootPath, ".pztools", "test-results", report.RunId);
        Directory.CreateDirectory(report.ArtifactDirectory);
        return report;
    }

    public static Task<ModTestReport> RunUnitAsync(ModProject project, string? filter = null, CancellationToken cancellationToken = default)
        => Task.Run(() => RunUnit(project, filter, cancellationToken), CancellationToken.None);

    private static ModTestReport RunUnit(ModProject project, string? filter, CancellationToken ct)
    {
        var report = CreateReport(project, "unit");
        try
        {
            var files = Discover(project, "unit", filter);
            if (files.Count == 0) report.Errors.Add("No unit test files match this run. Unit files end in .unit.lua; game files require Run game tests.");
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(project.RootPath, file);
                var watch = Stopwatch.StartNew();
                var script = new Script(CoreModules.Preset_SoftSandbox | CoreModules.LoadMethods);
                script.Options.ScriptLoader = new ProjectLoader(project.RootPath);
                script.Options.DebugPrint = line => File.AppendAllText(Path.Combine(report.ArtifactDirectory, "unit.log"), line + Environment.NewLine);
                try
                {
                    Execute(script, Resource("Harness.lua"), "PZTools/Harness", ct, watch);
                    var pzt = script.Globals.Get("PZT").Table;
                    pzt.Set("now", DynValue.NewCallback((_, _) => DynValue.NewNumber(watch.Elapsed.TotalSeconds)));
                    pzt.Set("file", DynValue.NewString(relative));
                    pzt.Set("emit", DynValue.NewCallback((_, args) =>
                    {
                        ApplyEvent(report, "unit", args[0].String, args[1].String, args[2].String, args[3].String, args[4].Number);
                        return DynValue.Nil;
                    }));
                    Execute(script, File.ReadAllText(file), relative, ct, watch);
                    var count = report.Tests.Count;
                    while (!pzt.Get("finished").Boolean)
                    {
                        RunFunction(script, pzt.Get("tick"), ct, watch);
                        Thread.Yield();
                    }
                    if (report.Tests.Count == count) report.Errors.Add(relative + ": No tests registered.");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    report.Errors.Add(relative + ": " + ex.Message);
                    foreach (var test in report.Tests.Where(t => t.Status == "running")) { test.Status = "error"; test.Message = ex.Message; }
                }
            }
        }
        catch (OperationCanceledException) { report.Errors.Add("Run cancelled."); }
        finally { SaveReport(report); }
        return report;
    }

    private static void Execute(Script script, string code, string name, CancellationToken ct, Stopwatch watch)
        => RunFunction(script, script.LoadString(code, null, name), ct, watch);

    private static void RunFunction(Script script, DynValue function, CancellationToken ct, Stopwatch watch)
    {
        var coroutine = script.CreateCoroutine(function).Coroutine;
        coroutine.AutoYieldCounter = 10000;
        do
        {
            ct.ThrowIfCancellationRequested();
            if (watch.Elapsed > TimeSpan.FromSeconds(60)) throw new TimeoutException("Unit file exceeded 60 seconds.");
            coroutine.Resume();
        } while (coroutine.State != CoroutineState.Dead);
    }

    public static void ApplyEvent(ModTestReport report, string endpoint, string kind, string file, string name, string message, double seconds)
    {
        if (kind == "error") { report.Errors.Add(endpoint + ": " + message); return; }
        if (kind == "begin")
        {
            report.Tests.Add(new ModTestCase { File = file, Name = name, Endpoint = endpoint });
            return;
        }
        if (kind is "complete" or "ready") return;
        var test = report.Tests.LastOrDefault(t => t.Endpoint == endpoint && t.File == file && t.Name == name && t.Status == "running");
        if (test is null) { report.Errors.Add("Unexpected test event: " + kind + " " + name); return; }
        if (kind == "step") test.Steps.Add(message);
        else if (kind is "passed" or "failed") { test.Status = kind; test.Message = message; test.DurationSeconds = seconds; }
        else report.Errors.Add("Unknown test event: " + kind);
    }

    public static void SaveReport(ModTestReport report)
    {
        foreach (var test in report.Tests.Where(t => t.Status == "running")) { test.Status = "error"; test.Message = "Run ended before this test completed."; }
        Directory.CreateDirectory(report.ArtifactDirectory);
        File.WriteAllText(Path.Combine(report.ArtifactDirectory, "results.json"), JsonConvert.SerializeObject(report, Formatting.Indented));
        var suite = new XElement("testsuite", new XAttribute("name", report.Project),
            new XAttribute("tests", report.Tests.Count + report.Errors.Count),
            new XAttribute("failures", report.Tests.Count(t => t.Status == "failed")),
            new XAttribute("errors", report.Errors.Count + report.Tests.Count(t => t.Status == "error")),
            report.Tests.Select(t => new XElement("testcase", new XAttribute("name", t.Name),
                new XAttribute("classname", t.Endpoint + ":" + t.File),
                new XAttribute("time", t.DurationSeconds.ToString(CultureInfo.InvariantCulture)),
                t.Status == "passed" ? null : new XElement(t.Status == "failed" ? "failure" : "error", new XAttribute("message", t.Message)),
                new XElement("system-out", string.Join('\n', t.Steps)))),
            report.Errors.Select(e => new XElement("testcase", new XAttribute("name", "Runner"), new XElement("error", e))));
        new XDocument(suite).Save(Path.Combine(report.ArtifactDirectory, "junit.xml"));
    }

    public static void Scaffold(ModProject project)
    {
        var root = Path.Combine(project.RootPath, TestFolder);
        Directory.CreateDirectory(root);
        foreach (var pair in new[] { ("example.unit.lua", "ExampleUnit.lua"), ("example.game.lua", "ExampleGame.lua"), ("README.md", "") })
        {
            var path = Path.Combine(root, pair.Item1);
            if (!File.Exists(path)) File.WriteAllText(path, pair.Item2.Length > 0 ? Resource(pair.Item2) :
                "Lua tests: *.unit.lua runs outside the game; *.game.lua and *.e2e.lua run on client 1; *.server.lua runs on the dedicated server. See PZTools docs/testing.md. Test files are excluded from deployment.\n");
        }
    }

    private sealed class ProjectLoader : ScriptLoaderBase
    {
        private readonly string root;
        public ProjectLoader(string root)
        {
            this.root = Path.GetFullPath(root);
            ModulePaths = new[] { ".pztests/?.lua", "common/media/lua/shared/?.lua", "42/media/lua/shared/?.lua", "media/lua/shared/?.lua" };
            IgnoreLuaPathGlobal = true;
        }
        private string Resolve(string file)
        {
            var path = Path.GetFullPath(Path.Combine(root, file));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Test module is outside the project.");
            for (var current = path; current.Length > root.Length; current = Path.GetDirectoryName(current)!)
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked test modules are not supported.");
            return path;
        }
        public override object LoadFile(string file, Table globalContext) => File.ReadAllText(Resolve(file));
        public override bool ScriptFileExists(string name) => File.Exists(Resolve(name));
    }
}
