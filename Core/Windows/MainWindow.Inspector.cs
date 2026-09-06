using System.Diagnostics;
using System.IO;
using System.Windows;
using PZTools.Core.Functions.Projects;

namespace PZTools.Core.Windows;

public partial class MainWindow
{
    private string? _inspectorPath;
    private bool _inspectorSyncBusy;

    private void InspectPath(string path)
    {
        if (!string.Equals(_inspectorPath, path, StringComparison.OrdinalIgnoreCase))
            InspectorSyncResult.Visibility = Visibility.Collapsed;
        _inspectorPath = path;
        RefreshInspector();
    }

    private void RefreshInspector()
    {
        if (_inspectorPath is not { } path) return;
        InspectorSyncEnabled.IsEnabled = false;
        InspectorSyncNow.IsEnabled = false;
        InspectorFileSettings.IsEnabled = false;
        try
        {
            var isFile = File.Exists(path);
            FileSystemInfo info = isFile ? new FileInfo(path) : new DirectoryInfo(path);
            if (!info.Exists)
            {
                InspectorSyncStatus.Text = "This item has moved or been deleted. Select it again in the explorer.";
                InspectorSyncEnabled.IsChecked = false;
                InspectorSyncTargets.Text = "";
                txtPropSize.Text = "—";
                txtPropEncoding.Text = "—";
                InspectorFileType.Text = "Item unavailable";
                InspectorModified.Text = "";
                InspectorCreated.Text = "";
                return;
            }
            InspectorFileSettings.Visibility = isFile ? Visibility.Visible : Visibility.Collapsed;
            InspectorFileSettings.IsEnabled = isFile;
            InspectorReadOnly.IsChecked = info.Attributes.HasFlag(FileAttributes.ReadOnly);
            InspectorHidden.IsChecked = info.Attributes.HasFlag(FileAttributes.Hidden);
            InspectorFileType.Text = isFile ? (info.Extension.Length > 0 ? $"{info.Extension.ToUpperInvariant()} file" : "File (no extension)") : "Folder";
            InspectorModified.Text = $"Modified: {info.LastWriteTime:g}";
            InspectorCreated.Text = $"Created: {info.CreationTime:g}";
            if (info is FileInfo file)
                txtPropSize.Text = file.Length < 1024 ? $"{file.Length:N0} B" : file.Length < 1024 * 1024 ? $"{file.Length / 1024d:N1} KB" : $"{file.Length / (1024d * 1024):N1} MB";

            var status = VersionSyncService.GetStatus(ModProject, path);
            var enabled = status is VersionSyncStatus.EnabledDirectly or VersionSyncStatus.EnabledByParent;
            InspectorSyncEnabled.IsChecked = enabled;
            InspectorSyncEnabled.IsEnabled = !_inspectorSyncBusy && (status is VersionSyncStatus.Available or VersionSyncStatus.EnabledDirectly);
            InspectorSyncNow.IsEnabled = !_inspectorSyncBusy && enabled;
            InspectorSyncNow.Content = _inspectorSyncBusy ? "Syncing…" : "Sync now";
            InspectorSyncStatus.Text = status switch
            {
                VersionSyncStatus.EnabledByParent => $"Inherited from {VersionSyncService.GetDirectRuleDisplayPath(ModProject, path)}. Select that folder to turn sync off. Sync now checks the whole folder rule.",
                VersionSyncStatus.EnabledDirectly => "Changes saved in any build are synced automatically. Differing copies are preserved for manual review.",
                VersionSyncStatus.Available => "Keep matching paths aligned across builds. Turning sync off keeps existing copies.",
                _ => "Sync is available for files and folders inside a build target, excluding shared common content and the target root."
            };
            InspectorSyncTargets.Text = status == VersionSyncStatus.NotAvailable ? "" :
                $"Targets: {string.Join(", ", ModProject.Targets.Select(x => x.BuildName))}" +
                (ModProject.Targets.Count < 2 ? ". Add another build target to sync copies; the rule will apply when one is added." : "");
            var conflicts = VersionSyncService.GetConflictPaths(ModProject, path);
            if (conflicts.Count > 0)
                InspectorSyncStatus.Text += $"\nConflict: {string.Join(", ", conflicts.Take(3))}. Make the copies match, then sync again.";
        }
        catch (Exception ex)
        {
            InspectorSyncStatus.Text = $"Could not read settings: {ex.Message}";
        }
    }

    private async void InspectorSyncEnabled_Click(object sender, RoutedEventArgs e)
        => await SetInspectorSyncAsync(InspectorSyncEnabled.IsChecked == true);

    private async void InspectorSyncNow_Click(object sender, RoutedEventArgs e)
        => await SetInspectorSyncAsync(true);

    private async Task SetInspectorSyncAsync(bool enabled)
    {
        if (_inspectorPath is not { } path || _inspectorSyncBusy) return;
        _inspectorSyncBusy = true;
        RefreshInspector();
        string message;
        try
        {
            if (enabled)
            {
                var result = await Task.Run(() => VersionSyncService.Enable(ModProject, path));
                message = $"Copied {result.Copied}, updated {result.Updated}, created {result.FoldersCreated} folders.";
                if (result.Conflicts > 0) message += $" {result.Conflicts} conflicts preserved. Review matching files in each build.";
                RefreshVersionSyncTree();
            }
            else
            {
                var disabled = await Task.Run(() => VersionSyncService.Disable(ModProject, path));
                message = disabled ? "Sync turned off. Existing copies were kept." : "No direct rule to remove. Folder sync is managed on the parent folder.";
            }
        }
        catch (Exception ex) { message = $"Sync failed: {ex.Message}"; }
        finally { _inspectorSyncBusy = false; }
        if (IsClosing) return;
        RefreshInspector();
        if (string.Equals(_inspectorPath, path, StringComparison.OrdinalIgnoreCase))
        {
            InspectorSyncResult.Text = message;
            InspectorSyncResult.Visibility = Visibility.Visible;
        }
    }

    private void InspectorAttribute_Click(object sender, RoutedEventArgs e)
    {
        if (_inspectorPath is not { } path || !File.Exists(path)) return;
        try
        {
            var attribute = ReferenceEquals(sender, InspectorReadOnly) ? FileAttributes.ReadOnly : FileAttributes.Hidden;
            var enabled = ReferenceEquals(sender, InspectorReadOnly) ? InspectorReadOnly.IsChecked == true : InspectorHidden.IsChecked == true;
            var attributes = File.GetAttributes(path);
            File.SetAttributes(path, enabled ? attributes | attribute : attributes & ~attribute);
            StatusBarText.Text = $"Updated {attribute} for {Path.GetFileName(path)}.";
        }
        catch (Exception ex) { StatusBarText.Text = $"Could not update file settings: {ex.Message}"; }
        RefreshInspector();
    }

    private void InspectorCopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (_inspectorPath is not { } path) return;
        try { System.Windows.Clipboard.SetText(path); StatusBarText.Text = "Path copied."; }
        catch (Exception ex) { StatusBarText.Text = $"Could not copy path: {ex.Message}"; }
    }

    private void InspectorReveal_Click(object sender, RoutedEventArgs e)
    {
        if (_inspectorPath is not { } path) return;
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException("This item no longer exists.");
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { StatusBarText.Text = $"Could not reveal item: {ex.Message}"; }
    }

    private void InspectorRefresh_Click(object sender, RoutedEventArgs e) => RefreshInspector();
}
