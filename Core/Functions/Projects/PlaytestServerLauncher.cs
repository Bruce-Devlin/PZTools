using System.Diagnostics;
using System.IO;
using Newtonsoft.Json.Linq;

namespace PZTools.Core.Functions.Projects;

internal static class PlaytestServerLauncher
{
    public static string Resolve(string configured, string gameRoot)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (File.Exists(configured) && new[] { ".bat", ".cmd", ".exe" }.Contains(Path.GetExtension(configured), StringComparer.OrdinalIgnoreCase)) return configured;
            if (File.Exists(Path.Combine(configured, "StartServer64.bat"))) return Path.Combine(configured, "StartServer64.bat");
            if (HasBundledServer(configured)) return Path.Combine(configured, "ProjectZomboid64.json");
            throw new FileNotFoundException("The selected server folder has no supported launcher. Select the matching dedicated-server installation or clear the field to use the game's bundled server.");
        }
        if (File.Exists(Path.Combine(gameRoot, "StartServer64.bat"))) return Path.Combine(gameRoot, "StartServer64.bat");
        if (HasBundledServer(gameRoot)) return Path.Combine(gameRoot, "ProjectZomboid64.json");
        var sibling = Path.Combine(Path.GetDirectoryName(gameRoot) ?? gameRoot, "Project Zomboid Dedicated Server", "StartServer64.bat");
        if (File.Exists(sibling)) return sibling;
        throw new FileNotFoundException("Server files were not found. Install Project Zomboid Dedicated Server from Steam Library > Tools using the same build as your clients, then select its folder.");
    }

    private static bool HasBundledServer(string root) => File.Exists(Path.Combine(root, "ProjectZomboid64.json")) &&
        File.Exists(Path.Combine(root, "projectzomboid.jar")) && File.Exists(Path.Combine(root, "jre64", "bin", "java.exe")) &&
        File.Exists(Path.Combine(root, "ProjectZomboidServer.bat"));

    public static ProcessStartInfo Create(string launcher, string arguments, bool noSteam)
    {
        var root = Path.GetDirectoryName(launcher)!;
        if (Path.GetExtension(launcher).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            // Use the installation's JVM requirements, not a hard-coded Java version or the
            // bundled .bat (some builds discard all incoming server arguments and pause).
            var config = JObject.Parse(File.ReadAllText(launcher));
            var vm = config["vmArgs"]?.Values<string>().OfType<string>().Where(x => !x.StartsWith("-Dzomboid.steam=") && !x.StartsWith("-Djava.library.path=")).ToList()
                ?? throw new InvalidDataException("Game launcher configuration has no JVM arguments.");
            vm.Add("-Dzomboid.steam=" + (noSteam ? "0" : "1"));
            vm.Add("-Djava.library.path=./natives/;./natives/win64/;./win64/;./");
            var classpath = config["classpath"]?.Values<string>().ToArray() ?? throw new InvalidDataException("Game launcher configuration has no classpath.");
            return new ProcessStartInfo(Path.Combine(root, "jre64", "bin", "java.exe"))
            {
                WorkingDirectory = root,
                Arguments = string.Join(" ", vm.Select(Quote)) + " -cp " + Quote(string.Join(';', classpath)) + " zombie.network.GameServer " + arguments
            };
        }
        if (Path.GetExtension(launcher).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            return new ProcessStartInfo(launcher, arguments) { WorkingDirectory = root };
        return new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe", $"/d /s /c \"\"{launcher}\" {arguments}\"") { WorkingDirectory = root };
    }

    private static string Quote(string text) => "\"" + text.Replace("\"", "") + "\"";
}
