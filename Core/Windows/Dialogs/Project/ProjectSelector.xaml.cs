using PZTools.Core.Functions;
using PZTools.Core.Functions.Projects;
using PZTools.Core.Models;
using PZTools.Core.Models.InputDialog;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using TreeView = System.Windows.Controls.TreeView;


namespace PZTools.Core.Windows.Dialogs.Project
{
    public partial class ProjectSelector : Window
    {
        public ObservableCollection<ModProject> Projects { get; set; } = new();

        public ModProject? SelectedProject { get; private set; }
        public ModTarget? SelectedTarget { get; private set; }


        public ProjectSelector()
        {
            InitializeComponent();

            var loaded = ProjectEngine.LoadProjects();
            foreach (var project in loaded)
                Projects.Add(project);

            ProjectFolderTxt.Text = "Projects Folder: " + ProjectEngine.ProjectsRootPath;

            ProjectsTreeView.ItemsSource = Projects;

            ProjectsTreeView.SelectedItemChanged += ProjectsTreeView_SelectedItemChanged;
        }

        private void ProjectsTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (ProjectsTreeView.SelectedItem is ModTarget target)
            {
                SelectedTarget = target;
                foreach (var project in Projects)
                {
                    if (project.Targets.Contains(target))
                    {
                        SelectedProject = project;
                        break;
                    }
                }
            }
            else if (ProjectsTreeView.SelectedItem is ModProject project)
            {
                SelectedProject = project;
                SelectedTarget = null;
            }
        }

        private void SelectTreeViewItem(TreeView treeView, object itemToSelect)
        {
            if (itemToSelect == null) return;

            TreeViewItem? treeViewItem = GetTreeViewItem(treeView, itemToSelect);
            if (treeViewItem != null)
            {
                treeViewItem.IsSelected = true;
                treeViewItem.BringIntoView();
            }
        }

        private TreeViewItem? GetTreeViewItem(ItemsControl container, object item)
        {
            if (container == null) return null;

            for (int i = 0; i < container.Items.Count; i++)
            {
                var currentItem = container.Items[i];

                TreeViewItem? treeViewItem = container.ItemContainerGenerator.ContainerFromItem(currentItem) as TreeViewItem;
                if (treeViewItem == null) continue;

                if (currentItem == item)
                    return treeViewItem;

                TreeViewItem? child = GetTreeViewItem(treeViewItem, item);
                if (child != null) return child;
            }

            return null;
        }

        private bool ValidateInputResponses(InputDialogs inputDialogs, string projectName, string targetBuild)
        {
            if (string.IsNullOrWhiteSpace(projectName))
            {
                MessageBox.Show("Project name cannot be empty.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            projectName = projectName.Trim();

            if (string.IsNullOrWhiteSpace(targetBuild))
            {
                MessageBox.Show("Target build cannot be empty.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            targetBuild = targetBuild.Trim();

            var invalidChars = System.IO.Path.GetInvalidFileNameChars();
            if (projectName.IndexOfAny(invalidChars) >= 0)
            {
                MessageBox.Show("Project name contains invalid characters.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (targetBuild.IndexOfAny(invalidChars) >= 0)
            {
                MessageBox.Show("Target build contains invalid characters.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (targetBuild != string.Empty && !double.TryParse(targetBuild, out _))
            {
                MessageBox.Show("Target build must be a valid version number.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        private void NewProjectButton_Click(object sender, RoutedEventArgs e)
        {
            const string projectNameKey = "projectName";
            const string targetBuildKey = "targetBuild";
            var fields = new[]
            {
                new InputFieldDefinition
                {
                    Key = projectNameKey,
                    Label = "Project Name",
                    IsRequired = true,
                    DefaultValue = string.Empty
                },
                new InputFieldDefinition
                {
                    Key = targetBuildKey,
                    Label = "Target Build",
                    IsRequired = true,
                    DefaultValue = string.Empty
                }
            };

            var inputDialogs = new InputDialogs("Enter new project name:", fields, "New Project");
            if (inputDialogs.ShowDialog() == true)
            {
                string projectName = inputDialogs.TryGetResponse(projectNameKey);
                string targetBuild = inputDialogs.TryGetResponse(targetBuildKey);

                bool isValid = ValidateInputResponses(inputDialogs, projectName, targetBuild);

                if (!isValid) return;

                try
                {
                    var newProject = ProjectEngine.CreateProject(projectName, targetBuild);
                    ProjectEngine.LoadProjects();
                    Projects.Add(newProject);
                    ProjectsTreeView.Items.Refresh();

                    SelectedProject = newProject;
                    SelectedTarget = null;
                    SelectTreeViewItem(ProjectsTreeView, newProject);

                    MessageBox.Show($"Project '{projectName}' created successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to create project: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedProject == null)
            {
                System.Windows.MessageBox.Show("Please select a project.", "Select Project", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ProjectEngine.LoadProject(SelectedProject);

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void OpenProjectFolderBtn_Click(object sender, RoutedEventArgs e)
        {
            WindowsHelpers.OpenFile(ProjectEngine.ProjectsRootPath);
        }
    }
}
