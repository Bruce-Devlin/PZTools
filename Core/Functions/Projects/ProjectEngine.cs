using System.Globalization;
using System.IO;
using PZTools.Core.Functions.Zomboid;
using PZTools.Core.Models;

namespace PZTools.Core.Functions.Projects
{
    public static class ProjectEngine
    {
        private static readonly HashSet<string> ReservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        /// <summary>
        /// The root directory where all mods are stored.
        /// </summary>
        public static string ProjectsRootPath { get; set; } = Path.Combine(AppPaths.CurrentDirectory.FullName, "Projects");

        private static readonly List<ModProject> LoadedProjects = new();
        public static ModProject? CurrentProject { get; private set; } = null;
        public static string CurrentProjectPath => CurrentProject?.RootPath ?? "";
        public static void LoadProject(ModProject project)
        {
            CurrentProject = project;
            CurrentTarget = project.Targets.FirstOrDefault(x => x.IsPrimary) ?? project.Targets.FirstOrDefault();
        }
        public static ModTarget? CurrentTarget { get; private set; } = null;
        public static string CurrentTargetPath => CurrentTarget?.Path ?? "";
        public static void SwitchTarget(int build)
        {
            CurrentTarget = CurrentProject?.Targets.FirstOrDefault(t => t.Build == build);
        }
        public static void Cleanup()
        {
            CurrentProject = null;
            CurrentTarget = null;
            LoadedProjects.Clear();
        }

        public static ProjectFileNode BuildFileTree(string path, bool root = false)
        {
            var node = new ProjectFileNode
            {
                Name = Path.GetFileName(path),
                Path = path,
                IsFolder = Directory.Exists(path)
            };
            if (root)
                node.Name = CurrentProject?.Name ?? Path.GetFileName(path);

            if (node.IsFolder)
            {
                foreach (var dir in Directory.GetDirectories(path).OrderBy(x => Path.GetFileName(x), StringComparer.OrdinalIgnoreCase))
                    node.Children.Add(BuildFileTree(dir));

                foreach (var file in Directory.GetFiles(path).OrderBy(x => Path.GetFileName(x), StringComparer.OrdinalIgnoreCase))
                    node.Children.Add(BuildFileTree(file));
            }

            return node;
        }

        /// <summary>
        /// Load all projects from the ProjectsRootPath.
        /// Scans for version folders like 41, 42, etc.
        /// </summary>
        public static List<ModProject> LoadProjects()
        {
            LoadedProjects.Clear();

            if (!Directory.Exists(ProjectsRootPath))
                Directory.CreateDirectory(ProjectsRootPath);

            foreach (var projectDir in Directory.GetDirectories(ProjectsRootPath))
            {
                bool hasModInfo = File.Exists(projectDir + Path.DirectorySeparatorChar + "mod.info");
                bool hasWorkshopInfo = File.Exists(projectDir + Path.DirectorySeparatorChar + "workshop.txt");
                var versionDirs = Directory.GetDirectories(projectDir)
                    .Select(path => new
                    {
                        Path = path,
                        Name = Path.GetFileName(path),
                        HasInfo = File.Exists(Path.Combine(path, "mod.info"))
                    })
                    .Where(x => x.HasInfo && double.TryParse(x.Name, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    .ToList();
                var commonPath = Path.Combine(projectDir, "common");
                var hasCommonInfo = File.Exists(Path.Combine(commonPath, "mod.info"));

                if (!hasModInfo && !hasWorkshopInfo && !hasCommonInfo && versionDirs.Count == 0)
                    continue;

                var projectName = Path.GetFileName(projectDir);
                var projectRoot = projectDir;
                if (hasWorkshopInfo && !hasModInfo)
                {
                    var modsRoot = Path.Combine(projectDir, "Contents", "mods");
                    if (!Directory.Exists(modsRoot))
                        continue;
                    projectRoot = Directory.GetDirectories(modsRoot)
                        .FirstOrDefault(d => Path.GetFileName(d).Equals(projectName, StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(d, "mod.info")))
                        ?? Directory.GetDirectories(modsRoot).FirstOrDefault(d => File.Exists(Path.Combine(d, "mod.info")))
                        ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(projectRoot))
                        continue;
                }

                var metadataRoot = versionDirs
                    .OrderByDescending(x => double.Parse(x.Name, CultureInfo.InvariantCulture))
                    .Select(x => x.Path)
                    .FirstOrDefault() ?? (hasCommonInfo ? commonPath : projectRoot);

                var modProject = new ModProject
                {
                    Name = projectName,
                    RootPath = projectRoot,
                    WorkshopRootPath = hasWorkshopInfo ? projectDir : null,
                    ModInfo = ModInfoParser.Load(metadataRoot)
                };

                foreach (var versionDir in versionDirs)
                {
                    if (double.TryParse(versionDir.Name, NumberStyles.Float, CultureInfo.InvariantCulture, out double build))
                    {
                        modProject.Targets.Add(new ModTarget
                        {
                            Build = build,
                            Path = versionDir.Path,
                            IsPrimary = false
                        });
                    }
                }

                if (modProject.Targets.Count > 0)
                {
                    var stableMajor = Math.Truncate(ZomboidGame.latestStableBuild);
                    var primary = modProject.Targets.FirstOrDefault(x => Math.Truncate(x.Build) == stableMajor)
                        ?? modProject.Targets.OrderByDescending(x => x.Build).First();
                    primary.IsPrimary = true;
                }
                else if (hasModInfo)
                {
                    modProject.Targets.Add(new ModTarget
                    {
                        Build = 41,
                        Path = projectRoot,
                        IsPrimary = true,
                        IsLegacyRoot = true
                    });
                }
                else if (hasCommonInfo)
                {
                    modProject.Targets.Add(new ModTarget
                    {
                        Build = ZomboidGame.latestStableBuild,
                        Path = commonPath,
                        IsPrimary = true
                    });
                }

                LoadedProjects.Add(modProject);
            }

            return LoadedProjects;
        }

        /// <summary>
        /// Creates a new project project with a base folder.
        /// </summary>
        public static ModProject CreateProject(string projectName, params string[] supportedBuilds)
        {
            projectName = ValidateProjectName(projectName);

            var projectsRoot = Path.GetFullPath(ProjectsRootPath);
            var projectPath = Path.GetFullPath(Path.Combine(projectsRoot, projectName));
            var rootPrefix = Path.TrimEndingDirectorySeparator(projectsRoot) + Path.DirectorySeparatorChar;
            if (!projectPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Project path must remain inside the configured projects folder.", nameof(projectName));
            if (Directory.Exists(projectPath))
                throw new InvalidOperationException($"Project '{projectName}' already exists.");

            EnsureFolder(projectPath);
            EnsureFolder(projectPath, "common");
            File.WriteAllText(Path.Combine(projectPath, ".gitignore"),
                $".pztools-deploy/{Environment.NewLine}Workshop/{Environment.NewLine}*.log{Environment.NewLine}");

            var stableBuild = ZomboidGame.latestStableBuild;
            var stableFolder = Math.Truncate(stableBuild).ToString(CultureInfo.InvariantCulture);
            var stablePath = Path.Combine(projectPath, stableFolder);
            CreateModFolderStructure(stablePath);

            var newMod = new ModProject
            {
                Name = projectName,
                RootPath = projectPath,
                Targets = new List<ModTarget>()
            };

            newMod.Targets.Add(new ModTarget
            {
                Build = Math.Truncate(stableBuild),
                Path = stablePath,
                IsPrimary = true
            });

            foreach (var buildStr in supportedBuilds)
            {
                if (double.TryParse(buildStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double build))
                {
                    if (Math.Abs(build - Math.Truncate(stableBuild)) < 0.0001)
                        continue;

                    var targetPath = Path.Combine(projectPath, buildStr);
                    CreateModFolderStructure(targetPath);
                    newMod.Targets.Add(new ModTarget
                    {
                        Build = build,
                        Path = targetPath
                    });
                }
            }

            newMod.CreateModInfo();
            LoadedProjects.Add(newMod);
            return newMod;
        }

        public static void CreateModFolderStructure(string modPath, bool foldersOnly = false)
        {
            var mediaPath = Path.Combine(modPath, "media");
            var luaPath = Path.Combine(mediaPath, "lua");


            EnsureFolder(modPath);
            EnsureFolder(mediaPath);
            EnsureFolder(luaPath);
            EnsureFolder(luaPath, "client");
            EnsureFolder(luaPath, "server");
            EnsureFolder(luaPath, "shared");

            EnsureFolder(mediaPath, "scripts");
            EnsureFolder(mediaPath, "ui");
        }

        private static void EnsureFolder(string path, string? name = null)
        {
            if (string.IsNullOrEmpty(name))
            {
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
            }
            else
            {
                var subPath = Path.Combine(path, name);
                if (!Directory.Exists(subPath))
                    Directory.CreateDirectory(subPath);
            }
        }

        public static void CreateModInfo(this ModProject project)
        {
            if (project == null)
                throw new ArgumentNullException(nameof(project));

            var info = BuildDefaultModInfo(project);

            foreach (var target in project.Targets.Where(t => !string.IsNullOrWhiteSpace(t.Path)))
            {
                var infoPath = Path.Combine(target.Path, "mod.info");
                Directory.CreateDirectory(target.Path);

                File.WriteAllLines(infoPath, info.ToModInfoLines());
            }

            project.ModInfo = info;
        }

        public static void UpdateModInfo(this ModProject project, ModInfo newInfo)
        {
            if (project == null)
                throw new ArgumentNullException(nameof(project));
            if (newInfo == null)
                throw new ArgumentNullException(nameof(newInfo));

            foreach (var target in project.Targets.Where(t => !string.IsNullOrWhiteSpace(t.Path)))
            {
                var infoPath = Path.Combine(target.Path, "mod.info");
                Directory.CreateDirectory(target.Path);
                SyncMetadataAssets(project, target.Path, newInfo);
                File.WriteAllLines(infoPath, newInfo.ToModInfoLines());
            }

            project.ModInfo = newInfo;
        }

        private static void SyncMetadataAssets(ModProject project, string destination, ModInfo info)
        {
            foreach (var asset in info.Posters.Append(info.Icon).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>())
            {
                var destinationPath = Path.Combine(destination, asset);
                if (File.Exists(destinationPath))
                    continue;

                var source = project.Targets
                    .Select(x => Path.Combine(x.Path, asset))
                    .Append(Path.Combine(project.RootPath, "common", asset))
                    .Append(Path.Combine(project.RootPath, asset))
                    .FirstOrDefault(File.Exists);
                if (source == null)
                    continue;

                var destinationDirectory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                    Directory.CreateDirectory(destinationDirectory);
                File.Copy(source, destinationPath, overwrite: true);
            }
        }

        private static ModInfo BuildDefaultModInfo(ModProject project)
        {
            var id = ModInfoUtil.NormalizeModId(project.Name);
            if (string.IsNullOrWhiteSpace(id))
                id = "MyMod";

            var info = new ModInfo
            {
                Name = project.Name,
                Id = id,
                Description = "A project created using PZTools"
            };

            return info;
        }

        /// <summary>
        /// Adds a versioned target to an existing project.
        /// Creates the folder structure.
        /// </summary>
        public static ModTarget AddTarget(ModProject project, double build, ModTarget? copyFrom = null)
        {
            if (project == null)
                throw new ArgumentNullException(nameof(project));

            if (!double.IsFinite(build) || build <= 0)
                throw new ArgumentOutOfRangeException(nameof(build), "Build target must be a positive number.");

            if (project.Targets.Exists(t => t.Build == build))
                throw new InvalidOperationException($"Build {build} already exists for project {project.Name}.");

            var targetPath = Path.Combine(project.RootPath, build.ToString("0.################", CultureInfo.InvariantCulture));
            if (Directory.Exists(targetPath) || File.Exists(targetPath))
                throw new InvalidOperationException($"The target path already exists and was left unchanged: {targetPath}");
            var stagingPath = Path.Combine(project.RootPath, ".pztools-target-" + Guid.NewGuid().ToString("N"));
            try
            {
                if (copyFrom != null)
                {
                    if (!project.Targets.Contains(copyFrom) || !Directory.Exists(copyFrom.Path))
                        throw new InvalidOperationException("The selected source build is not available.");
                    CopyTargetDirectory(copyFrom.Path, stagingPath, copyFrom.IsLegacyRoot ? project : null, isRoot: true);
                }
                else
                {
                    CreateModFolderStructure(stagingPath);
                }

                var info = project.ModInfo.Name.Length > 0 ? project.ModInfo : BuildDefaultModInfo(project);
                ModInfoParser.Save(stagingPath, info);
                Directory.Move(stagingPath, targetPath);
            }
            catch
            {
                if (Directory.Exists(stagingPath))
                    Directory.Delete(stagingPath, recursive: true);
                throw;
            }

            var newTarget = new ModTarget
            {
                Build = build,
                Path = targetPath,
                IsPrimary = false
            };

            project.Targets.Add(newTarget);
            return newTarget;
        }

        private static void CopyTargetDirectory(string source, string destination, ModProject? legacyProject, bool isRoot)
        {
            if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("A build target cannot be copied from a symbolic link or reparse point.");

            // Snapshot before creating the destination. A legacy target can be the project root,
            // which means its new numeric target folder is a child of the source directory.
            var sourceDirectories = Directory.EnumerateDirectories(source).ToList();
            var sourceFiles = Directory.EnumerateFiles(source).ToList();
            Directory.CreateDirectory(destination);
            foreach (var directory in sourceDirectories)
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    continue;
                if (isRoot && legacyProject != null && ShouldSkipLegacyProjectDirectory(legacyProject, directory))
                    continue;
                CopyTargetDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)), legacyProject: null, isRoot: false);
            }
            foreach (var file in sourceFiles)
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    continue;
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: false);
            }
        }

        private static bool ShouldSkipLegacyProjectDirectory(ModProject project, string directory)
        {
            var name = Path.GetFileName(directory);
            if (name.Equals("common", StringComparison.OrdinalIgnoreCase) ||
                name.Equals(".pztools", StringComparison.OrdinalIgnoreCase) ||
                name.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                name.Equals(".vscode", StringComparison.OrdinalIgnoreCase) ||
                name.Equals(".vs", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Workshop", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("obj", StringComparison.OrdinalIgnoreCase))
                return true;

            var full = Path.GetFullPath(directory);
            return project.Targets.Any(x => !x.IsLegacyRoot &&
                string.Equals(Path.GetFullPath(x.Path), full, StringComparison.OrdinalIgnoreCase));
        }

        public static string ValidateProjectName(string projectName)
        {
            if (string.IsNullOrWhiteSpace(projectName))
                throw new ArgumentException("Project name cannot be empty.", nameof(projectName));

            var value = projectName.Trim();
            if (value is "." or ".." ||
                value.EndsWith('.') ||
                value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                value.Contains(Path.DirectorySeparatorChar) ||
                value.Contains(Path.AltDirectorySeparatorChar))
                throw new ArgumentException("Project name contains characters that cannot be used in a folder name.", nameof(projectName));

            if (ReservedWindowsNames.Contains(Path.GetFileNameWithoutExtension(value)))
                throw new ArgumentException("Project name is reserved by Windows. Choose another name.", nameof(projectName));

            return value;
        }

        /// <summary>
        /// Get a project by name.
        /// </summary>
        public static ModProject? GetProject(string modName)
        {
            return LoadedProjects.Find(m => m.Name.Equals(modName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Get all loaded projects.
        /// </summary>
        public static List<ModProject> GetAllProjects() => new(LoadedProjects);
    }
}
