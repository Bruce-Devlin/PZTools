using PZTools.Core.Functions.Projects;
using System.Collections.ObjectModel;
using System.IO;

namespace PZTools.Core.Models
{
    public class ModProject
    {
        public string Name { get; set; } = "";
        public string RootPath { get; set; } = "";
        public List<ModTarget> Targets { get; set; } = new();

        public List<BuildWrapper> VersionWrappers => new List<BuildWrapper>
        {
            new BuildWrapper { Builds = Targets }
        };

        public override string ToString() => Name;
    }

    public class BuildWrapper
    {
        public string Label { get; set; } = "Supported Versions:";
        public List<ModTarget> Builds { get; set; } = new();
    }

    public class ModTarget
    {
        public double Build { get; set; }
        public string Path { get; set; } = "";
        public ProjectFileNode? FileTree { get; set; }

        public void LoadFiles()
        {
            if (Directory.Exists(Path))
                FileTree = ProjectEngine.BuildFileTree(Path, Build == 0);
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
