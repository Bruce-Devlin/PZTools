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
            [".txt", ".info", ".lua", ".xml", ".json", ".cfg", ".ini", ".md"],
            StringComparer.OrdinalIgnoreCase);

        private async void ProjectTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var selected = ProjectTreeView.SelectedItem;
            if (selected is ProjectFileNode node)
            {
                FilePropertiesContent.Visibility = Visibility.Visible;
                EmptyPropertiesState.Visibility = Visibility.Collapsed;
                var extension = Path.GetExtension(node.Path);
                if (PreviewableFileExtensions.Contains(extension))
                {
                    try
                    {
                        OpenedFilePath = node.Path;
                        WatchOpenedFile(node.Path);

                        var text = System.IO.File.ReadAllText(node.Path);
                        LuaEditor.Text = text;
                        EditorEmptyState.Visibility = Visibility.Collapsed;
                        LoadHighlighting(extension);

                        if (extension == ".lua")
                            await LuaTester.Test(text, OpenedFilePath);
                    }
                    catch (Exception ex)
                    {
                        LuaEditor.Text = $"-- Error loading file: {node.Path}";
                        await Console.Log($"Could not preview '{node.Path}': {ex.Message}", Console.LogLevel.Warning);
                    }
                }
                else if (!node.IsFolder)
                {
                    StopWatchingOpenedFile();
                    OpenedFilePath = string.Empty;
                    LuaEditor.Clear();
                    LuaEditor.Text = $"-- Preview not available for this file type. ({extension})";
                    EditorEmptyState.Visibility = Visibility.Collapsed;
                }
                else
                {
                    StopWatchingOpenedFile();
                    OpenedFilePath = string.Empty;
                    LuaEditor.Clear();
                    ShowEditorEmptyState("Folder selected", "Select a file to preview it.");
                }

                txtPropName.Text = node.Name;
                txtPropPath.Text = node.Path;

                if (System.IO.File.Exists(node.Path))
                {
                    var info = new FileInfo(node.Path);
                    txtPropSize.Text = $"{info.Length / 1024.0:F2} KB";

                    txtPropEncoding.Text = GetFileEncoding(node.Path).WebName;
                }
                else
                {
                    txtPropSize.Text = "-";
                    txtPropEncoding.Text = "-";
                }
            }
            else if (selected is ModTarget target)
            {
                FilePropertiesContent.Visibility = Visibility.Visible;
                EmptyPropertiesState.Visibility = Visibility.Collapsed;
                ShowEditorEmptyState("Build target selected", "Select a file to preview it.");
                txtPropName.Text = target.BuildName;

                txtPropPath.Text = target.Path;
                txtPropSize.Text = "-";
                txtPropEncoding.Text = "-";
            }
            else
            {
                FilePropertiesContent.Visibility = Visibility.Collapsed;
                EmptyPropertiesState.Visibility = Visibility.Visible;
                ShowEditorEmptyState("No file selected", "Select a project file to preview it.");
                txtPropName.Text = "";
                txtPropPath.Text = "";
                txtPropSize.Text = "";
                txtPropEncoding.Text = "";
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
                return;

            using var reader = XmlReader.Create(stream);
            LuaEditor.SyntaxHighlighting =
                ICSharpCode.AvalonEdit.Highlighting.Xshd.HighlightingLoader.Load(
                    reader,
                    ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance);
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
