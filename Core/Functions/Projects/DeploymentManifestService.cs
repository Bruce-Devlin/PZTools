using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using PZTools.Core.Models;

namespace PZTools.Core.Functions.Projects
{
    public static class DeploymentManifestService
    {
        public const string ManifestFileName = ".pztools-deployment.json";

        public static void Write(string deployedRoot, string projectId)
        {
            if (!Directory.Exists(deployedRoot))
                throw new DirectoryNotFoundException(deployedRoot);

            var manifest = new DeploymentManifest
            {
                ProjectId = projectId,
                DeployedAtUtc = DateTime.UtcNow,
                Files = Directory.EnumerateFiles(deployedRoot, "*", SearchOption.AllDirectories)
                    .Where(x => !Path.GetFileName(x).Equals(ManifestFileName, StringComparison.OrdinalIgnoreCase))
                    .Select(x => new DeploymentManifestEntry
                    {
                        Path = Path.GetRelativePath(deployedRoot, x).Replace('\\', '/'),
                        Length = new FileInfo(x).Length,
                        Sha256 = ComputeHash(x)
                    })
                    .OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };

            var json = JsonConvert.SerializeObject(manifest, Formatting.Indented);
            File.WriteAllText(Path.Combine(deployedRoot, ManifestFileName), json + Environment.NewLine, new UTF8Encoding(false));
        }

        public static IReadOnlyList<ProjectDiagnostic> Verify(string sourceRoot, string deployedRoot, string projectId)
        {
            var diagnostics = new List<ProjectDiagnostic>();
            var manifestPath = Path.Combine(deployedRoot, ManifestFileName);
            if (!Directory.Exists(deployedRoot) || !File.Exists(manifestPath))
            {
                Add(diagnostics, DiagnosticSeverity.Warning, "PZD001", "No PZTools deployment manifest was found.",
                    "Deploy the project before launching so the exact test files can be verified.", manifestPath);
                return diagnostics;
            }

            DeploymentManifest? manifest;
            try
            {
                manifest = JsonConvert.DeserializeObject<DeploymentManifest>(File.ReadAllText(manifestPath));
            }
            catch (Exception ex)
            {
                Add(diagnostics, DiagnosticSeverity.Error, "PZD002", $"Deployment manifest is invalid: {ex.Message}",
                    "Redeploy the project to replace the damaged manifest.", manifestPath);
                return diagnostics;
            }

            if (manifest is null || manifest.Files is null)
            {
                Add(diagnostics, DiagnosticSeverity.Error, "PZD002", "Deployment manifest is empty or incomplete.",
                    "Redeploy the project to recreate the manifest.", manifestPath);
                return diagnostics;
            }

            if (!string.Equals(manifest.ProjectId, projectId, StringComparison.OrdinalIgnoreCase))
                Add(diagnostics, DiagnosticSeverity.Error, "PZD003",
                    $"Deployed mod ID '{manifest.ProjectId}' does not match project ID '{projectId}'.",
                    "Deploy the current project again before launching.", manifestPath);

            var manifestFiles = manifest.Files
                .GroupBy(x => NormalizeRelative(x.Path), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
            foreach (var entry in manifest.Files)
            {
                var relative = NormalizeRelative(entry.Path);
                var deployedFile = SafeCombine(deployedRoot, relative);
                if (deployedFile is null || !File.Exists(deployedFile))
                {
                    Add(diagnostics, DiagnosticSeverity.Error, "PZD010", $"Deployed file is missing: {relative}",
                        "Redeploy the project to restore the exact playtest payload.", deployedFile ?? deployedRoot);
                    continue;
                }

                if (!HashMatches(deployedFile, entry))
                    Add(diagnostics, DiagnosticSeverity.Error, "PZD011", $"Deployed file was modified after deployment: {relative}",
                        "Redeploy, or move intentional runtime edits back into the source project first.", deployedFile);

                var sourceFile = SafeCombine(sourceRoot, relative);
                if (sourceFile is null || !File.Exists(sourceFile) || !HashMatches(sourceFile, entry))
                    Add(diagnostics, DiagnosticSeverity.Warning, "PZD012", $"Source changed after deployment: {relative}",
                        "Redeploy before launching to test the current source.", sourceFile ?? sourceRoot);
            }

            if (Directory.Exists(sourceRoot))
            {
                foreach (var sourceFile in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(sourceRoot, sourceFile);
                    if (!ProjectDeployer.ShouldIncludePath(relative))
                        continue;
                    relative = NormalizeRelative(relative);
                    if (!manifestFiles.ContainsKey(relative))
                        Add(diagnostics, DiagnosticSeverity.Warning, "PZD013", $"New source file has not been deployed: {relative}",
                            "Redeploy before launching to include the new file.", sourceFile);
                }
            }

            return diagnostics;
        }

        private static bool HashMatches(string path, DeploymentManifestEntry entry)
        {
            try
            {
                var info = new FileInfo(path);
                return info.Length == entry.Length && ComputeHash(path).Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static string ComputeHash(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Convert.ToHexString(SHA256.HashData(stream));
        }

        private static string NormalizeRelative(string path) => path.Replace('\\', '/').TrimStart('/');

        private static string? SafeCombine(string root, string relative)
        {
            try
            {
                var fullRoot = Path.GetFullPath(root);
                var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
                var prefix = Path.TrimEndingDirectorySeparator(fullRoot) + Path.DirectorySeparatorChar;
                return fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? fullPath : null;
            }
            catch { return null; }
        }

        private static void Add(List<ProjectDiagnostic> diagnostics, DiagnosticSeverity severity, string code,
            string message, string recommendation, string? filePath)
            => diagnostics.Add(new ProjectDiagnostic
            {
                Severity = severity,
                Code = code,
                Target = "Deployment",
                Message = message,
                Recommendation = recommendation,
                FilePath = filePath
            });

        private sealed class DeploymentManifest
        {
            public string ProjectId { get; set; } = "";
            public DateTime DeployedAtUtc { get; set; }
            public List<DeploymentManifestEntry> Files { get; set; } = new();
        }

        private sealed class DeploymentManifestEntry
        {
            public string Path { get; set; } = "";
            public long Length { get; set; }
            public string Sha256 { get; set; } = "";
        }
    }
}
