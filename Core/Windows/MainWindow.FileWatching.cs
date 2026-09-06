using System.IO;
using PZTools.Core.Functions.Tester;
using PZTools.Core.Functions.Projects;
using PZTools.Core.Models;
using Application = System.Windows.Application;

namespace PZTools.Core.Windows
{
    public partial class MainWindow
    {
        private const int FolderDebounceMs = 150;

        private readonly object _watchLock = new();
        private readonly object _folderWatchLock = new();
        private readonly Dictionary<string, FileSystemWatcher> _folderWatchersByPath = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CancellationTokenSource> _folderDebounceByPath = new(StringComparer.OrdinalIgnoreCase);
        private FileSystemWatcher? _openedFileWatcher;
        private CancellationTokenSource? _openedFileDebounceCts;
        private string _watchedFullPath = string.Empty;

        private ProjectFileNode? FindNodeByPath(string fullPath)
        {
            if (_commonTree != null)
            {
                var commonResult = FindNodeByPathRecursive(_commonTree, fullPath);
                if (commonResult != null)
                    return commonResult;
            }
            foreach (var t in ModProject.Targets)
            {
                if (t.FileTree == null)
                    continue;

                var found = FindNodeByPathRecursive(t.FileTree, fullPath);
                if (found != null)
                    return found;
            }
            return null;
        }

        private ProjectFileNode? FindNodeByPathRecursive(ProjectFileNode current, string fullPath)
        {
            var currentFull = SafeFullPath(current.Path);
            if (currentFull != null && string.Equals(currentFull, fullPath, StringComparison.OrdinalIgnoreCase))
                return current;

            foreach (var child in current.Children)
            {
                var found = FindNodeByPathRecursive(child, fullPath);
                if (found != null)
                    return found;
            }
            return null;
        }

        private static string? SafeFullPath(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch { return null; }
        }

        private void EnsureFolderWatcher(string folderFullPath)
        {
            lock (_folderWatchLock)
            {
                if (IsClosing) return;
                if (_folderWatchersByPath.ContainsKey(folderFullPath))
                    return;

                if (!Directory.Exists(folderFullPath))
                    return;

                var watcher = new FileSystemWatcher(folderFullPath)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter =
                        NotifyFilters.FileName |
                        NotifyFilters.DirectoryName |
                        NotifyFilters.LastWrite |
                        NotifyFilters.Size |
                        NotifyFilters.CreationTime,
                    EnableRaisingEvents = true
                };

                watcher.Created += FolderWatcher_OnEvent;
                watcher.Changed += FolderWatcher_OnEvent;
                watcher.Deleted += FolderWatcher_OnEvent;
                watcher.Renamed += FolderWatcher_OnRenamed;

                _folderWatchersByPath[folderFullPath] = watcher;
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

                if (_folderWatchersByPath.TryGetValue(folderFullPath, out var watcher))
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Created -= FolderWatcher_OnEvent;
                    watcher.Changed -= FolderWatcher_OnEvent;
                    watcher.Deleted -= FolderWatcher_OnEvent;
                    watcher.Renamed -= FolderWatcher_OnRenamed;
                    watcher.Dispose();

                    _folderWatchersByPath.Remove(folderFullPath);
                }
            }
        }

        private void FolderWatcher_OnEvent(object sender, FileSystemEventArgs e)
        {
            if (sender is not FileSystemWatcher w)
                return;
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
            if (sender is not FileSystemWatcher w)
                return;

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
            if (full == null)
                return;

            lock (_folderWatchLock)
            {
                if (!_expandedFolderPaths.Contains(full))
                    return;
                if (IsClosing) return;

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
            if (folderFull == null)
                return;

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
                if (IsClosing) return;
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
                    Filter = fileName,
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
                if (IsClosing) return;
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

                    LuaEditor.Clear();
                    ShowEditorEmptyState("File moved or deleted", "Select another file from the project explorer.");
                    RefreshInspector();
                });
                return;
            }

            if (new FileInfo(path).Length > ProjectSearchService.MaxFileBytes)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    if (OpenedFilePath == path) { LuaEditor.Clear(); ShowEditorEmptyState("Large file", "Open this file in VS Code to view its contents."); }
                });
                return;
            }
            string text = await ReadAllTextWithRetryAsync(path, token);

            string? ext = null;

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (!string.Equals(OpenedFilePath, path, StringComparison.OrdinalIgnoreCase))
                    return;

                if (token.IsCancellationRequested || IsClosing) return;
                UpdatePreviewText(text);
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
    }
}
