using PZTools.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PZTools.Core.Functions.Projects
{
    public static class ProjectEngine
    {
        /// <summary>
        /// The root directory where all mods are stored.
        /// </summary>
        public static string ModsRootPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Zomboid", "mods");

        private static readonly List<ModProject> LoadedMods = new();
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
            LoadedMods.Clear();
        }

        public static ProjectFileNode BuildFileTree(string path, bool root = false)
        {
            var node = new ProjectFileNode
            {
                Name = Path.GetFileName(path),
                Path = path,
                IsFolder = Directory.Exists(path)
            };
            if (root) node.Name = ProjectEngine.CurrentProject.Name;

            if (node.IsFolder)
            {
                foreach (var dir  in Directory.GetDirectories(path))
                    node.Children.Add(BuildFileTree(dir));

                foreach (var file in Directory.GetFiles(path))
                    node.Children.Add(BuildFileTree(file));
            }

            return node;
        }

        /// <summary>
        /// Load all mods from the ModsRootPath.
        /// Scans for version folders like 41, 42, etc.
        /// </summary>
        public static List<ModProject> LoadMods()
        {
            LoadedMods.Clear();

            if (!Directory.Exists(ModsRootPath))
                Directory.CreateDirectory(ModsRootPath);

            foreach (var modDir in Directory.GetDirectories(ModsRootPath))
            {
                bool hasModInfo = File.Exists(modDir + Path.DirectorySeparatorChar + "mod.info");
                bool hasWorkshopInfo = File.Exists(modDir + Path.DirectorySeparatorChar + "workshop.txt");

                if (!hasModInfo && !hasWorkshopInfo) continue;

                var modName = Path.GetFileName(modDir);
                var modProject = new ModProject
                {
                    Name = modName,
                    RootPath = modDir
                };

                modProject.Targets.Add(new ModTarget
                {
                    Build = 41,
                    Path = modDir
                });

                string modRoot = modDir;

                if (hasWorkshopInfo)
                {
                    modRoot = Path.Combine(modDir, "Contents", "mods", modName);
                }

                

                // Look for versioned folders (numeric folder names)
                foreach (var versionDir in Directory.GetDirectories(modRoot))
                {
                    var folderName = Path.GetFileName(versionDir);
                    if (int.TryParse(folderName, out int build))
                    {
                        modProject.Targets.Add(new ModTarget
                        {
                            Build = build,
                            Path = versionDir
                        });
                    }
                }

                LoadedMods.Add(modProject);
            }

            return LoadedMods;
        }

        /// <summary>
        /// Creates a new mod project with a base folder.
        /// </summary>
        public static ModProject CreateMod(string modName)
        {
            if (string.IsNullOrWhiteSpace(modName))
                throw new ArgumentException("Mod name cannot be empty.", nameof(modName));

            var modPath = Path.Combine(ModsRootPath, modName);
            if (Directory.Exists(modPath))
                throw new InvalidOperationException($"Mod '{modName}' already exists.");

            Directory.CreateDirectory(modPath);

            var newMod = new ModProject
            {
                Name = modName,
                RootPath = modPath,
                Targets = new List<ModTarget>()
            };

            LoadedMods.Add(newMod);

            return newMod;
        }

        /// <summary>
        /// Adds a versioned target to an existing mod.
        /// Creates the folder structure.
        /// </summary>
        public static ModTarget AddTarget(ModProject mod, int build)
        {
            if (mod == null)
                throw new ArgumentNullException(nameof(mod));

            if (mod.Targets.Exists(t => t.Build == build))
                throw new InvalidOperationException($"Build {build} already exists for mod {mod.Name}.");

            var targetPath = Path.Combine(mod.RootPath, build.ToString());
            Directory.CreateDirectory(targetPath);

            var newTarget = new ModTarget
            {
                Build = build,
                Path = targetPath
            };

            mod.Targets.Add(newTarget);
            return newTarget;
        }

        /// <summary>
        /// Get a mod by name.
        /// </summary>
        public static ModProject? GetMod(string modName)
        {
            return LoadedMods.Find(m => m.Name.Equals(modName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Get all loaded mods.
        /// </summary>
        public static List<ModProject> GetAllMods() => new(LoadedMods);
    }
}
