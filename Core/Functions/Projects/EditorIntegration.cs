using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PZTools.Core.Functions.Zomboid;
using PZTools.Core.Models;

namespace PZTools.Core.Functions.Projects
{
    public sealed class EditorSetupResult
    {
        public required string WorkspacePath { get; init; }
        public List<string> CreatedFiles { get; } = new();
        public List<string> UpdatedFiles { get; } = new();
        public List<string> PreservedFiles { get; } = new();
    }

    public static class EditorIntegration
    {
        private static readonly string[] EditorCommands = { "code", "code-insiders", "codium" };

        public static EditorSetupResult PrepareVsCodeWorkspace(ModProject project)
        {
            ArgumentNullException.ThrowIfNull(project);
            if (!Directory.Exists(project.RootPath))
                throw new DirectoryNotFoundException(project.RootPath);

            var vscodePath = Path.Combine(project.RootPath, ".vscode");
            Directory.CreateDirectory(vscodePath);
            var workspacePath = Path.Combine(project.RootPath, $"{SafeFileName(project.Name)}.code-workspace");
            var result = new EditorSetupResult { WorkspacePath = workspacePath };

            WriteIfMissing(Path.Combine(vscodePath, "extensions.json"), BuildExtensions(), result);
            WriteIfMissing(Path.Combine(vscodePath, "settings.json"), BuildSettings(), result);
            MergeTasks(Path.Combine(vscodePath, "tasks.json"), result);
            WriteIfMissing(Path.Combine(project.RootPath, ".luarc.json"), BuildLuaConfig(), result);
            WriteIfMissing(workspacePath, BuildWorkspace(project), result);
            return result;
        }

        public static string? FindVsCode()
        {
            foreach (var command in EditorCommands)
            {
                if (CanStart(command))
                    return command;
            }

            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var candidates = new[]
            {
                Path.Combine(local, "Programs", "Microsoft VS Code", "Code.exe"),
                Path.Combine(local, "Programs", "Microsoft VS Code Insiders", "Code - Insiders.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft VS Code", "Code.exe")
            };
            return candidates.FirstOrDefault(File.Exists);
        }

        public static void OpenInVsCode(ModProject project, string? filePath = null, int? line = null)
        {
            var editor = FindVsCode()
                ?? throw new InvalidOperationException("Visual Studio Code, VS Code Insiders, or VSCodium was not found. Install one or configure your preferred editor in App Options.");
            var workspace = Path.Combine(project.RootPath, $"{SafeFileName(project.Name)}.code-workspace");
            var target = File.Exists(workspace) ? workspace : project.RootPath;

            var start = new ProcessStartInfo { FileName = editor, UseShellExecute = true };
            start.ArgumentList.Add("--reuse-window");
            start.ArgumentList.Add(target);
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                start.ArgumentList.Add("--goto");
                start.ArgumentList.Add(line > 0 ? $"{filePath}:{line}" : filePath);
            }
            Process.Start(start);
        }

        private static string BuildExtensions() => JsonConvert.SerializeObject(new
        {
            recommendations = new[]
            {
                "sumneko.lua",
                "tangzx.emmylua",
                "simkdt.project-zomboid-scripts"
            }
        }, Formatting.Indented);

        private static string BuildSettings()
        {
            var settings = new JObject
            {
                ["files.associations"] = new JObject { ["*.txt"] = "ZedScripts" },
                ["files.exclude"] = new JObject { ["**/.pztools-deploy"] = true },
                ["search.exclude"] = new JObject { ["**/.pztools-deploy"] = true },
                ["editor.formatOnSave"] = false,
                ["files.eol"] = "\r\n"
            };

            var scripts = Path.Combine(ZomboidGame.GameDirectory, "media", "scripts");
            if (Directory.Exists(scripts))
                settings["ZedScripts.defaultLibrary"] = scripts;
            return settings.ToString(Formatting.Indented);
        }

        private static string BuildLuaConfig()
        {
            var libraries = new JArray();
            var gameLua = Path.Combine(ZomboidGame.GameDirectory, "media", "lua");
            if (Directory.Exists(gameLua))
                libraries.Add(gameLua.Replace('\\', '/'));

            return new JObject
            {
                ["$schema"] = "https://raw.githubusercontent.com/LuaLS/vscode-lua/master/setting/schema.json",
                ["runtime.version"] = "Lua 5.1",
                ["workspace.library"] = libraries,
                ["workspace.checkThirdParty"] = false,
                ["diagnostics.globals"] = new JArray("Events", "SandboxVars", "getPlayer", "getSpecificPlayer", "getCore", "Perks", "ProceduralDistributions")
            }.ToString(Formatting.Indented);
        }

        private static void MergeTasks(string path, EditorSetupResult result)
        {
            var existed = File.Exists(path);
            JObject document;
            if (existed)
            {
                try
                {
                    document = JObject.Parse(File.ReadAllText(path));
                }
                catch (JsonException ex)
                {
                    result.PreservedFiles.Add(path);
                    throw new InvalidOperationException($"VS Code tasks were not changed because '{path}' is not valid JSON: {ex.Message}", ex);
                }
            }
            else
            {
                document = new JObject { ["version"] = "2.0.0" };
            }

            var tasks = document["tasks"] as JArray ?? new JArray();
            document["tasks"] = tasks;
            var existingLabels = tasks.OfType<JObject>()
                .Select(task => task.Value<string>("label"))
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var added = false;
            foreach (var task in BuildTasks())
            {
                if (existingLabels.Add(task.Value<string>("label")!))
                {
                    tasks.Add(task);
                    added = true;
                }
            }

            if (!added && existed)
            {
                result.PreservedFiles.Add(path);
                return;
            }

            File.WriteAllText(path, document.ToString(Formatting.Indented) + Environment.NewLine);
            if (existed)
                result.UpdatedFiles.Add(path);
            else
                result.CreatedFiles.Add(path);
        }

        private static IEnumerable<JObject> BuildTasks()
        {
            var executablePath = Path.Combine(AppContext.BaseDirectory, "PZTools.exe");
            var assemblyPath = Path.Combine(AppContext.BaseDirectory, "PZTools.dll");
            var useExecutable = !File.Exists(assemblyPath) && File.Exists(executablePath);
            if (!useExecutable && !File.Exists(assemblyPath))
                throw new InvalidOperationException("The PZTools task runner could not be located for VS Code tasks.");

            var matcher = new JObject
            {
                ["owner"] = "pztools",
                ["fileLocation"] = new JArray("relative", "${workspaceFolder}"),
                ["pattern"] = new JObject
                {
                    ["regexp"] = "^(.*)\\((\\d+),(\\d+)\\):\\s+(error|warning|info)\\s+(PZ[A-Z0-9]+):\\s+(.*)$",
                    ["file"] = 1,
                    ["line"] = 2,
                    ["column"] = 3,
                    ["severity"] = 4,
                    ["code"] = 5,
                    ["message"] = 6
                }
            };

            JObject ShellTask(string label, string command, string? group = null) => new()
            {
                ["label"] = label,
                ["type"] = "process",
                ["command"] = useExecutable ? executablePath : "dotnet",
                ["args"] = useExecutable
                    ? new JArray(ArgumentForTaskRunner(), command, "--project", "${workspaceFolder}")
                    : new JArray(assemblyPath, ArgumentForTaskRunner(), command, "--project", "${workspaceFolder}"),
                ["problemMatcher"] = new JArray(matcher.DeepClone()),
                ["group"] = group,
                ["presentation"] = new JObject { ["reveal"] = "always", ["panel"] = "shared", ["clear"] = true }
            };

            yield return ShellTask("PZTools: Test Mod", "health", "test");
            yield return ShellTask("PZTools: Run Unit Tests", "unit", "test");
            yield return ShellTask("PZTools: Run Game Tests", "game-tests", "test");
            yield return ShellTask("PZTools: Deploy Mod", "deploy");
            yield return new JObject
            {
                ["label"] = "PZTools: Test and Deploy Mod",
                ["dependsOrder"] = "sequence",
                ["dependsOn"] = new JArray("PZTools: Test Mod", "PZTools: Deploy Mod"),
                ["problemMatcher"] = new JArray(),
                ["group"] = new JObject { ["kind"] = "build", ["isDefault"] = true }
            };
        }

        private static string ArgumentForTaskRunner() => ProjectTaskCommand.Argument;

        private static string BuildWorkspace(ModProject project)
        {
            var folders = new JArray { new JObject { ["name"] = project.Name, ["path"] = "." } };
            var gamePath = ZomboidGame.GameDirectory;
            if (Directory.Exists(gamePath))
                folders.Add(new JObject { ["name"] = "Project Zomboid (read-only reference)", ["path"] = gamePath });
            return new JObject
            {
                ["folders"] = folders,
                ["settings"] = new JObject
                {
                    ["files.readonlyInclude"] = new JObject { ["Project Zomboid (read-only reference)/**"] = true }
                },
                ["extensions"] = new JObject
                {
                    ["recommendations"] = new JArray("sumneko.lua", "tangzx.emmylua", "simkdt.project-zomboid-scripts")
                }
            }.ToString(Formatting.Indented);
        }

        private static void WriteIfMissing(string path, string contents, EditorSetupResult result)
        {
            if (File.Exists(path))
            {
                result.PreservedFiles.Add(path);
                return;
            }
            File.WriteAllText(path, contents + Environment.NewLine);
            result.CreatedFiles.Add(path);
        }

        private static bool CanStart(string command)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = "--version",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });
                if (process is null)
                    return false;
                process.WaitForExit(1200);
                return process.HasExited && process.ExitCode == 0;
            }
            catch { return false; }
        }

        private static string SafeFileName(string value)
            => string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c));
    }
}
