using PZTools.Core.Functions.Zomboid;
using PZTools.Core.Models;
using System.IO;

namespace PZTools.Core.Functions.Projects
{
    public static class ProjectEngine
    {
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
                node.Name = ProjectEngine.CurrentProject.Name;

            if (node.IsFolder)
            {
                foreach (var dir in Directory.GetDirectories(path))
                    node.Children.Add(BuildFileTree(dir));

                foreach (var file in Directory.GetFiles(path))
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

                if (!hasModInfo && !hasWorkshopInfo) continue;

                var projectName = Path.GetFileName(projectDir);
                var modProject = new ModProject
                {
                    Name = projectName,
                    RootPath = projectDir
                };

                modProject.Targets.Add(new ModTarget
                {
                    Build = ZomboidGame.latestStableBuild,
                    Path = projectDir
                });

                string projectRoot = projectDir;

                if (hasWorkshopInfo)
                {
                    projectRoot = Path.Combine(projectDir, "Contents", "mods", projectName);
                }

                foreach (var versionDir in Directory.GetDirectories(projectDir))
                {
                    var folderName = Path.GetFileName(versionDir);
                    if (double.TryParse(folderName, out double build))
                    {
                        modProject.Targets.Add(new ModTarget
                        {
                            Build = build,
                            Path = versionDir
                        });
                    }
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
            if (string.IsNullOrWhiteSpace(projectName))
                throw new ArgumentException("Project name cannot be empty.", nameof(projectName));

            var projectPath = Path.Combine(ProjectsRootPath, projectName);
            if (Directory.Exists(projectPath))
                throw new InvalidOperationException($"Project '{projectName}' already exists.");

            CreateModFolderStructure(projectPath);

            var newMod = new ModProject
            {
                Name = projectName,
                RootPath = projectPath,
                Targets = new List<ModTarget>()
            };

            newMod.Targets.Add(new ModTarget
            {
                Build = ZomboidGame.latestStableBuild,
                Path = projectPath
            });

            foreach (var buildStr in supportedBuilds)
            {
                if (double.TryParse(buildStr, out double build))
                {
                    if (build == ZomboidGame.latestStableBuild)
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
            EnsureFolder(modPath, "common");

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
            if (project == null) throw new ArgumentNullException(nameof(project));

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
            if (project == null) throw new ArgumentNullException(nameof(project));
            if (newInfo == null) throw new ArgumentNullException(nameof(newInfo));

            foreach (var target in project.Targets.Where(t => !string.IsNullOrWhiteSpace(t.Path)))
            {
                var infoPath = Path.Combine(target.Path, "mod.info");
                if (!File.Exists(infoPath))
                    continue;
                File.WriteAllLines(infoPath, newInfo.ToModInfoLines());
            }

            project.ModInfo = newInfo;
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
        public static ModTarget AddTarget(ModProject project, int build)
        {
            if (project == null)
                throw new ArgumentNullException(nameof(project));

            if (project.Targets.Exists(t => t.Build == build))
                throw new InvalidOperationException($"Build {build} already exists for project {project.Name}.");

            var targetPath = Path.Combine(project.RootPath, build.ToString());
            Directory.CreateDirectory(targetPath);

            var newTarget = new ModTarget
            {
                Build = build,
                Path = targetPath
            };

            project.Targets.Add(newTarget);
            return newTarget;
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
