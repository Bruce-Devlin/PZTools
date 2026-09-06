using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using PZTools.Core.Functions;
using PZTools.Core.Functions.Projects;
using PZTools.Core.Functions.Tester;
using PZTools.Core.Functions.Undo;
using PZTools.Core.Models;
using PZTools.Core.Models.InputDialog;
using PZTools.Core.Windows.Dialogs.Project;

namespace PZTools.Core.Windows
{
    public partial class MainWindow
    {
        private async void Context_NewFile_Click(object sender, RoutedEventArgs e)
        {
            var node = GetProjectFileNode(sender);
            if (node == null || !node.IsFolder)
                return;

            var fields = new[]
            {
                new InputFieldDefinition
                {
                    Key = "name",
                    Label = "File name (without extension)",
                    Placeholder = "example",
                    IsRequired = true,
                    Validator = s => !string.IsNullOrWhiteSpace(s) && s.IndexOfAny(Path.GetInvalidFileNameChars()) < 0,
                    ValidationMessage = "Provide a valid file name"
                },
                new InputFieldDefinition
                {
                    Key = "ext",
                    Label = "Extension (include dot or not)",
                    DefaultValue = ".lua",
                    IsRequired = true,
                    Validator = s => !string.IsNullOrWhiteSpace(s),
                    ValidationMessage = "Provide an extension"
                }
            };

            var dlg = new InputDialogs("Create new file", fields, "New File");
            if (dlg.ShowDialog() != true)
                return;

            var name = dlg.TryGetResponse("name");
            var ext = dlg.TryGetResponse("ext");
            if (!ext.StartsWith('.'))
                ext = $".{ext}";

            var fullName = name + ext;
            var targetPath = Path.Combine(node.Path, fullName);

            try
            {
                if (File.Exists(targetPath))
                {
                    MessageBox.Show("File already exists.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                var initialContent = string.Empty;

                if (ext.Equals(".lua", StringComparison.OrdinalIgnoreCase))
                {
                    initialContent = "-- A New Lua file\r\n";
                    var watermark = Config.GetVariable(VariableType.user, $"{ModProject.Name}-watermark");
                    if (!string.IsNullOrEmpty(watermark))
                    {
                        initialContent = watermark;
                    }
                }
                else if (ext.Equals(".info", StringComparison.OrdinalIgnoreCase))
                {
                    initialContent = $"name={name}\r\nid={name.Replace(" ", "")}\r\ndescription=Created by PZTools\r\n";
                }

                var cmd = new FileCreateCommand(targetPath, initialContent);
                await UndoRedoManager.Instance.ExecuteAsync(cmd);

                var child = new ProjectFileNode
                {
                    Name = fullName,
                    Path = targetPath,
                    IsFolder = false
                };

                node.Children.Add(child);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to create file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void Context_NewFolder_Click(object sender, RoutedEventArgs e)
        {
            var node = GetProjectFileNode(sender);
            if (node == null || !node.IsFolder)
                return;

            var fields = new[]
            {
                new InputFieldDefinition
                {
                    Key = "name",
                    Label = "Folder name",
                    Placeholder = "example",
                    IsRequired = true,
                    Validator = s => !string.IsNullOrWhiteSpace(s) && s.IndexOfAny(Path.GetInvalidFileNameChars()) < 0,
                    ValidationMessage = "Provide a valid folder name"
                }
            };

            var dlg = new InputDialogs("Create new folder", fields, "New Folder");
            if (dlg.ShowDialog() != true)
                return;

            var name = dlg.TryGetResponse("name");

            var targetPath = Path.Combine(node.Path, name);

            try
            {
                if (Directory.Exists(targetPath))
                {
                    MessageBox.Show("Folder already exists.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }


                var cmd = new FileCreateCommand(targetPath);
                await UndoRedoManager.Instance.ExecuteAsync(cmd);

                var child = new ProjectFileNode
                {
                    Name = name,
                    Path = targetPath,
                    IsFolder = true
                };

                node.Children.Add(child);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to create folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Context_AddTarget_Click(object sender, RoutedEventArgs e)
        {
            var fields = new[]
            {
                new InputFieldDefinition
                {
                    Key = "newTargetBuild",
                    Label = "New Target Build",
                    IsRequired = true
                }
            };

            var inputDialogs = new InputDialogs("Enter new Target Build:", fields, "Add Target Build");
            if (inputDialogs.ShowDialog() == true)
            {
                var newTargetName = inputDialogs.TryGetResponse("newTargetBuild");
                if (!double.TryParse(newTargetName, NumberStyles.Float, CultureInfo.InvariantCulture, out var newBuild) ||
                    !double.IsFinite(newBuild) || newBuild <= 0)
                {
                    MessageBox.Show("Invalid build target!", "Invalid Build Target", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (ModProject.Targets.Any(x => x.Build == newBuild))
                {
                    MessageBox.Show($"Build {newBuild} is already supported within this project.", "Already targeting build!", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var previousTarget = ModProject.Targets
                    .Where(x => x.Build < newBuild)
                    .OrderByDescending(x => x.Build)
                    .FirstOrDefault()
                    ?? ModProject.Targets.OrderBy(x => Math.Abs(x.Build - newBuild)).FirstOrDefault();

                ModTarget? copyFrom = null;
                if (previousTarget != null)
                {
                    var copyChoice = MessageBox.Show(
                        $"Copy everything from Build {previousTarget.Build:0.##} into the new Build {newBuild:0.##} package?\n\n" +
                        "Yes copies the complete previous package before it is added. No creates the usual empty target.",
                        "Copy Previous Version",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Question);
                    if (copyChoice == MessageBoxResult.Cancel)
                        return;
                    if (copyChoice == MessageBoxResult.Yes)
                        copyFrom = previousTarget;
                }

                try
                {
                    var modTarget = ProjectEngine.AddTarget(ModProject, newBuild, copyFrom);
                    var syncResult = VersionSyncService.ReconcileAll(ModProject);
                    modTarget.LoadFiles();
                    UpdateTreeView();

                    var copySummary = copyFrom == null
                        ? "Created an empty version package."
                        : $"Copied the complete Build {copyFrom.Build:0.##} package.";
                    var syncSummary = syncResult.Copied + syncResult.Updated + syncResult.FoldersCreated > 0
                        ? $"\nApplied {syncResult.Copied + syncResult.Updated} existing synced file(s) and {syncResult.FoldersCreated} folder(s)."
                        : string.Empty;
                    var conflictSummary = syncResult.Conflicts > 0
                        ? $"\nPreserved {syncResult.Conflicts} conflicting item(s); no differing files were overwritten."
                        : string.Empty;
                    MessageBox.Show(
                        $"Build {newBuild:0.##} was added. {copySummary}{syncSummary}{conflictSummary}",
                        "Target Added",
                        MessageBoxButton.OK,
                        syncResult.Conflicts > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to add Build {newBuild:0.##}: {ex.Message}", "Add Target", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void Context_EnableVersionSync_Click(object sender, RoutedEventArgs e)
        {
            var node = GetProjectFileNode(sender);
            if (node == null)
                return;

            try
            {
                var result = await Task.Run(() => VersionSyncService.Enable(ModProject, node.Path));
                RefreshVersionSyncTree();
                ShowVersionSyncResult($"'{node.Name}' is now synced across game versions.", result);
                RefreshInspector();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not enable version sync: {ex.Message}", "Version Sync", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Context_DisableVersionSync_Click(object sender, RoutedEventArgs e)
        {
            var node = GetProjectFileNode(sender);
            if (node == null)
                return;

            try
            {
                if (VersionSyncService.Disable(ModProject, node.Path))
                    MessageBox.Show($"'{node.Name}' is no longer synced across game versions. Existing copies were kept.", "Version Sync", MessageBoxButton.OK, MessageBoxImage.Information);
                RefreshInspector();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not disable version sync: {ex.Message}", "Version Sync", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void ShowVersionSyncResult(string heading, VersionSyncResult result)
        {
            var details = $"Created {result.FoldersCreated} missing folder(s), copied {result.Copied} missing file(s), and updated {result.Updated} unchanged counterpart(s).";
            if (result.Conflicts > 0)
            {
                details += $"\n\nPreserved {result.Conflicts} differing file(s) without overwriting them:" +
                    Environment.NewLine + string.Join(Environment.NewLine, result.ConflictPaths.Distinct(StringComparer.OrdinalIgnoreCase).Take(8));
                if (result.ConflictPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count() > 8)
                    details += "\n…and more.";
            }
            MessageBox.Show(
                heading + "\n\n" + details,
                "Version Sync",
                MessageBoxButton.OK,
                result.Conflicts > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }

        private async void Context_RemoveTarget_Click(object sender, RoutedEventArgs e)
        {
            var node = (sender as FrameworkElement)?.DataContext as ModTarget;
            if (node == null)
                return;

            if (node.IsPrimary)
            {
                MessageBox.Show("You cannot remove the primary build target from the project.", "Remove Target", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"Remove Build {node.Build:0.##}?\n\nThis permanently deletes the target's project files.",
                "Remove Target",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {

                if (Directory.Exists(node.Path))
                {
                    var cmd = new TargetDeleteCommand(node.Build);
                    await UndoRedoManager.Instance.ExecuteAsync(cmd);
                }
            }


        }

        private async void Context_TestFile_Click(object sender, RoutedEventArgs e)
        {
            var node = GetProjectFileNode(sender);
            if (node == null)
                return;

            await LuaTester.TestFile(node.Path);
        }

        private void Context_Show_Click(object sender, RoutedEventArgs e)
        {
            var node = GetProjectFileNode(sender);
            if (node == null)
                return;

            WindowsHelpers.ShowInExplorer(node.Path);
        }

        private void Context_EditFile_Click(object sender, RoutedEventArgs e)
        {
            var node = GetProjectFileNode(sender);
            if (node == null)
                return;

            var defaultApp = Config.GetAppSetting<string>("DefaultFileEditorApp");
            if (defaultApp == null)
                defaultApp = "";

            var defaultAppArgs = Config.GetAppSetting<string>("DefaultFileEditorArgs");
            if (defaultAppArgs == null)
                defaultAppArgs = "";

            var error = WindowsHelpers.OpenFile(node.Path, defaultApp, defaultAppArgs);
            if (!string.IsNullOrWhiteSpace(error))
                MessageBox.Show($"Could not open the file: {error}", "Edit File", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private async void Context_RenameFile_Click(object sender, RoutedEventArgs e)
        {
            var node = GetProjectFileNode(sender);
            if (node == null)
                return;

            var fields = new[]
            {
                new InputFieldDefinition
                {
                    Key = "newFileName",
                    Label = "File Name",
                    IsRequired = true,
                    DefaultValue = new FileInfo(node.Path).Name
                }
            };

            var inputDialogs = new InputDialogs("Enter new file name:", fields, "Rename File");
            if (inputDialogs.ShowDialog() == true)
            {
                string newFileName = inputDialogs.TryGetResponse("newFileName");
                string? parentDir = Directory.GetParent(node.Path)?.FullName;
                if (string.IsNullOrWhiteSpace(parentDir))
                    return;
                string newPath = Path.Combine(parentDir, newFileName);

                if (File.Exists(newPath) || Directory.Exists(newPath))
                {
                    MessageBox.Show("A file or folder with that name already exists.", "Rename File",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                try
                {
                    var cmd = new FileMoveCommand(node.Path, newPath);
                    await UndoRedoManager.Instance.ExecuteAsync(cmd);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to rename file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }

                var parentNode = FindNodeByPath(parentDir);
                if (parentNode != null)
                {
                    RemoveProjectTreeNode(node);
                    node.Name = newFileName;
                    node.Path = newPath;
                    UpdateProjectTreeNodeParent(node, parentNode);
                }
            }
        }

        private async void Context_Delete_Click(object sender, RoutedEventArgs e)
        {
            var node = GetProjectFileNode(sender);
            if (node == null)
                return;

            var confirm = MessageBox.Show($"Are you sure you want to delete '{node.Name}'?", "Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
                return;

            try
            {
                if (node.IsFolder)
                {
                    var confirmFolder = MessageBox.Show(
                        $"Delete the folder '{node.Name}' and all its contents? This cannot be undone.",
                        "Delete Folder",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);
                    if (confirmFolder != MessageBoxResult.Yes)
                        return;

                    if (Directory.Exists(node.Path))
                    {
                        var cmd = new FileDeleteCommand(node.Path);
                        await UndoRedoManager.Instance.ExecuteAsync(cmd);
                    }
                }
                else
                {
                    if (File.Exists(node.Path))
                    {
                        var cmd = new FileDeleteCommand(node.Path);
                        await UndoRedoManager.Instance.ExecuteAsync(cmd);
                    }
                }

                RemoveProjectTreeNode(node);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to delete: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static ProjectFileNode? GetProjectFileNode(object sender) =>
            (sender as FrameworkElement)?.DataContext as ProjectFileNode;
    }
}
