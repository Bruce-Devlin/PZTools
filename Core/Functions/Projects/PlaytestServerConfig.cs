using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using PZTools.Core.Models;

namespace PZTools.Core.Functions.Projects
{
    public static class PlaytestServerConfig
    {
        private static readonly HashSet<string> ManagedKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "Mods", "WorkshopItems", "DefaultPort", "UDPPort", "MaxPlayers", "Public", "PublicName",
            "Password", "RCONPassword", "DiscordToken", "WebhookAddress", "DoLuaChecksum", "SteamVAC"
        };

        public static string Write(PlaytestProfile profile, PlaytestSessionWorkspace workspace, string projectId, ModProject? project = null)
        {
            var serverDirectory = Path.Combine(workspace.ServerCachePath, "Server");
            Directory.CreateDirectory(serverDirectory);
            var path = Path.Combine(serverDirectory, profile.ServerName + ".ini");
            var dependencies = profile.Dependencies.Where(x => x.Enabled).ToList();
            var modIds = project is not null && project.ModInfo.Id.Equals(projectId, StringComparison.OrdinalIgnoreCase)
                ? DependencyLoadOrder.Resolve(project, profile).AsEnumerable()
                : dependencies.Select(x => x.ModId).Append(projectId).Where(x => !string.IsNullOrWhiteSpace(x));
            if (profile.Build >= 42)
                modIds = modIds.Select(x => x.StartsWith('\\') ? x : "\\" + x);
            var workshopIds = dependencies.Select(x => x.WorkshopId).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct();

            var lines = new List<string>
            {
                "PVP=false",
                "PauseEmpty=false",
                "GlobalChat=true",
                "Open=true",
                "Public=false",
                $"PublicName={profile.ServerName}",
                "Password=",
                $"DefaultPort={profile.ServerPort}",
                $"UDPPort={profile.ServerPort + 1}",
                $"MaxPlayers={profile.MaxPlayers}",
                $"Mods={string.Join(';', modIds.Distinct(StringComparer.OrdinalIgnoreCase))}",
                $"WorkshopItems={string.Join(';', workshopIds)}",
                "DoLuaChecksum=true",
                $"SteamVAC={!profile.NoSteam}"
            };
            lines.AddRange(ParseAdditionalOptions(profile.AdditionalServerOptions));
            File.WriteAllLines(path, lines, new UTF8Encoding(false));
            return path;
        }

        public static IReadOnlyList<string> ParseAdditionalOptions(string raw)
        {
            var lines = new List<string>();
            foreach (var sourceLine in (raw ?? "").Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                var line = sourceLine.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                    continue;
                var equals = line.IndexOf('=');
                if (equals <= 0)
                    throw new InvalidDataException($"Invalid server option '{line}'. Use Key=Value.");
                var key = line[..equals].Trim();
                var value = line[(equals + 1)..].Trim();
                if (!Regex.IsMatch(key, "^[A-Za-z][A-Za-z0-9]*$"))
                    throw new InvalidDataException($"Invalid server option name '{key}'.");
                if (ManagedKeys.Contains(key))
                    throw new InvalidDataException($"'{key}' is managed by the playtest profile.");
                if (key.Contains("password", StringComparison.OrdinalIgnoreCase) || key.Contains("token", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Secrets cannot be stored in a playtest profile.");
                lines.Add($"{key}={value}");
            }
            return lines;
        }
    }
}
