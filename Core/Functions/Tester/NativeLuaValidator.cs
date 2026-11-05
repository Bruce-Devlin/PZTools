using System.Diagnostics;
using System.IO;
using System.Text;
using PZTools.Core.Functions.Projects;
using PZTools.Core.Models.Test;

namespace PZTools.Core.Functions.Tester;

public static class NativeLuaValidator
{
    public sealed record FileResult(string File, LuaTestResult Result);
    public static async Task<IReadOnlyList<FileResult>> CompileAsync(string gameRoot, IEnumerable<string> files, CancellationToken ct = default)
    {
        var identity = GameInstallation.Inspect(gameRoot, fingerprint: true);
        var java = Path.Combine(gameRoot, "jre64", "bin", "java.exe");
        if (!File.Exists(java) || !File.Exists(identity.Binary)) throw new FileNotFoundException($"Installed runtime requires '{java}' and '{identity.Binary}'. Fix the installation in App Options > System.");
        var directory = Path.Combine(Path.GetTempPath(), "PZToolsNativeLua", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using (var resource = typeof(NativeLuaValidator).Assembly.GetManifestResourceStream("PZTools.Resources.Testing.NativeLuaCompiler.class")
                ?? throw new FileNotFoundException("Native compiler helper is missing from this PZTools build."))
            using (var output = File.Create(Path.Combine(directory, "NativeLuaCompiler.class"))) await resource.CopyToAsync(output, ct);
            var results = new List<FileResult>();
            foreach (var batch in files.Select(Path.GetFullPath).Chunk(32))
            {
                var start = new ProcessStartInfo(java) { WorkingDirectory = gameRoot, UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var arg in new[] { "-cp", directory + Path.PathSeparator + identity.Binary, "NativeLuaCompiler" }.Concat(batch)) start.ArgumentList.Add(arg);
                using var process = Process.Start(start) ?? throw new IOException("Could not start installed Java runtime.");
                var stdout = process.StandardOutput.ReadToEndAsync(ct);
                var stderr = process.StandardError.ReadToEndAsync(ct);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(60));
                try { await process.WaitForExitAsync(timeout.Token); }
                catch { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(CancellationToken.None); throw; }
                var rows = (await stdout).Split('\n').Where(l => l.StartsWith("PZTC\t", StringComparison.Ordinal)).ToArray();
                if (rows.Length != batch.Length) throw new IOException("Installed Kahlua compiler did not return every result: " + await stderr);
                foreach (var row in rows)
                {
                    var fields = row.TrimEnd('\r').Split('\t');
                    var file = Encoding.UTF8.GetString(Convert.FromBase64String(fields[1]));
                    results.Add(new(file, new LuaTestResult { Ok = fields[2] == "passed", Interpreter = "Installed Kahlua (selected game's JVM and jar)",
                        Scope = "Native compilation only; no Lua closure executed and no API behavior verified", GameBuild = identity.Build,
                        BinaryFingerprint = identity.Sha256, Type = "Native compilation", Message = fields[3].Length == 0 ? "Compiled" : Encoding.UTF8.GetString(Convert.FromBase64String(fields[3])) }));
                }
            }
            return results;
        }
        finally { Directory.Delete(directory, true); }
    }
}
