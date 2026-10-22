using System.IO;
using Newtonsoft.Json;
using PZTools.Core.Models;

namespace PZTools.Core.Functions.Projects;

public static class PlaytestFixtureService
{
    private sealed record Receipt(int Version, double Build, int Clients, string Server, int Port, bool NoSteam);
    private const string ReceiptFile = "test-fixture.json";

    internal static void Capture(PlaytestProfile profile, PlaytestSessionWorkspace workspace)
    {
        File.WriteAllText(Path.Combine(workspace.RootPath, ReceiptFile), JsonConvert.SerializeObject(
            new Receipt(1, profile.Build, profile.ClientCount, profile.ServerName, profile.ServerPort, profile.NoSteam), Formatting.Indented));
    }

    public static IReadOnlyList<string> Validate(PlaytestProfile profile)
    {
        try
        {
            if (profile.Mode != PlaytestMode.DedicatedServer) return new[] { "Session fixtures are for multiplayer. Choose a single-player save instead." };
            var path = Path.Combine(profile.SourceSessionPath, ReceiptFile);
            if (string.IsNullOrWhiteSpace(profile.SourceSessionPath) || !File.Exists(path))
                return new[] { "Capture a multiplayer fixture in guided setup first. The fixture folder is missing or incomplete." };
            var receipt = JsonConvert.DeserializeObject<Receipt>(File.ReadAllText(path));
            if (receipt is null || receipt.Version != 1 || receipt.Build != profile.Build || receipt.Clients != profile.ClientCount ||
                receipt.Server != profile.ServerName || receipt.Port != profile.ServerPort || receipt.NoSteam != profile.NoSteam)
                return new[] { "This fixture uses a different build, client count, server name, port, or Steam mode. Restore those settings or capture a new fixture." };
            PlaytestWorkspaceService.EnsureNoLinks(profile.SourceSessionPath);
            var caches = Enumerable.Range(1, profile.ClientCount).Select(i => "client-" + i).Append("server");
            foreach (var cache in caches)
            {
                var saves = Path.Combine(profile.SourceSessionPath, cache, "Saves");
                if (!Directory.Exists(saves) || !Directory.EnumerateFiles(saves, "*", SearchOption.AllDirectories).Any())
                    return new[] { $"The fixture has no saved world for {cache}. Enter the world and quit normally in every client before capturing." };
            }
            return Array.Empty<string>();
        }
        catch (Exception ex) { return new[] { "Cannot read the multiplayer fixture: " + ex.Message }; }
    }
}
