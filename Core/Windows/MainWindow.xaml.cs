using PZTools.Core.Functions;
using PZTools.Core.Functions.Logger;
using PZTools.Core.Functions.Projects;
using PZTools.Core.Functions.Tester;
using PZTools.Core.Functions.Undo;
using PZTools.Core.Functions.Zomboid;
using PZTools.Core.Models;
using PZTools.Core.Models.InputDialog;
using PZTools.Core.Models.View;
using PZTools.Core.Windows.Dialogs.Project;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Packaging;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml;
using System.Xml.Linq;
using Application = System.Windows.Application;
using ContextMenu = System.Windows.Controls.ContextMenu;
using DataObject = System.Windows.DataObject;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using LayoutSettings = PZTools.Core.Models.View.LayoutSettings;
using MenuItem = System.Windows.Controls.MenuItem;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace PZTools.Core.Windows
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public ModProject ModProject { get; set; }
        public System.Windows.Controls.TextBox ConsoleOutputControl => ConsoleOutput;
        public static MainWindow Instance { get; private set; }
        public string OpenedFilePath = string.Empty;

        private System.Windows.Point _dragStartPoint;
        private ProjectFileNode? _draggedNode;

        private FileSystemWatcher? _openedFileWatcher;
        private CancellationTokenSource? _openedFileDebounceCts;

        private readonly object _watchLock = new();
        private string _watchedFullPath = string.Empty;

        private readonly object _folderWatchLock = new();
        private readonly Dictionary<string, FileSystemWatcher> _folderWatchersByPath = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CancellationTokenSource> _folderDebounceByPath = new(StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<string> _expandedFolderPaths = new(StringComparer.OrdinalIgnoreCase);

        private const int FolderDebounceMs = 150;

        public bool reloading = false;
        public bool isClosing { get; private set; } = false;

        public MainWindow()
        {
            InitializeComponent();
            this.FreeDragThisWindow();

            // Apply editor font size from saved AppSettings (will update the DynamicResource EditorFontSize)
            ApplyAppSettings();

            DataContext = new MainViewModel();
            ModProject = ProjectEngine.CurrentProject;
            Instance = this;
            UndoRedoManager.Instance.CommandExecuted += UndoRedo_CommandExecuted;

            Title = $"PZ Tools - {ProjectEngine.CurrentProject}";
            LoadLayout();
        }

        public void ApplyAppSettings()
        {
            UpdateFontSize();
            UpdateWordWrap();
        }

        private void UpdateFontSize()
        {
            try
            {
                const int defaultSize = 14;

                var fontSize = Config.GetAppSetting<double>("EditorFontSize");
                this.Resources["EditorFontSize"] = fontSize;

                // Also apply directly to known controls as a fallback
                if (LuaEditor != null) LuaEditor.FontSize = fontSize;
                if (ConsoleOutput != null) ConsoleOutput.FontSize = fontSize;
            }
            catch
            {
                this.Resources["EditorFontSize"] = 14.0;
            }
        }

        private void UpdateWordWrap()
        {
            var enableWordWrap = Config.GetAppSetting<bool>("EditorWordWrap");
            LuaEditor.WordWrap = enableWordWrap;
        }

        private async void UndoRedo_CommandExecuted(object? sender, UndoRedoEventArgs e)
        {
            try
            {
                switch (e.Command)
                {
                    case FileMoveCommand move:
                        {
                            var oldParent = SafeFullPath(Path.GetDirectoryName(move.Source) ?? string.Empty);
                            var newParent = SafeFullPath(Path.GetDirectoryName(move.Destination) ?? string.Empty);

                            if (!string.IsNullOrWhiteSpace(oldParent))
                                await RefreshFolderPathUIAsync(oldParent);

                            if (!string.IsNullOrWhiteSpace(newParent) && !string.Equals(oldParent, newParent, StringComparison.OrdinalIgnoreCase))
                                await RefreshFolderPathUIAsync(newParent);
                        }
                        break;

                    case FileCreateCommand create:
                        {
                            var parent = SafeFullPath(Path.GetDirectoryName(create.Path) ?? string.Empty);
                            if (!string.IsNullOrWhiteSpace(parent))
                                await RefreshFolderPathUIAsync(parent);
                        }
                        break;

                    case FileDeleteCommand del:
                        {
                            var parent = SafeFullPath(Path.GetDirectoryName(del.Path) ?? string.Empty);
                            if (!string.IsNullOrWhiteSpace(parent))
                                await RefreshFolderPathUIAsync(parent);
                        }
                        break;

                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                await WriteToConsole($"Undo/Redo UI refresh error: {ex.Message}");
            }
        }

        public async Task RefreshFolderPathUIAsync(string folderFullPath)
        {
            var full = SafeFullPath(folderFullPath);
            if (full == null) return;

            var node = FindNodeByPath(full);
            if (node != null && node.IsFolder)
            {
                await RefreshFolderNodeAsync(node);
                return;
            }

            var parent = SafeFullPath(Path.GetDirectoryName(full) ?? string.Empty);
            if (parent != null)
            {
                var parentNode = FindNodeByPath(parent);
                if (parentNode != null && parentNode.IsFolder)
                {
                    await RefreshFolderNodeAsync(parentNode);
                }
            }
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ProjectTreeView.SelectedItemChanged += ProjectTreeView_SelectedItemChanged;
            ProjectTreeView.MouseDoubleClick += ProjectTreeView_MouseDoubleClick;

            ProjectTreeView.AddHandler(TreeViewItem.ExpandedEvent, new RoutedEventHandler(ProjectTreeView_ItemExpanded));
            ProjectTreeView.AddHandler(TreeViewItem.CollapsedEvent, new RoutedEventHandler(ProjectTreeView_ItemCollapsed));

            if (ProjectTreeView.ContextMenu == null)
                ProjectTreeView.ContextMenu = new ContextMenu();

            foreach (var target in ModProject.Targets)
            {
                target.LoadFiles();
            }

            ProjectTreeView.ItemsSource = ModProject.Targets;

            foreach (var msg in Console.GetAllMessages())
            {
                await WriteToConsole(msg);
            }

            Console.OnLogMessage += async (s, msg) => { if (this.IsVisible) { await WriteToConsole(msg); } };
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            isClosing = true;
            if (Config.GetAppSetting<bool>("ConfirmOnExit") && !reloading)
            {
                var result = MessageBox.Show(
                    "Are you sure you want to exit?",
                    "Confirm Exit",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                }
            }

            if (!e.Cancel && !reloading) App.CloseApp();
        }

        public void LoadSettings()
        {

        }

        public void SaveLayout()
        {
            var layoutSetting = new LayoutSettings(
                LayoutExplorer.ActualWidth,
                LayoutEditor.ActualWidth,
                LayoutProperties.ActualWidth,
                LayoutConsole.ActualHeight);

            Config.StoreObject(VariableType.system, "MainWindowLayout", layoutSetting);
        }

        public void LoadLayout()
        {
            var saved = Config.GetObject<LayoutSettings>(VariableType.system, "MainWindowLayout");
            if (saved == null) return;

            double total = saved.ExplorerWidth + saved.EditorWidth + saved.PropertiesWidth;
            if (total <= 0) return;

            LayoutExplorer.Width = new GridLength(saved.ExplorerWidth / total, GridUnitType.Star);
            LayoutEditor.Width = new GridLength(saved.EditorWidth / total, GridUnitType.Star);
            LayoutProperties.Width = new GridLength(saved.PropertiesWidth / total, GridUnitType.Star);

            LayoutConsole.Height = new GridLength(saved.ConsoleHeight);
        }


        public static Task WriteToConsole(string message)
        {
            string formattedMessage = $"[{DateTime.Now:HH:mm:ss}] {message}";

            return Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var window = MainWindow.Instance;
                if (window == null)
                    return;

                window.ConsoleOutputControl.AppendText(formattedMessage + Environment.NewLine);
                window.ConsoleScroll.ScrollToBottom();
            }).Task;
        }

        private ProjectFileNode? FindNodeByPath(string fullPath)
        {
            foreach (var t in ModProject.Targets)
            {
                if (t.FileTree == null) continue;

                var found = FindNodeByPathRecursive(t.FileTree, fullPath);
                if (found != null) return found;
            }
            return null;
        }

        private ProjectFileNode? FindNodeByPathRecursive(ProjectFileNode current, string fullPath)
        {
            var currentFull = SafeFullPath(current.Path);
            if (currentFull != null && string.Equals(currentFull, fullPath, StringComparison.OrdinalIgnoreCase))
                return current;

            foreach (var c in current.Children.Where(x => !Double.TryParse(x.Name, out _)))
            {
                var found = FindNodeByPathRecursive(c, fullPath);
                if (found != null) return found;
            }
            return null;
        }

        private static string? SafeFullPath(string path)
        {
            try { return Path.GetFullPath(path); }
            catch { return null; }
        }

        private void EnsureFolderWatcher(string folderFullPath)
        {
            lock (_folderWatchLock)
            {
                if (_folderWatchersByPath.ContainsKey(folderFullPath))
                    return;

                if (!Directory.Exists(folderFullPath))
                    return;

                var w = new FileSystemWatcher(folderFullPath)
                {
                    IncludeSubdirectories = false, // Watch this folder only; open children will get their own watchers
                    NotifyFilter =
                        NotifyFilters.FileName |
                        NotifyFilters.DirectoryName |
                        NotifyFilters.LastWrite |
                        NotifyFilters.Size |
                        NotifyFilters.CreationTime,
                    EnableRaisingEvents = true
                };

                w.Created += FolderWatcher_OnEvent;
                w.Changed += FolderWatcher_OnEvent;
                w.Deleted += FolderWatcher_OnEvent;
                w.Renamed += FolderWatcher_OnRenamed;

                _folderWatchersByPath[folderFullPath] = w;
            }
        }

        private void StopFolderWatcher(string folderFullPath)
        {
            lock (_folderWatchLock)
            {
                if (_folderDebounceByPath.TryGetValue(folderFullPath, out var cts))
                {
                    cts.Cancel();
                    cts.Dispose();
                    _folderDebounceByPath.Remove(folderFullPath);
                }

                if (_folderWatchersByPath.TryGetValue(folderFullPath, out var w))
                {
                    w.EnableRaisingEvents = false;
                    w.Created -= FolderWatcher_OnEvent;
                    w.Changed -= FolderWatcher_OnEvent;
                    w.Deleted -= FolderWatcher_OnEvent;
                    w.Renamed -= FolderWatcher_OnRenamed;
                    w.Dispose();

                    _folderWatchersByPath.Remove(folderFullPath);
                }
            }
        }

        private void FolderWatcher_OnEvent(object sender, FileSystemEventArgs e)
        {
            if (sender is not FileSystemWatcher w) return;
            if (!string.IsNullOrWhiteSpace(OpenedFilePath))
            {
                var openedFull = SafeFullPath(OpenedFilePath);
                var changedFull = SafeFullPath(e.FullPath);

                if (openedFull != null && changedFull != null &&
                    string.Equals(openedFull, changedFull, StringComparison.OrdinalIgnoreCase))
                {
                    DebounceOpenedFileChange();
                }
            }

            DebounceFolderRefresh(w.Path);
        }


        private void FolderWatcher_OnRenamed(object sender, RenamedEventArgs e)
        {
            if (sender is not FileSystemWatcher w) return;

            var openedFull = SafeFullPath(OpenedFilePath);
            if (openedFull != null)
            {
                var oldFull = SafeFullPath(e.OldFullPath);
                var newFull = SafeFullPath(e.FullPath);

                if ((oldFull != null && string.Equals(openedFull, oldFull, StringComparison.OrdinalIgnoreCase)) ||
                    (newFull != null && string.Equals(openedFull, newFull, StringComparison.OrdinalIgnoreCase)))
                {
                    DebounceOpenedFileChange();
                }
            }

            DebounceFolderRefresh(w.Path);
        }


        private void DebounceFolderRefresh(string folderPath)
        {
            var full = SafeFullPath(folderPath);
            if (full == null) return;

            lock (_folderWatchLock)
            {
                if (!_expandedFolderPaths.Contains(full))
                    return;

                if (_folderDebounceByPath.TryGetValue(full, out var prev))
                {
                    prev.Cancel();
                    prev.Dispose();
                }

                var cts = new CancellationTokenSource();
                _folderDebounceByPath[full] = cts;

                _ = RefreshFolderByPathDebouncedAsync(full, cts.Token);
            }
        }

        private void ReconcileChildren(ProjectFileNode folderNode, List<ProjectFileNode> newChildren)
        {
            var existingByPath = folderNode.Children
                .Where(c => SafeFullPath(c.Path) != null)
                .ToDictionary(c => SafeFullPath(c.Path)!, c => c, StringComparer.OrdinalIgnoreCase);

            var rebuilt = new List<ProjectFileNode>();

            foreach (var incoming in newChildren)
            {
                var incomingFull = SafeFullPath(incoming.Path);
                if (incomingFull != null && existingByPath.TryGetValue(incomingFull, out var existing))
                {
                    existing.Name = incoming.Name;
                    existing.IsFolder = incoming.IsFolder;
                    existing.Path = incoming.Path;

                    rebuilt.Add(existing);
                }
                else
                {
                    rebuilt.Add(incoming);
                }
            }

            folderNode.Children.Clear();
            foreach (var n in rebuilt)
                folderNode.Children.Add(n);
        }


        private async Task RefreshFolderNodeAsync(ProjectFileNode folderNode)
        {
            var folderFull = SafeFullPath(folderNode.Path);
            if (folderFull == null) return;

            HashSet<string> expandedSnapshot;
            lock (_folderWatchLock)
            {
                expandedSnapshot = new HashSet<string>(_expandedFolderPaths, StringComparer.OrdinalIgnoreCase);
            }

            List<ProjectFileNode> newChildren = await Task.Run(() =>
            {
                var list = new List<ProjectFileNode>();

                if (!Directory.Exists(folderFull))
                    return list;

                foreach (var dir in Directory.GetDirectories(folderFull))
                {
                    list.Add(new ProjectFileNode
                    {
                        Name = Path.GetFileName(dir),
                        Path = dir,
                        IsFolder = true
                    });
                }

                foreach (var file in Directory.GetFiles(folderFull))
                {
                    list.Add(new ProjectFileNode
                    {
                        Name = Path.GetFileName(file),
                        Path = file,
                        IsFolder = false
                    });
                }

                return list
                    .OrderByDescending(n => n.IsFolder)
                    .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            });

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ReconcileChildren(folderNode, newChildren);

                foreach (var child in folderNode.Children.Where(c => c.IsFolder))
                {
                    var childFull = SafeFullPath(child.Path);
                    if (childFull != null && expandedSnapshot.Contains(childFull))
                        EnsureFolderWatcher(childFull);
                }
            });
        }


        private async Task RefreshFolderByPathDebouncedAsync(string folderFullPath, CancellationToken token)
        {
            try
            {
                await Task.Delay(FolderDebounceMs, token);

                var node = FindNodeByPath(folderFullPath);
                if (node == null || !node.IsFolder)
                    return;

                await RefreshFolderNodeAsync(node);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                await WriteToConsole($"Folder watch error ({folderFullPath}): {ex.Message}");
            }
        }


        private void WatchOpenedFile(string? fullPath)
        {
            lock (_watchLock)
            {
                StopWatchingOpenedFile_NoLock();

                if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
                    return;

                _watchedFullPath = Path.GetFullPath(fullPath);

                var dir = Path.GetDirectoryName(_watchedFullPath);
                var fileName = Path.GetFileName(_watchedFullPath);

                if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(fileName))
                    return;

                _openedFileWatcher = new FileSystemWatcher(dir)
                {
                    Filter = fileName, // watch only this file
                    NotifyFilter =
                        NotifyFilters.LastWrite |
                        NotifyFilters.Size |
                        NotifyFilters.FileName |
                        NotifyFilters.Attributes |
                        NotifyFilters.CreationTime |
                        NotifyFilters.Security,
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true
                };

                _openedFileWatcher.Changed += OpenedFileWatcher_OnFsEvent;
                _openedFileWatcher.Created += OpenedFileWatcher_OnFsEvent;
                _openedFileWatcher.Deleted += OpenedFileWatcher_OnFsEvent;
                _openedFileWatcher.Renamed += OpenedFileWatcher_OnRenamed;
            }
        }

        private void StopWatchingOpenedFile()
        {
            lock (_watchLock)
            {
                StopWatchingOpenedFile_NoLock();
            }
        }

        private void StopWatchingOpenedFile_NoLock()
        {
            _openedFileDebounceCts?.Cancel();
            _openedFileDebounceCts?.Dispose();
            _openedFileDebounceCts = null;

            if (_openedFileWatcher != null)
            {
                _openedFileWatcher.EnableRaisingEvents = false;
                _openedFileWatcher.Changed -= OpenedFileWatcher_OnFsEvent;
                _openedFileWatcher.Created -= OpenedFileWatcher_OnFsEvent;
                _openedFileWatcher.Deleted -= OpenedFileWatcher_OnFsEvent;
                _openedFileWatcher.Renamed -= OpenedFileWatcher_OnRenamed;
                _openedFileWatcher.Dispose();
                _openedFileWatcher = null;
            }

            _watchedFullPath = string.Empty;
        }

        private void OpenedFileWatcher_OnFsEvent(object sender, FileSystemEventArgs e)
        {
            if (!PathsMatchWatchedFile(e.FullPath))
                return;

            DebounceOpenedFileChange();
        }

        private void OpenedFileWatcher_OnRenamed(object sender, RenamedEventArgs e)
        {
            if (!PathsMatchWatchedFile(e.OldFullPath) && !PathsMatchWatchedFile(e.FullPath))
                return;

            DebounceOpenedFileChange();
        }

        private bool PathsMatchWatchedFile(string path)
        {
            lock (_watchLock)
            {
                if (string.IsNullOrWhiteSpace(_watchedFullPath))
                    return false;

                return string.Equals(Path.GetFullPath(path), _watchedFullPath, StringComparison.OrdinalIgnoreCase);
            }
        }

        private void DebounceOpenedFileChange()
        {
            CancellationTokenSource cts;

            lock (_watchLock)
            {
                _openedFileDebounceCts?.Cancel();
                _openedFileDebounceCts?.Dispose();
                _openedFileDebounceCts = new CancellationTokenSource();
                cts = _openedFileDebounceCts;
            }

            _ = HandleOpenedFileChangeDebouncedAsync(cts.Token);
        }

        private async Task HandleOpenedFileChangeDebouncedAsync(CancellationToken token)
        {
            try
            {
                await Task.Delay(200, token);

                await OnOpenedFileChangedAsync(token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                await WriteToConsole($"File watch error: {ex.Message}");
            }
        }

        private async Task OnOpenedFileChangedAsync(CancellationToken token)
        {
            string path;
            lock (_watchLock)
                path = _watchedFullPath;

            if (string.IsNullOrWhiteSpace(path))
                return;

            if (!File.Exists(path))
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (!string.Equals(OpenedFilePath, path, StringComparison.OrdinalIgnoreCase))
                        return;

                    LuaEditor.Text = $"-- File was deleted: {path}";
                });
                return;
            }

            string text = await ReadAllTextWithRetryAsync(path, token);

            string? ext = null;

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (!string.Equals(OpenedFilePath, path, StringComparison.OrdinalIgnoreCase))
                    return;

                LuaEditor.Text = text;
                ext = Path.GetExtension(path);
                LoadHighlighting(ext);
            });

            if (ext != null && ext.Equals(".lua", StringComparison.OrdinalIgnoreCase))
                await LuaTester.Test(text, path);

        }


        private static async Task<string> ReadAllTextWithRetryAsync(
            string path,
            CancellationToken token,
            int retries = 5,
            int delayMs = 50)
        {
            for (int i = 0; i < retries; i++)
            {
                token.ThrowIfCancellationRequested();

                try
                {
                    using var fs = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);

                    using var reader = new StreamReader(fs);
                    return await reader.ReadToEndAsync();
                }
                catch (IOException) when (i < retries - 1)
                {
                    await Task.Delay(delayMs, token);
                }
            }

            return await File.ReadAllTextAsync(path, token);
        }


        private async void ProjectTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var selected = ProjectTreeView.SelectedItem;
            if (selected is ProjectFileNode node)
            {
                var extension = Path.GetExtension(node.Path);
                var supportedExtensions = new[] { ".txt", ".info", ".lua", ".xml", ".json", ".cfg", ".ini", ".md" };
                if (supportedExtensions.Contains(extension))
                {
                    try
                    {
                        OpenedFilePath = node.Path;
                        WatchOpenedFile(node.Path);

                        var text = System.IO.File.ReadAllText(node.Path);
                        LuaEditor.Text = text;
                        LoadHighlighting(extension);

                        if (extension == ".lua") await LuaTester.Test(text, OpenedFilePath);
                    }
                    catch
                    {
                        LuaEditor.Text = $"-- Error loading file: {node.Path}";
                    }
                }
                else if (!node.IsFolder)
                {
                    LuaEditor.Clear();
                    LuaEditor.Text = $"-- Preview not available for this file type. ({extension})";
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
                if (target.Build == ZomboidGame.latestStableBuild) txtPropName.Text = ProjectEngine.CurrentProject.Name;
                else txtPropName.Text = $"Build {target.Build}";

                txtPropPath.Text = target.Path;
                txtPropSize.Text = "-";
                txtPropEncoding.Text = "-";
            }
            else
            {
                txtPropName.Text = "";
                txtPropPath.Text = "";
                txtPropSize.Text = "";
                txtPropEncoding.Text = "";
            }
        }

        private System.Text.Encoding GetFileEncoding(string filename)
        {
            using (var reader = new StreamReader(filename, true))
            {
                if (reader.Peek() >= 0)
                    reader.Read();
                return reader.CurrentEncoding;
            }
        }

        private void LoadHighlighting(string ext)
        {
            string extension = ext.Replace(".", "").ToLower();
            foreach (var n in Assembly.GetExecutingAssembly().GetManifestResourceNames())
                Debug.WriteLine(n);

            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream($"PZTools.Resources.PZ{extension}.xshd");
            if (stream != null)
            {
                using (XmlTextReader reader = new XmlTextReader(stream))
                {
                    LuaEditor.SyntaxHighlighting = ICSharpCode.AvalonEdit.Highlighting.Xshd.HighlightingLoader.Load
                        (reader, ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance);
                }

            }
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
                        if (defaultApp == null) defaultApp = "";

                        var defaultAppArgs = Config.GetAppSetting<string>("DefaultFileEditorArgs");
                        if (defaultAppArgs == null) defaultAppArgs = "";

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
            if (full == null) return;

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
            if (full == null) return;

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
            ProjectFileNode? fileNode = null;
            ModTarget? modTargetNode = null;

            bool isTreeHeader = false;

            if (tvi?.DataContext is not ProjectFileNode)
            {
                isTreeHeader = true;
                modTargetNode = tvi?.DataContext as ModTarget;
            }
            else fileNode = tvi?.DataContext as ProjectFileNode;

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

                var miDelete = new MenuItem { Header = "Delete", DataContext = fileNode };
                miDelete.Click += Context_Delete_Click;
                menu.Items.Add(miDelete);
            }
        }


        private async void Context_NewFile_Click(object sender, RoutedEventArgs e)
        {
            var fe = sender as FrameworkElement;
            var node = fe?.DataContext as ProjectFileNode;
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
            if (dlg.ShowDialog() != true) return;

            var name = dlg.TryGetResponse("name");
            var ext = dlg.TryGetResponse("ext");
            if (!ext.StartsWith(".")) ext = "." + ext;

            var fullName = name + ext;
            var targetPath = Path.Combine(node.Path, fullName);

            try
            {
                if (File.Exists(targetPath))
                {
                    MessageBox.Show("File already exists.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                string initialContent = string.Empty;

                if (ext.ToLower() == ".lua")
                {
                    initialContent = "-- A New Lua file\r\n";
                    var watermark = Config.GetVariable(VariableType.user, $"{ProjectEngine.CurrentProject.Name}-watermark");
                    if (!string.IsNullOrEmpty(watermark))
                    {
                        initialContent = watermark;
                    }
                }
                else if (ext.ToLower() == ".info")
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
            var fe = sender as FrameworkElement;
            var node = fe?.DataContext as ProjectFileNode;
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
            if (dlg.ShowDialog() != true) return;

            var name = dlg.TryGetResponse("name");

            var fullName = name;
            var targetPath = Path.Combine(node.Path, fullName);

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
                    Name = fullName,
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

        private async void Context_AddTarget_Click(object sender, RoutedEventArgs e)
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
                string newTargetName = inputDialogs.TryGetResponse("newTargetBuild");
                double newBuild = 0;

                if (!double.TryParse(newTargetName, out newBuild))
                {
                    MessageBox.Show("Invalid build target!", "Invalid Build Target", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (newBuild < ZomboidGame.latestStableBuild)
                {
                    MessageBox.Show($"Older Build Target than latest ({ZomboidGame.latestStableBuild}) is unsupported.", "Old Build Target?", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (ProjectEngine.CurrentProject.Targets.Any(x => x.Build == newBuild))
                {
                    MessageBox.Show($"Build {newBuild} is already supported within this project.", "Already targeting build!", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var targetPath = Path.Combine(ProjectEngine.CurrentProjectPath, newTargetName);
                ProjectEngine.CreateModFolderStructure(targetPath);
                var modTarget = new ModTarget()
                {
                    Build = newBuild,
                    Path = targetPath
                };

                ProjectEngine.CurrentProject.Targets.Add(modTarget);

                modTarget.LoadFiles();

                UpdateTreeView();
            }
        }

        private async void Context_RemoveTarget_Click(object sender, RoutedEventArgs e)
        {
            var fe = sender as FrameworkElement;
            var node = fe?.DataContext as ModTarget;
            if (node == null)
                return;

            if (node.Build == ZomboidGame.latestStableBuild)
            {
                MessageBox.Show("You cannot remove the latest stable build target from the project.", "Remove Target", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show("Are you sure you would like to remove this supported target? \n\n(Warning: This will delete any data under these build within your project)", "Remove Target", MessageBoxButton.YesNo, MessageBoxImage.Warning);
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
            var fe = sender as FrameworkElement;
            var node = fe?.DataContext as ProjectFileNode;
            if (node == null)
                return;

            await LuaTester.TestFile(node.Path);
        }

        private void Context_Show_Click(object sender, RoutedEventArgs e)
        {
            var fe = sender as FrameworkElement;
            var node = fe?.DataContext as ProjectFileNode;
            if (node == null)
                return;

            WindowsHelpers.ShowInExplorer(node.Path);
        }

        private void Context_EditFile_Click(object sender, RoutedEventArgs e)
        {
            var fe = sender as FrameworkElement;
            var node = fe?.DataContext as ProjectFileNode;
            if (node == null)
                return;

            var defaultApp = Config.GetAppSetting<string>("DefaultFileEditorApp");
            if (defaultApp == null) defaultApp = "";

            var defaultAppArgs = Config.GetAppSetting<string>("DefaultFileEditorArgs");
            if (defaultAppArgs == null) defaultAppArgs = "";

            WindowsHelpers.OpenFile(node.Path, defaultApp, defaultAppArgs);
        }

        private async void Context_RenameFile_Click(object sender, RoutedEventArgs e)
        {
            var fe = sender as FrameworkElement;
            var node = fe?.DataContext as ProjectFileNode;
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
                string parentDir = Directory.GetParent(node.Path).FullName;
                string newPath = Path.Combine(parentDir, newFileName);

                try
                {
                    var cmd = new FileMoveCommand(node.Path, newPath);
                    await UndoRedoManager.Instance.ExecuteAsync(cmd);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to rename file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }

                RemoveProjectTreeNode(node);
                UpdateProjectTreeNodeParent(node, FindNodeByPath(parentDir));
            }
        }

        private async void Context_Delete_Click(object sender, RoutedEventArgs e)
        {
            var fe = sender as FrameworkElement;
            var node = fe?.DataContext as ProjectFileNode;
            if (node == null)
                return;

            var confirm = MessageBox.Show($"Are you sure you want to delete '{node.Name}'?", "Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                if (node.IsFolder)
                {
                    var confirmFolder = MessageBox.Show($"Are you sure you want to delete this folder '{node.Name}' and all it's contents? (this can't be un-done)", "Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (confirmFolder != MessageBoxResult.Yes) return;

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
            if (tvi == null) return;

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

            // prevent moving into self or descendant
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

                // remove from old parent
                RemoveProjectTreeNode(dragged);

                // update node paths and add to destination
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
            if (source == null) return null;
            var current = source;
            while (current != null)
            {
                if (current is T typed) return typed;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private ProjectFileNode? FindParent(ProjectFileNode child)
        {
            foreach (var t in ModProject.Targets)
            {
                if (t.FileTree == null) continue;
                var parent = FindParentRecursive(t.FileTree, child);
                if (parent != null) return parent;
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
                if (found != null) return found;
            }
            return null;
        }

        private bool IsDescendant(ProjectFileNode? node, ProjectFileNode potentialAncestor)
        {
            if (node == null) return false;
            if (node == potentialAncestor) return true;
            foreach (var child in potentialAncestor.Children)
            {
                if (IsDescendant(node, child)) return true;
            }
            return false;
        }

        public void UpdateTreeView()
        {
            ProjectEngine.CurrentProject.Targets[0].LoadFiles();
            ProjectTreeView.Items.Refresh();
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

        private void DeployProject_Click(object sender, RoutedEventArgs e)
        {
            ProjectDeployer.DeployProject(DeployFolder.Workshop);
        }

        private async void RunGame_Click(object sender, RoutedEventArgs e)
        {
            if (ZomboidGame.IsGameRunning())
            {
                await ZomboidGame.StopGame();
            }
            else
            {
                bool showWindow = true;
                var showSettingsWindow = Config.GetVariable(VariableType.user, "showRunWindowBeforeLaunch");
                if (!string.IsNullOrEmpty(showSettingsWindow))
                {
                    bool.TryParse(showSettingsWindow, out showWindow);
                }

                var runGameWindow = new RunProject(showWindow);
                runGameWindow.ShowDialog();
                if (ZomboidGame.IsGameRunning())
                {
                    RunGameBtn.Content = "Stop Game";
                }
            }
        }
    }
}
