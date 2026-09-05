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
            if (path.Contains(Path.DirectorySeparatorChar + ".pztools" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                path.Contains(".pztools-sync-", StringComparison.OrdinalIgnoreCase))
                return;

            lock (_versionSyncWatchLock)
            {
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
                await Task.Delay(250, token);
                var result = await Task.Run(() => VersionSyncService.ReconcileAll(ModProject), token);
                if (result.Copied + result.Updated + result.FoldersCreated > 0)
                    await Dispatcher.InvokeAsync(UpdateTreeView);
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
    }
}
