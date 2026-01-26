using PZTools.Core.Functions.Projects;
using PZTools.Core.Functions.Zomboid;
using System.Collections.ObjectModel;
using System.IO;

namespace PZTools.Core.Models
{
    public class ModProject
    {
        public string Name { get; set; } = "";
        public string RootPath { get; set; } = "";
        public List<ModTarget> Targets { get; set; } = new();
        public ModInfo ModInfo { get; set; } = new ModInfo();

        public List<BuildWrapper> VersionWrappers => new List<BuildWrapper>
        {
            new BuildWrapper { Builds = Targets }
        };

        public override string ToString() => Name;
    }

    public enum DeployFolder
    {
        Mods,
        Workshop
    }

    public class BuildWrapper
    {
        public string Label { get; set; } = "Supported Versions:";
        public List<ModTarget> Builds { get; set; } = new();
    }

    public class ModTarget
    {
        public double Build { get; set; }
        public string BuildName => GetBuildName();
        public string Path { get; set; } = "";
        public ProjectFileNode? FileTree { get; set; }

        public string GetBuildName()
        {
            if (Build == ZomboidGame.latestStableBuild) return ProjectEngine.CurrentProject.Name;
            else return $"Build: {Build}";
        }

        public void LoadFiles()
        {
            if (Directory.Exists(Path))
                FileTree = ProjectEngine.BuildFileTree(Path, Build == ZomboidGame.latestStableBuild);
        }
    }

    public class ProjectFileNode
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public bool IsFolder { get; set; }
        public ObservableCollection<ProjectFileNode> Children { get; set; } = new();

        public override string ToString() => Name;
    }
}
