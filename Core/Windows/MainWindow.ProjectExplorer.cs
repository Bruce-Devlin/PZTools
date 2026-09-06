using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Xml;
using PZTools.Core.Functions;
using PZTools.Core.Functions.Projects;
using PZTools.Core.Functions.Tester;
using PZTools.Core.Models;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;

namespace PZTools.Core.Windows
{
    public partial class MainWindow
    {
        private static readonly HashSet<string> PreviewableFileExtensions = new(
            [".txt", ".info", ".lua", ".xml", ".json", ".cfg", ".ini", ".md", ".csv", ".properties", ".yml", ".yaml"],
            StringComparer.OrdinalIgnoreCase);

        private async void ProjectTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            try
            {
                if (e.NewValue is ProjectFileNode node && !node.IsFolder)
                {
                    PreviewFile(node.Path);
                    if (Path.GetExtension(node.Path).Equals(".lua", StringComparison.OrdinalIgnoreCase) &&
                        EditorEmptyState.Visibility == Visibility.Collapsed)
                        await LuaTester.Test(LuaEditor.Text, node.Path);
                    return;
                }
                ResetPreview();
                var path = e.NewValue is ProjectFileNode folder ? folder.Path : (e.NewValue as ModTarget)?.Path;
                if (path != null)
                {
                    FilePropertiesContent.Visibility = Visibility.Visible;
                    EmptyPropertiesState.Visibility = Visibility.Collapsed;
                    txtPropName.Text = e.NewValue is ModTarget target ? target.BuildName : Path.GetFileName(path);
                    txtPropPath.Text = path;
                    txtPropSize.Text = "—";
                    txtPropEncoding.Text = "—";
                    InspectPath(path);
                    ShowEditorEmptyState(e.NewValue is ModTarget ? "Build target selected" : "Folder selected",
                        "Select a file to preview it, or use Find in project.");
                }
                else
                {
                    FilePropertiesContent.Visibility = Visibility.Collapsed;
                    EmptyPropertiesState.Visibility = Visibility.Visible;
                    ShowEditorEmptyState("No file selected", "Select a project file to preview it.");
                }
            }
            catch (Exception ex)
            {
                ResetPreview();
                ShowEditorEmptyState("Could not preview file", ex.Message);
                await Console.Log($"Could not preview file: {ex.Message}", Console.LogLevel.Warning);
            }
        }

        private static System.Text.Encoding GetFileEncoding(string filename)
        {
            using var reader = new StreamReader(filename, detectEncodingFromByteOrderMarks: true);
            if (reader.Peek() >= 0)
                reader.Read();

            return reader.CurrentEncoding;
        }

        private void LoadHighlighting(string ext)
        {
            var extension = ext.TrimStart('.').ToLowerInvariant();
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream($"PZTools.Resources.PZ{extension}.xshd");
            if (stream is null)
            {
                LuaEditor.SyntaxHighlighting = ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance.GetDefinitionByExtension(ext);
                return;
            }

            using var reader = XmlReader.Create(stream);
            var definition = ICSharpCode.AvalonEdit.Highlighting.Xshd.HighlightingLoader.Load(
                    reader,
                    ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance);
            foreach (var color in definition.NamedHighlightingColors)
            {
                var name = color.Name?.ToLowerInvariant() ?? "";
                var key = name.Contains("comment") ? "Brush.SyntaxComment" :
                    name.Contains("string") || name == "char" ? "Brush.SyntaxString" :
                    name.Contains("number") || name.Contains("digit") ? "Brush.SyntaxNumber" :
                    name.Contains("key") ? "Brush.SyntaxKeyword" :
                    name.Contains("punctuation") ? "Brush.TextPrimary" : "Brush.SyntaxFunction";
                if (TryFindResource(key) is System.Windows.Media.SolidColorBrush brush)
                    color.Foreground = new ICSharpCode.AvalonEdit.Highlighting.SimpleHighlightingBrush(brush.Color);
            }
            LuaEditor.SyntaxHighlighting = definition;
        }

        private void ProjectTreeView_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (ProjectTreeView.SelectedItem is ProjectFileNode node)
            {
                if (!node.IsFolder && System.IO.File.Exists(node.Path))
                {
                    try
                    {
                        var defaultApp = Config.GetAppSetting<string>("DefaultFileEditorApp");
                        if (defaultApp == null)
                            defaultApp = "";

                        var defaultAppArgs = Config.GetAppSetting<string>("DefaultFileEditorArgs");
                        if (defaultAppArgs == null)
                            defaultAppArgs = "";

                        WindowsHelpers.OpenFile(node.Path, defaultApp, defaultAppArgs);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to open file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void ProjectTreeView_ItemExpanded(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is not TreeViewItem tvi)
                return;

            if (tvi.DataContext is not ProjectFileNode node || !node.IsFolder)
                return;

            var full = SafeFullPath(node.Path);
            if (full == null)
                return;

            lock (_folderWatchLock)
            {
                _expandedFolderPaths.Add(full);
            }

            EnsureFolderWatcher(full);

            _ = RefreshFolderNodeAsync(node);
        }

        private void ProjectTreeView_ItemCollapsed(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is not TreeViewItem tvi)
                return;

            if (tvi.DataContext is not ProjectFileNode node || !node.IsFolder)
                return;

            var full = SafeFullPath(node.Path);
            if (full == null)
                return;

            lock (_folderWatchLock)
            {
                _expandedFolderPaths.Remove(full);
            }

            StopFolderWatcher(full);
        }

        private void ProjectTreeView_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var original = e.OriginalSource as DependencyObject;
            var tvi = VisualUpwardSearch<TreeViewItem>(original);
            if (tvi is null)
            {
                e.Handled = true;
                return;
            }
            ProjectFileNode? fileNode = null;
            ModTarget? modTargetNode = null;

            bool isTreeHeader = false;

            if (tvi.DataContext is not ProjectFileNode)
            {
                isTreeHeader = true;
                modTargetNode = tvi.DataContext as ModTarget;
            }
            else
                fileNode = tvi.DataContext as ProjectFileNode;

            tvi.IsSelected = true;
            tvi.Focus();

            var menu = ProjectTreeView.ContextMenu ??= new ContextMenu();
            menu.Items.Clear();

            if (isTreeHeader)
            {
                var miAddTarget = new MenuItem { Header = "Add Target", DataContext = modTargetNode };
                miAddTarget.Click += Context_AddTarget_Click;
                menu.Items.Add(miAddTarget);

                var miRemoveTarget = new MenuItem { Header = "Remove Target", DataContext = modTargetNode };
                miRemoveTarget.Click += Context_RemoveTarget_Click;
                menu.Items.Add(miRemoveTarget);
            }
            else
            {
                if (fileNode is null)
                {
                    e.Handled = true;
                    return;
                }
                if (fileNode.IsFolder)
                {
                    var miNewFile = new MenuItem { Header = "New File...", DataContext = fileNode };
                    miNewFile.Click += Context_NewFile_Click;
                    menu.Items.Add(miNewFile);

                    var miNewFolder = new MenuItem { Header = "New Folder...", DataContext = fileNode };
                    miNewFolder.Click += Context_NewFolder_Click;
                    menu.Items.Add(miNewFolder);
                }
                else
                {
                    var miEdit = new MenuItem { Header = "Edit File", DataContext = fileNode };
                    miEdit.Click += Context_EditFile_Click;
                    menu.Items.Add(miEdit);

                    if (fileNode.Name.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
                    {
                        var miTestLua = new MenuItem { Header = "Test LUA File", DataContext = fileNode };
                        miTestLua.Click += Context_TestFile_Click;
                        menu.Items.Add(miTestLua);
                    }

                    var miRename = new MenuItem { Header = "Rename File", DataContext = fileNode };
                    miRename.Click += Context_RenameFile_Click;
                    menu.Items.Add(miRename);
                }

                var miShow = new MenuItem { Header = "Show In File Explorer", DataContext = fileNode };
                miShow.Click += Context_Show_Click;
                menu.Items.Add(miShow);

                var syncStatus = VersionSyncService.GetStatus(ModProject, fileNode.Path);
                if (syncStatus == VersionSyncStatus.Available)
                {
                    var miSync = new MenuItem { Header = "Sync Across Game Versions", DataContext = fileNode };
                    miSync.Click += Context_EnableVersionSync_Click;
                    menu.Items.Add(miSync);
                }
                else if (syncStatus == VersionSyncStatus.EnabledDirectly)
                {
                    var miStopSync = new MenuItem { Header = "Stop Syncing Across Versions", DataContext = fileNode };
                    miStopSync.Click += Context_DisableVersionSync_Click;
                    menu.Items.Add(miStopSync);
                }
                else if (syncStatus == VersionSyncStatus.EnabledByParent)
                {
                    var rulePath = VersionSyncService.GetDirectRuleDisplayPath(ModProject, fileNode.Path);
                    menu.Items.Add(new MenuItem
                    {
                        Header = $"Synced by parent folder: {rulePath}",
                        IsEnabled = false
                    });
                }

                var miDelete = new MenuItem { Header = "Delete", DataContext = fileNode };
                miDelete.Click += Context_Delete_Click;
                menu.Items.Add(miDelete);
            }
        }
    }
}
