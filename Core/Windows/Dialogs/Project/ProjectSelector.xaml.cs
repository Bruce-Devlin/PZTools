using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using PZTools.Core.Functions;
using PZTools.Core.Functions.Projects;
using PZTools.Core.Models;
using PZTools.Core.Models.InputDialog;
using Forms = System.Windows.Forms;
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
            this.FreeDragThisWindow();

            var loaded = ProjectEngine.LoadProjects();
            foreach (var project in loaded)
                Projects.Add(project);

            ProjectFolderTxt.Text = ProjectEngine.ProjectsRootPath;

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
            if (itemToSelect == null)
                return;

            TreeViewItem? treeViewItem = GetTreeViewItem(treeView, itemToSelect);
            if (treeViewItem != null)
            {
                treeViewItem.IsSelected = true;
                treeViewItem.BringIntoView();
            }
        }

        private TreeViewItem? GetTreeViewItem(ItemsControl container, object item)
        {
            if (container == null)
                return null;

            for (int i = 0; i < container.Items.Count; i++)
            {
                var currentItem = container.Items[i];

                TreeViewItem? treeViewItem = container.ItemContainerGenerator.ContainerFromItem(currentItem) as TreeViewItem;
                if (treeViewItem == null)
                    continue;

                if (currentItem == item)
                    return treeViewItem;

                TreeViewItem? child = GetTreeViewItem(treeViewItem, item);
                if (child != null)
                    return child;
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

            if (targetBuild != string.Empty && !double.TryParse(targetBuild, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
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
                    Label = "Additional Legacy Build (optional)",
                    Description = "New projects target the current stable Build 42 family. Add 41 here for a compatibility target.",
                    IsRequired = false,
                    DefaultValue = "41"
                }
            };

            var inputDialogs = new InputDialogs("Enter new project name:", fields, "New Project");
            if (inputDialogs.ShowDialog() == true)
            {
                string projectName = inputDialogs.TryGetResponse(projectNameKey);
                string targetBuild = inputDialogs.TryGetResponse(targetBuildKey);

                bool isValid = ValidateInputResponses(inputDialogs, projectName, targetBuild);

                if (!isValid)
                    return;

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

        private async void ImportModButton_Click(object sender, RoutedEventArgs e)
        {
            using var folderPicker = new Forms.FolderBrowserDialog
            {
                Description = "Select the folder containing the existing Project Zomboid mod",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false
            };

            if (folderPicker.ShowDialog() != Forms.DialogResult.OK)
                return;

            try
            {
                var inspection = ModImportService.Inspect(folderPicker.SelectedPath);
                const string projectNameKey = "projectName";
                var input = new InputDialogs(
                    "Confirm the workspace project name. The source folder will not be changed.",
                    new[]
                    {
                        new InputFieldDefinition
                        {
                            Key = projectNameKey,
                            Label = "Project Name",
                            Description = "The imported copy will be stored under the PZTools Projects folder.",
                            IsRequired = true,
                            DefaultValue = inspection.SuggestedProjectName
                        }
                    },
                    "Import Existing Mod");

                if (input.ShowDialog() != true)
                    return;

                var result = ModImportService.Import(folderPicker.SelectedPath, input.TryGetResponse(projectNameKey));
                Projects.Clear();
                foreach (var project in ProjectEngine.GetAllProjects())
                    Projects.Add(project);

                SelectedProject = result.Project;
                SelectedTarget = null;
                ProjectsTreeView.Items.Refresh();
                SelectTreeViewItem(ProjectsTreeView, result.Project);

                var details = result.Issues.Count == 0
                    ? "No layout problems were detected."
                    : string.Join(Environment.NewLine, result.Issues.Take(12).Select(x =>
                        $"{(x.Severity == ModImportIssueSeverity.Warning ? "Warning" : "Adjusted")}: {x.Message}"));
                if (result.Issues.Count > 12)
                    details += $"{Environment.NewLine}…and {result.Issues.Count - 12} more. Open Project Health for the full project validation.";

                var showWarning = result.WarningCount > 0;
                string healthDetails;
                try
                {
                    var health = await ProjectHealthService.AnalyzeAsync(result.Project, validateLua: true);
                    var healthFindings = health.Diagnostics
                        .Where(x => x.Severity != DiagnosticSeverity.Info)
                        .Take(6)
                        .Select(x => $"{x.SeverityIcon} {x.Code}: {x.Message}")
                        .ToList();
                    healthDetails = healthFindings.Count == 0
                        ? "Project Health: no errors or warnings."
                        : $"Project Health: {health.Summary}{Environment.NewLine}" + string.Join(Environment.NewLine, healthFindings);
                    if (health.ErrorCount + health.WarningCount > healthFindings.Count)
                        healthDetails += $"{Environment.NewLine}…and {health.ErrorCount + health.WarningCount - healthFindings.Count} more finding(s).";
                    showWarning |= health.ErrorCount > 0 || health.WarningCount > 0;
                }
                catch (Exception healthException)
                {
                    healthDetails = $"Project Health could not finish: {healthException.Message}";
                    showWarning = true;
                }

                MessageBox.Show(
                    $"Imported '{result.Project.Name}' with {result.RepairCount} adjustment(s) and {result.WarningCount} warning(s)." +
                    Environment.NewLine + Environment.NewLine + details + Environment.NewLine + Environment.NewLine + healthDetails + Environment.NewLine + Environment.NewLine +
                    "Open Project Health after opening the project to review its files and metadata.",
                    "Mod Import Complete", MessageBoxButton.OK,
                    showWarning ? MessageBoxImage.Warning : MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"The mod could not be imported: {ex.Message}", "Import Existing Mod",
                    MessageBoxButton.OK, MessageBoxImage.Error);
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
