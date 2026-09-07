using System.IO;
using PZTools.Core.Functions.Projects;

namespace PZTools.Core.Windows
{
    public partial class MainWindow
    {
        private readonly object _versionSyncWatchLock = new();
        private readonly HashSet<string> _reportedVersionSyncConflicts = new(StringComparer.OrdinalIgnoreCase);
        private FileSystemWatcher? _versionSyncWatcher;
        private CancellationTokenSource? _versionSyncDebounce;

        private void StartVersionSyncWatcher()
        {
            StopVersionSyncWatcher();
            if (!Directory.Exists(ModProject.RootPath))
                return;

            _versionSyncWatcher = new FileSystemWatcher(ModProject.RootPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _versionSyncWatcher.Created += VersionSyncWatcher_OnChange;
            _versionSyncWatcher.Changed += VersionSyncWatcher_OnChange;
            _versionSyncWatcher.Deleted += VersionSyncWatcher_OnChange;
            _versionSyncWatcher.Renamed += VersionSyncWatcher_OnRenamed;
        }

        private void StopVersionSyncWatcher()
        {
            lock (_versionSyncWatchLock)
            {
                _versionSyncDebounce?.Cancel();
                _versionSyncDebounce?.Dispose();
                _versionSyncDebounce = null;

                if (_versionSyncWatcher == null)
                    return;
                _versionSyncWatcher.EnableRaisingEvents = false;
                _versionSyncWatcher.Created -= VersionSyncWatcher_OnChange;
                _versionSyncWatcher.Changed -= VersionSyncWatcher_OnChange;
                _versionSyncWatcher.Deleted -= VersionSyncWatcher_OnChange;
                _versionSyncWatcher.Renamed -= VersionSyncWatcher_OnRenamed;
                _versionSyncWatcher.Dispose();
                _versionSyncWatcher = null;
            }
        }

        private void VersionSyncWatcher_OnChange(object sender, FileSystemEventArgs e)
            => QueueVersionSync(e.FullPath);

        private void VersionSyncWatcher_OnRenamed(object sender, RenamedEventArgs e)
        {
            QueueVersionSync(e.OldFullPath);
            QueueVersionSync(e.FullPath);
        }

        private void QueueVersionSync(string path)
        {
            if (IsClosing) return;
            // Windows also reports changes to the metadata directory itself.
            var metadataPath = Path.Combine(ModProject.RootPath, ".pztools");
            if (string.Equals(path, metadataPath, StringComparison.OrdinalIgnoreCase) ||
                path.Contains(Path.DirectorySeparatorChar + ".pztools" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                path.Contains(".pztools-sync-", StringComparison.OrdinalIgnoreCase))
                return;

            lock (_versionSyncWatchLock)
            {
                if (IsClosing) return;
                _versionSyncDebounce?.Cancel();
                _versionSyncDebounce?.Dispose();
                _versionSyncDebounce = new CancellationTokenSource();
                _ = ReconcileVersionSyncDebouncedAsync(_versionSyncDebounce.Token);
            }
        }

        private async Task ReconcileVersionSyncDebouncedAsync(CancellationToken token)
        {
            try
            {
                // Superseded events are normal debounce control flow, not exceptions.
                await Task.Delay(250);
                if (token.IsCancellationRequested || IsClosing) return;
                var result = await Task.Run(() => VersionSyncService.ReconcileAll(ModProject));
                if (token.IsCancellationRequested || IsClosing) return;
                if (result.Copied + result.Updated + result.FoldersCreated > 0)
                    await Dispatcher.InvokeAsync(RefreshVersionSyncTree);
                await Dispatcher.InvokeAsync(() => { if (!IsClosing) RefreshInspector(); });
                var currentConflicts = result.ConflictPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var newConflicts = currentConflicts.Where(x => !_reportedVersionSyncConflicts.Contains(x)).Take(3).ToList();
                _reportedVersionSyncConflicts.Clear();
                _reportedVersionSyncConflicts.UnionWith(currentConflicts);
                if (newConflicts.Count > 0)
                    await WriteToConsole($"Version sync preserved a conflicting copy of '{string.Join(", ", newConflicts)}'. No differing file was overwritten.");
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                await WriteToConsole($"Version sync error: {ex.Message}");
            }
        }

        private void RefreshVersionSyncTree()
        {
            // Retain the bound nodes so selection, expansion and the preview survive sync.
            foreach (var target in ModProject.Targets)
            {
                if (target.FileTree != null && Directory.Exists(target.Path))
                    MergeVersionSyncChildren(target.FileTree, ProjectEngine.BuildFileTree(target.Path));
            }
        }

        private static void MergeVersionSyncChildren(
            PZTools.Core.Models.ProjectFileNode current, PZTools.Core.Models.ProjectFileNode incoming)
        {
            for (var index = 0; index < incoming.Children.Count; index++)
            {
                var next = incoming.Children[index];
                var existing = current.Children.FirstOrDefault(x =>
                    string.Equals(x.Path, next.Path, StringComparison.OrdinalIgnoreCase) && x.IsFolder == next.IsFolder);
                if (existing == null)
                    current.Children.Insert(index, next);
                else
                {
                    var previousIndex = current.Children.IndexOf(existing);
                    if (previousIndex != index) current.Children.Move(previousIndex, index);
                    if (existing.IsFolder) MergeVersionSyncChildren(existing, next);
                }
            }
            while (current.Children.Count > incoming.Children.Count)
                current.Children.RemoveAt(current.Children.Count - 1);
        }
    }
}
