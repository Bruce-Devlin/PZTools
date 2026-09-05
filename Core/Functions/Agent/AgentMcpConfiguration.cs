using System.IO;
using System.Security.Cryptography;
using System.Text;
using PZTools.Core.Functions.Projects;
using PZTools.Core.Models;

namespace PZTools.Core.Functions.Agent
{
    public static class AgentMcpConfiguration
    {
        public const string ServerName = "pztools";
        public const string ManagedBlockStart = "# BEGIN PZTOOLS MCP (managed by PZTools)";
        public const string ManagedBlockEnd = "# END PZTOOLS MCP";

        public static readonly string[] ReadOnlyTools =
        {
            "pztools_capabilities",
            "pztools_get_editor_state",
            "pztools_list_files",
            "pztools_read_file",
            "pztools_get_game_status",
            "pztools_get_game_log"
        };

        public static string GetPipeName(string projectRoot)
        {
            var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot)).ToUpperInvariant();
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..20];
            return $"PZTools.Agent.{hash}";
        }

        public static string GetLifetimePipeName(string projectRoot)
            => GetPipeName(projectRoot) + ".Lifetime";

        public static IReadOnlyList<string> GetEnabledTools(AppSettings settings)
        {
            var tools = new List<string> { "pztools_capabilities" };
            if (settings.AgentMcpAllowEditorControl)
                tools.AddRange(new[] { "pztools_get_editor_state", "pztools_list_files", "pztools_read_file", "pztools_open_file" });
            if (settings.AgentMcpAllowProjectWrites)
                tools.Add("pztools_write_file");
            if (settings.AgentMcpAllowTesting)
                tools.AddRange(new[] { "pztools_test_lua", "pztools_check_project" });
            if (settings.AgentMcpAllowDeployment)
                tools.Add("pztools_deploy_project");
            if (settings.AgentMcpAllowGameControl)
                tools.AddRange(new[] { "pztools_start_game", "pztools_stop_game", "pztools_get_game_status", "pztools_get_game_log" });
            return tools.Distinct(StringComparer.Ordinal).ToArray();
        }

        public static string ConfigureCurrentProject(AppSettings settings)
        {
            var project = ProjectEngine.CurrentProject
                ?? throw new InvalidOperationException("Open a PZTools project before configuring Codex.");
            var serverPath = FindServerPath();
            if (serverPath == null)
                throw new FileNotFoundException("PZTools.Mcp.exe was not found beside this PZTools installation. Build or reinstall PZTools, then try again.");

            return ConfigureProject(project.RootPath, serverPath, settings);
        }

        public static string ConfigureProject(string projectRoot, string serverPath, AppSettings settings)
        {
            if (!Directory.Exists(projectRoot))
                throw new DirectoryNotFoundException(projectRoot);
            if (!File.Exists(serverPath))
                throw new FileNotFoundException("PZTools MCP server was not found.", serverPath);

            projectRoot = Path.GetFullPath(projectRoot);
            serverPath = Path.GetFullPath(serverPath);
            var codexDirectory = Path.Combine(projectRoot, ".codex");
            Directory.CreateDirectory(codexDirectory);
            var configPath = Path.Combine(codexDirectory, "config.toml");
            var existing = File.Exists(configPath) ? File.ReadAllText(configPath) : string.Empty;
            if (!existing.Contains(ManagedBlockStart, StringComparison.Ordinal) &&
                existing.Split('\n').Any(line => line.Trim().Equals("[mcp_servers.pztools]", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("This project already contains an unmanaged [mcp_servers.pztools] table. Rename or remove it before using automatic setup.");
            var managed = BuildManagedBlock(serverPath, projectRoot, settings);
            var updated = ReplaceManagedBlock(existing, managed);
            File.WriteAllText(configPath, updated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return configPath;
        }

        public static string? FindServerPath()
        {
            var appDirectory = Path.GetDirectoryName(AppPaths.CurrentFilePath) ?? AppPaths.CurrentDirectoryPath;
            var candidates = new[]
            {
                Path.Combine(appDirectory, "mcp", "PZTools.Mcp.exe"),
                Path.Combine(appDirectory, "PZTools.Mcp.exe")
            };
            return candidates.FirstOrDefault(File.Exists);
        }

        internal static string ReplaceManagedBlock(string existing, string managedBlock)
        {
            var start = existing.IndexOf(ManagedBlockStart, StringComparison.Ordinal);
            var end = existing.IndexOf(ManagedBlockEnd, StringComparison.Ordinal);
            string remaining;
            if (start >= 0 && end >= start)
            {
                end += ManagedBlockEnd.Length;
                remaining = (existing[..start] + existing[end..]).Trim();
            }
            else
            {
                remaining = existing.Trim();
            }

            return remaining.Length == 0
                ? managedBlock + Environment.NewLine
                : remaining + Environment.NewLine + Environment.NewLine + managedBlock + Environment.NewLine;
        }

        private static string BuildManagedBlock(string serverPath, string projectRoot, AppSettings settings)
        {
            var tools = GetEnabledTools(settings);
            var readOnly = ReadOnlyTools.Intersect(tools, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
            var builder = new StringBuilder();
            builder.AppendLine(ManagedBlockStart);
            builder.AppendLine("[mcp_servers.pztools]");
            builder.AppendLine($"command = {Toml(serverPath)}");
            builder.AppendLine($"args = [{Toml("--pipe")}, {Toml(GetPipeName(projectRoot))}, {Toml("--project-root")}, {Toml(projectRoot)}]");
            builder.AppendLine($"enabled = {settings.AgentMcpEnabled.ToString().ToLowerInvariant()}");
            builder.AppendLine("required = false");
            builder.AppendLine("startup_timeout_sec = 15");
            builder.AppendLine("tool_timeout_sec = 300");
            builder.AppendLine("default_tools_approval_mode = \"prompt\"");
            builder.AppendLine($"enabled_tools = [{string.Join(", ", tools.Select(Toml))}]");
            foreach (var tool in readOnly)
            {
                builder.AppendLine();
                builder.AppendLine($"[mcp_servers.pztools.tools.{tool}]");
                builder.AppendLine("approval_mode = \"approve\"");
            }
            builder.Append(ManagedBlockEnd);
            return builder.ToString();
        }

        private static string Toml(string value)
            => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
