using System.IO;
using System.Security.Cryptography;
using PZTools.Core.Functions.Tester;
using PZTools.Core.Models;

namespace PZTools.Core.Functions.Projects;

internal static class PlaytestLocalConnection
{
    public const string ModId = "PZToolsLocalConnect";
    public static void Prepare(PlaytestProfile profile, PlaytestSessionWorkspace workspace)
    {
        foreach (var cache in workspace.ClientCachePaths.Append(workspace.ServerCachePath))
        {
            var container = Path.Combine(cache, "mods", ModId);
            if (Directory.Exists(container) && !File.Exists(Path.Combine(container, ".pztools-owned")))
                throw new IOException("A mod uses the reserved PZToolsLocalConnect folder.");
            var mod = profile.Build >= 42 ? Path.Combine(container, "42") : container;
            Directory.CreateDirectory(Path.Combine(mod, "media", "lua", "client"));
            File.WriteAllText(Path.Combine(container, ".pztools-owned"), "PZTools local connection companion");
            if (profile.Build >= 42) Directory.CreateDirectory(Path.Combine(container, "common"));
            File.WriteAllText(Path.Combine(mod, "mod.info"), "name=PZTools Local Connection\nid=" + ModId + "\n");
            File.WriteAllText(Path.Combine(mod, "media", "lua", "client", "PZToolsLocalConnect.lua"), ModTestService.Resource("LocalConnect.lua"));
            var index = workspace.ClientCachePaths.ToList().IndexOf(cache);
            if (index < 0) continue;
            var lua = Path.Combine(cache, "Lua"); Directory.CreateDirectory(lua);
            var login = Path.Combine(lua, "pztools-local-login.txt");
            var password = File.Exists(login) ? File.ReadAllLines(login).ElementAtOrDefault(3) : null;
            password ??= Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            File.WriteAllLines(login, new[] { profile.ServerName, profile.ServerPort.ToString(System.Globalization.CultureInfo.InvariantCulture), "PZTools" + (index + 1), password });
        }
    }
}
