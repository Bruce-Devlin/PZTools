using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using PZTools.Core.Models;

namespace PZTools.Core.Functions.Projects;

public static class GameInstallation
{
    public static GameInstallationIdentity Inspect(string root, bool fingerprint = false)
    {
        root = Path.GetFullPath(root);
        var binary = Path.Combine(root, "projectzomboid.jar");
        var app = File.Exists(Path.Combine(root, "ProjectZomboid64.exe")) ? "108600" : "380870";
        var manifest = Path.Combine(root, "..", "..", "appmanifest_" + app + ".acf");
        var build = File.Exists(manifest) ? Regex.Match(File.ReadAllText(manifest), "\"buildid\"\\s+\"(?<id>[0-9]+)\"").Groups["id"].Value : "";
        if (build.Length > 0) build = "Steam build " + build;
        else build = "Game build unknown (no Steam manifest)";
        var hash = "";
        if (fingerprint && File.Exists(binary)) { using var input = File.OpenRead(binary); hash = Convert.ToHexString(SHA256.HashData(input)); }
        return new(root, build, binary, hash);
    }
    public static IReadOnlyList<string> Discover()
    {
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        var steam = key?.GetValue("SteamPath") as string;
        if (!string.IsNullOrWhiteSpace(steam)) libraries.Add(steam);
        libraries.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        foreach (var library in libraries.ToArray())
        {
            var vdf = Path.Combine(library, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            foreach (Match match in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"(?<path>[^\"]+)\""))
                libraries.Add(match.Groups["path"].Value.Replace("\\\\", "\\"));
        }
        return libraries.SelectMany(p => new[] { Path.Combine(p, "steamapps", "common", "ProjectZomboid"),
            Path.Combine(p, "steamapps", "common", "Project Zomboid Dedicated Server") })
            .Where(Directory.Exists).OrderBy(p => p).ToArray();
    }
}
