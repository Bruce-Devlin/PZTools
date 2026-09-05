using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PZTools.Core.Functions.Projects;
using PZTools.Core.Functions.Undo;
using PZTools.Core.Models;
using DataObject = System.Windows.DataObject;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace PZTools.Core.Windows
{
    public partial class MainWindow
    {
        private System.Windows.Point _dragStartPoint;
        private ProjectFileNode? _draggedNode;

        private void ProjectTreeView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);

            var tvItem = VisualUpwardSearch<TreeViewItem>(e.OriginalSource as DependencyObject);
            if (tvItem != null)
            {
                _draggedNode = tvItem.DataContext as ProjectFileNode;
            }
            else
            {
                _draggedNode = null;
            }
        }

        private void ProjectTreeView_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var dep = e.OriginalSource as DependencyObject;
            var tvi = VisualUpwardSearch<TreeViewItem>(dep);
            if (tvi == null)
                return;

            tvi.IsSelected = true;
            tvi.Focus();
        }

        private void ProjectTreeView_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _draggedNode == null)
                return;

            var currentPos = e.GetPosition(null);
            if (Math.Abs(currentPos.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(currentPos.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            var data = new DataObject("ProjectFileNode", _draggedNode);
            DragDrop.DoDragDrop(ProjectTreeView, data, DragDropEffects.Move);
            _draggedNode = null;
        }

        private void ProjectTreeView_DragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent("ProjectFileNode"))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            var dragged = e.Data.GetData("ProjectFileNode") as ProjectFileNode;
            var targetItem = GetNearestContainer(e.OriginalSource as UIElement);
            var targetNode = targetItem?.DataContext as ProjectFileNode;

            if (targetNode == null || !targetNode.IsFolder)
            {
                e.Effects = DragDropEffects.None;
            }
            else if (IsDescendant(dragged, targetNode))
            {
                e.Effects = DragDropEffects.None;
            }
            else
            {
                e.Effects = DragDropEffects.Move;
            }

            e.Handled = true;
        }

        private async void ProjectTreeView_Drop(object sender, DragEventArgs e)
        {

            if (!e.Data.GetDataPresent("ProjectFileNode"))
                return;

            var dragged = e.Data.GetData("ProjectFileNode") as ProjectFileNode;
            var targetItem = GetNearestContainer(e.OriginalSource as UIElement);
            var targetNode = targetItem?.DataContext as ProjectFileNode;

            if (dragged == null || targetNode == null || !targetNode.IsFolder)
                return;

            if (IsDescendant(dragged, targetNode) || dragged == targetNode)
                return;

            try
            {
                var destPath = Path.Combine(targetNode.Path, dragged.Name);

                if (dragged.IsFolder)
                {
                    if (Directory.Exists(dragged.Path))
                    {
                        var cmd = new FileMoveCommand(dragged.Path, destPath);
                        await UndoRedoManager.Instance.ExecuteAsync(cmd);
                    }
                }
                else
                {
                    if (File.Exists(dragged.Path))
                    {
                        var cmd = new FileMoveCommand(dragged.Path, destPath);
                        await UndoRedoManager.Instance.ExecuteAsync(cmd);
                    }
                }

                RemoveProjectTreeNode(dragged);

                dragged.Path = destPath;
                UpdateProjectTreeNodeParent(dragged, targetNode);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Failed to move: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private TreeViewItem? GetTreeViewItemByPath(string path)
        {
            var full = SafeFullPath(path);
            if (full == null)
                return null;

            foreach (var item in ProjectTreeView.Items)
            {
                if (item is ModTarget target)
                {
                    if (target.FileTree == null)
                        continue;

                    var container = ProjectTreeView
                        .ItemContainerGenerator
                        .ContainerFromItem(target) as TreeViewItem;

                    if (container == null)
                        continue;

                    var result = GetTreeViewItemByPathRecursive(container, full);
                    if (result != null)
                        return result;
                }
            }

            return null;
        }

        private TreeViewItem? GetTreeViewItemByPathRecursive(TreeViewItem parent, string fullPath)
        {
            if (parent.DataContext is ProjectFileNode node)
            {
                var nodeFull = SafeFullPath(node.Path);
                if (nodeFull != null &&
                    string.Equals(nodeFull, fullPath, StringComparison.OrdinalIgnoreCase))
                {
                    return parent;
                }
            }

            if (!parent.IsExpanded)
                return null;

            foreach (var child in parent.Items)
            {
                var childContainer = parent
                    .ItemContainerGenerator
                    .ContainerFromItem(child) as TreeViewItem;

                if (childContainer == null)
                    continue;

                var result = GetTreeViewItemByPathRecursive(childContainer, fullPath);
                if (result != null)
                    return result;
            }

            return null;
        }


        private TreeViewItem? GetNearestContainer(UIElement? element)
        {
            return VisualUpwardSearch<TreeViewItem>(element);
        }

        private static T? VisualUpwardSearch<T>(DependencyObject? source) where T : DependencyObject
        {
            if (source == null)
                return null;
            var current = source;
            while (current != null)
            {
                if (current is T typed)
                    return typed;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private ProjectFileNode? FindParent(ProjectFileNode child)
        {
            foreach (var t in ModProject.Targets)
            {
                if (t.FileTree == null)
                    continue;
                var parent = FindParentRecursive(t.FileTree, child);
                if (parent != null)
                    return parent;
            }
            return null;
        }

        private ProjectFileNode? FindParentRecursive(ProjectFileNode current, ProjectFileNode child)
        {
            if (current.Children.Contains(child))
                return current;

            foreach (var c in current.Children)
            {
                var found = FindParentRecursive(c, child);
                if (found != null)
                    return found;
            }
            return null;
        }

        private bool IsDescendant(ProjectFileNode? node, ProjectFileNode potentialAncestor)
        {
            if (node == null)
                return false;
            if (node == potentialAncestor)
                return true;
            foreach (var child in potentialAncestor.Children)
            {
                if (IsDescendant(node, child))
                    return true;
            }
            return false;
        }

        public void UpdateTreeView()
        {
            foreach (var target in ModProject.Targets)
                target.LoadFiles();
            LoadExplorerRoots();
            ProjectTreeView.Items.Refresh();
        }

        private void LoadExplorerRoots()
        {
            var roots = new List<object>();
            var commonPath = Path.Combine(ModProject.RootPath, "common");
            if (Directory.Exists(commonPath))
            {
                _commonTree = ProjectEngine.BuildFileTree(commonPath);
                _commonTree.Name = "Common (all builds)";
                roots.Add(_commonTree);
            }
            roots.AddRange(ModProject.Targets);
            ProjectTreeView.ItemsSource = roots;
        }

        private void UpdatePathsRecursively(ProjectFileNode node)
        {
            if (node.IsFolder)
            {
                foreach (var child in node.Children)
                {
                    child.Path = Path.Combine(node.Path, child.Name);
                    UpdatePathsRecursively(child);
                }
            }
        }

        private void RemoveProjectTreeNode(ProjectFileNode node)
        {
            var parent = FindParent(node);
            if (parent != null)
                parent.Children.Remove(node);
            else
            {
                foreach (var t in ModProject.Targets)
                {
                    if (t.FileTree != null && t.FileTree.Children.Remove(node))
                        break;
                }
            }
        }

        private void UpdateProjectTreeNodeParent(ProjectFileNode node, ProjectFileNode parent)
        {
            UpdatePathsRecursively(node);
            parent.Children.Add(node);
        }
    }
}
