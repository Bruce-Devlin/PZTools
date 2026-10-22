using System.Globalization;
using System.IO;
using System.Net.NetworkInformation;
using Newtonsoft.Json;
using PZTools.Core.Functions.Zomboid;
using PZTools.Core.Models;

namespace PZTools.Core.Functions.Projects;

public sealed record PlaytestSaveCandidate(string Path, string Display);
public sealed record PlaytestSetupCheck(string Title, string Detail, bool Blocking = false);

public static class PlaytestSetupService
{
    public static string ResolveGameRoot(double build)
    {
        if (!ZomboidGame.GameMode.Equals("Managed", StringComparison.OrdinalIgnoreCase)) return ZomboidGame.GameDirectory;
        var exact = Path.Combine(ZomboidGame.GameDirectory, build.ToString("0.################", CultureInfo.InvariantCulture));
        if (Directory.Exists(exact) || !Directory.Exists(ZomboidGame.GameDirectory)) return exact;
        return Directory.EnumerateDirectories(ZomboidGame.GameDirectory).FirstOrDefault(p =>
            double.TryParse(Path.GetFileName(p), NumberStyles.Float, CultureInfo.InvariantCulture, out var found) &&
            Math.Truncate(found) == Math.Truncate(build)) ?? exact;
    }

    public static PlaytestProfile Create(ModProject project, PlaytestMode mode, PlaytestProfile? basis = null)
    {
        var profile = basis is null ? PlaytestProfileStore.CreateDefault(project) :
            JsonConvert.DeserializeObject<PlaytestProfile>(JsonConvert.SerializeObject(basis))!;
        profile.Id = Guid.NewGuid();
        profile.Name = mode == PlaytestMode.SinglePlayer ? "Project SP" : "Project MP";
        profile.Mode = mode;
        profile.SaveMode = PlaytestSaveMode.Empty;
        profile.SourceSavePath = profile.SourceSessionPath = "";
        profile.WindowMode = PlaytestWindowMode.Windowed;
        profile.WindowWidth = 1280; profile.WindowHeight = 720;
        profile.ClientCount = mode == PlaytestMode.DedicatedServer ? 2 : 1;
        profile.NoSteam = true; profile.AutoConnectClients = true;
        profile.LocalServerOnly = true;
        // Debug clients can be refused by the server's access rules. Keep debug opt-in.
        profile.LaunchArguments = "-nosteam";
        profile.ServerName = "PZTools_" + profile.Id.ToString("N")[..8];
        profile.ServerPort = FindAvailablePort();
        profile.MaxPlayers = Math.Max(2, profile.MaxPlayers);
        profile.ServerStartupTimeoutSeconds = 180;
        profile.AdditionalServerOptions = "";
        profile.ServerInstallPath = "";
        return profile;
    }

    public static int FindAvailablePort()
    {
        var ports = IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners().Select(x => x.Port).ToHashSet();
        for (var port = 17261; port < 18000; port += 2)
            if (!ports.Contains(port) && !ports.Contains(port + 1)) return port;
        throw new InvalidOperationException("No free local test port pair found between 17261 and 18000. Choose ports in the advanced profile.");
    }

    public static IReadOnlyList<PlaytestSaveCandidate> DiscoverSaves(string userDirectory)
    {
        var root = Path.Combine(userDirectory, "Saves");
        if (!Directory.Exists(root)) return Array.Empty<PlaytestSaveCandidate>();
        var options = new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };
        return Directory.EnumerateDirectories(root, "*", options)
            .Where(p => !Path.GetFileName(p).StartsWith("Multiplayer", StringComparison.OrdinalIgnoreCase))
            .SelectMany(p => Directory.EnumerateDirectories(p, "*", options))
            .Where(p => File.Exists(Path.Combine(p, "players.db")) && File.Exists(Path.Combine(p, "map_ver.bin")))
            .OrderByDescending(Directory.GetLastWriteTimeUtc)
            .Select(p => new PlaytestSaveCandidate(p, Path.GetFileName(p) + "  ·  " + new DirectoryInfo(p).Parent!.Name)).ToArray();
    }

    public static IReadOnlyList<string> ValidateSave(string source)
    {
        if (!Directory.Exists(source)) return new[] { "Choose a saved world, or create a test character below." };
        try
        {
            if (!File.Exists(Path.Combine(source, "map_ver.bin")) || !File.Exists(Path.Combine(source, "players.db")) ||
                Directory.EnumerateFiles(source, "players.db", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }).Take(2).Count() != 1)
                return new[] { "Select one world folder containing map_ver.bin and players.db, not the whole Saves folder." };
            PlaytestWorkspaceService.EnsureNoLinks(source);
            return Array.Empty<string>();
        }
        catch (Exception ex) { return new[] { "Cannot read this save: " + ex.Message }; }
    }

    public static IReadOnlyList<PlaytestSetupCheck> Check(ModProject project, PlaytestProfile profile, string gameRoot, bool automated)
    {
        var checks = new List<PlaytestSetupCheck>();
        var gameFound = File.Exists(Path.Combine(gameRoot, "ProjectZomboid64.exe"));
        checks.Add(new("Game installation", gameFound ? gameRoot : "Open App Options > System and select your game installation.", !gameFound));
        foreach (var error in PlaytestProfileStore.Validate(profile, project)) checks.Add(new("Profile needs attention", error, true));
        if (profile.Mode == PlaytestMode.SinglePlayer && (profile.LaunchArguments ?? "").Contains("-nosteam", StringComparison.OrdinalIgnoreCase))
            foreach (var dependency in profile.Dependencies.Where(d => d.Enabled && !Directory.Exists(d.SourcePath)))
                checks.Add(new("Dependency: " + dependency.ModId, "Install this mod locally, then Check again. You can also set its source in Playtest Lab > Dependencies & Load Order.", true));
        if (profile.Mode == PlaytestMode.DedicatedServer)
        {
            try
            {
                var server = PlaytestServerLauncher.Resolve(profile.ServerInstallPath, gameRoot);
                checks.Add(new("Dedicated server", server));
            }
            catch (Exception ex) { checks.Add(new("Dedicated server", ex.Message, true)); }
            var busy = IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners()
                .Any(x => x.Port == profile.ServerPort || x.Port == profile.ServerPort + 1);
            checks.Add(new("Local connection", busy ? $"Port {profile.ServerPort} or {profile.ServerPort + 1} is busy. Stop the other session or choose another port." :
                $"127.0.0.1:{profile.ServerPort} · {profile.ClientCount} separate clients · {(profile.NoSteam ? "No Steam account juggling" : "Steam enabled")}", busy));
            if (automated && !profile.AutoConnectClients) checks.Add(new("Joining the server", "Auto-connect is off. Join the local server in every client before tests can start."));
        }
        if (automated && profile.SaveMode == PlaytestSaveMode.ReuseProfileData)
            checks.Add(new("Fresh test data", "Use guided setup to capture a fixture. Automated runs must start from a fresh copy.", true));
        if (profile.Mode == PlaytestMode.SinglePlayer && automated)
        {
            if (profile.SaveMode != PlaytestSaveMode.FreshClone)
                checks.Add(new("Test character", "Choose an existing save or create a test character in guided setup.", true));
            else
            {
                var errors = ValidateSave(profile.SourceSavePath);
                checks.AddRange(errors.Select(e => new PlaytestSetupCheck("Test character", e, true)));
                if (errors.Count == 0) checks.Add(new("Test character", "World found. The game checks the living character and save compatibility when it opens the copy."));
            }
        }
        if (profile.Mode == PlaytestMode.DedicatedServer && profile.SaveMode == PlaytestSaveMode.Empty)
            checks.Add(new("Characters", "Create a character in each client. Capture a fixture once to reuse those characters in fresh test runs."));
        return checks;
    }
}
