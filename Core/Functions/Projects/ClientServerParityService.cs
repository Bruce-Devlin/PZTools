using System.IO;
using System.Security.Cryptography;
using PZTools.Core.Models;

namespace PZTools.Core.Functions.Projects
{
    public static class ClientServerParityService
    {
        public static IReadOnlyList<ProjectDiagnostic> Compare(string serverModRoot, IEnumerable<string> clientModRoots)
        {
            var diagnostics = new List<ProjectDiagnostic>();
            var server = Snapshot(serverModRoot);
            var clientIndex = 0;
            foreach (var clientRoot in clientModRoots)
            {
                clientIndex++;
                var client = Snapshot(clientRoot);
                foreach (var path in server.Keys.Union(client.Keys, StringComparer.OrdinalIgnoreCase))
                {
                    if (!server.TryGetValue(path, out var serverHash))
                        Add(diagnostics, "PZP010", $"Client {clientIndex} has an extra mod file: {path}", clientRoot);
                    else if (!client.TryGetValue(path, out var clientHash))
                        Add(diagnostics, "PZP011", $"Client {clientIndex} is missing a server mod file: {path}", clientRoot);
                    else if (!serverHash.Equals(clientHash, StringComparison.OrdinalIgnoreCase))
                        Add(diagnostics, "PZP012", $"Client {clientIndex} file differs from the server: {path}", clientRoot);
                }
            }
            return diagnostics;
        }

        private static Dictionary<string, string> Snapshot(string root)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(root))
                return result;
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(file).Equals(DeploymentManifestService.ManifestFileName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (Path.GetRelativePath(root, file).Equals("default.txt", StringComparison.OrdinalIgnoreCase))
                    continue;
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                result[Path.GetRelativePath(root, file).Replace('\\', '/')] = Convert.ToHexString(SHA256.HashData(stream));
            }
            return result;
        }

        private static void Add(List<ProjectDiagnostic> diagnostics, string code, string message, string path)
            => diagnostics.Add(new ProjectDiagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Code = code,
                Target = "Client/server parity",
                Message = message,
                Recommendation = "Redeploy the profile to every isolated cache before launching the multiplayer test.",
                FilePath = path
            });
    }
}
