using System.IO;
using Newtonsoft.Json;
using PZTools.Core.Models;

namespace PZTools.Core.Functions.Projects;

public sealed record DeploymentPreviewEntry(string Path, bool Included);
public sealed record DeploymentSnapshotPayload(string RootPath, string ModId);
public sealed record DeploymentSnapshot(string Id, string ManifestPath, long Bytes,
    IReadOnlyList<DeploymentSnapshotPayload> Payloads);

/// <summary>Captures all local payloads while holding read locks, then releases sources before endpoint deployment.</summary>
public static class DeploymentSnapshotService
{
    public static IReadOnlyList<DeploymentPreviewEntry> Preview(string root)
    {
        ProjectDeployer.RejectLinkedPath(root);
        var entries = new List<DeploymentPreviewEntry>();
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.TryPop(out var current))
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(current))
            {
                var relative = Path.GetRelativePath(root, path);
                var included = ProjectDeployer.ShouldIncludePath(relative);
                if (!included) { entries.Add(new(relative, false)); continue; }
                ProjectDeployer.RejectLinkedPath(path);
                if (Directory.Exists(path)) pending.Push(path);
                else entries.Add(new(relative, true));
            }
        }
        return entries.OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static DeploymentSnapshot Capture(ModProject project, PlaytestProfile profile, string workspace,
        CancellationToken ct, Action? sourcesLocked = null)
    {
        var id = Guid.NewGuid().ToString("N");
        var destination = Path.Combine(workspace, "source-snapshot-" + id);
        ProjectDeployer.RejectLinkedPath(destination);
        var sources = new List<(string Root, string Folder, string ModId)>
        { (Path.GetFullPath(project.RootPath), new DirectoryInfo(project.RootPath).Name, project.ModInfo.Id) };
        var external = new List<string>();
        foreach (var dep in profile.Dependencies.Where(d => d.Enabled))
        {
            if (profile.Mode == PlaytestMode.DedicatedServer && !profile.NoSteam && !string.IsNullOrWhiteSpace(dep.WorkshopId))
            { external.Add(dep.ModId + ": Steam Workshop content is outside the local snapshot guarantee."); continue; }
            if (!Directory.Exists(dep.SourcePath)) throw new DirectoryNotFoundException("Local dependency source is missing: " + dep.SourcePath);
            var folder = ModInfoUtil.NormalizeModId(dep.ModId);
            if (string.IsNullOrWhiteSpace(folder)) folder = new DirectoryInfo(dep.SourcePath).Name;
            sources.Add((Path.GetFullPath(dep.SourcePath), folder, dep.ModId));
        }
        if (sources.GroupBy(s => s.Folder, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            throw new InvalidDataException("Local payloads have conflicting destination folder names.");
        foreach (var source in sources)
        {
            if (source.Folder is "." or ".." || Path.GetFileName(source.Folder) != source.Folder)
                throw new InvalidDataException("Invalid payload folder: " + source.Folder);
            var relative = Path.GetRelativePath(source.Root, destination);
            if (!relative.StartsWith(".." + Path.DirectorySeparatorChar) && !Path.IsPathRooted(relative) && ProjectDeployer.ShouldIncludePath(relative))
                throw new InvalidOperationException("Snapshot destination must be outside included source content.");
        }

        var inventories = sources.Select(s => Preview(s.Root)).ToArray();
        var held = new List<(int Source, string Relative, FileStream Stream)>();
        try
        {
            for (var i = 0; i < sources.Count; i++)
                foreach (var entry in inventories[i].Where(e => e.Included))
                {
                    ct.ThrowIfCancellationRequested();
                    var path = Path.Combine(sources[i].Root, entry.Path);
                    try
                    {
                        ProjectDeployer.RejectLinkedPath(path);
                        // Deny writes and deletion for the entire capture, including all dependencies.
                        held.Add((i, entry.Path, new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)));
                    }
                    catch (IOException ex) { throw new IOException("Runtime source changed or is being written during snapshot: " + path, ex); }
                }
            sourcesLocked?.Invoke();
            Directory.CreateDirectory(destination);
            long bytes = 0;
            foreach (var file in held)
            {
                ct.ThrowIfCancellationRequested();
                var target = Path.Combine(destination, sources[file.Source].Folder, file.Relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                file.Stream.CopyTo(output); bytes += output.Length;
            }
            for (var i = 0; i < sources.Count; i++)
            {
                var before = inventories[i].Where(e => e.Included).Select(e => e.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var after = ProjectDeployer.EnumeratePayloadFiles(sources[i].Root).Select(p => Path.GetRelativePath(sources[i].Root, p)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var changed = before.Except(after).Concat(after.Except(before)).FirstOrDefault();
                if (changed is not null) throw new IOException("Runtime source changed during snapshot: " + Path.Combine(sources[i].Root, changed));
            }
            var payloads = sources.Select(s => new DeploymentSnapshotPayload(Path.Combine(destination, s.Folder), s.ModId)).ToArray();
            foreach (var payload in payloads)
            {
                Directory.CreateDirectory(payload.RootPath);
                DeploymentManifestService.Write(payload.RootPath, payload.ModId, id);
            }
            var manifestPath = Path.Combine(destination, "snapshot.json");
            File.WriteAllText(manifestPath, JsonConvert.SerializeObject(new
            {
                sourceSnapshotId = id, bytes, externalDependencies = external,
                payloads = sources.Select((s, i) => new { source = s.Root, folder = s.Folder, modId = s.ModId,
                    manifest = Path.Combine(s.Folder, DeploymentManifestService.ManifestFileName), preview = inventories[i] })
            }, Formatting.Indented));
            return new(id, manifestPath, bytes, payloads);
        }
        finally { foreach (var file in held) file.Stream.Dispose(); }
    }
}
