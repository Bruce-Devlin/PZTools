using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json;
using PZTools.Core.Models;

namespace PZTools.Core.Functions.Projects
{
    /// <summary>
    /// Keeps explicitly selected files or folders aligned between a project's build targets.
    /// The stored hash is a three-way merge baseline: a target is only changed when it still
    /// contains the last synchronized content. Divergent target content is never overwritten.
    /// </summary>
    public static class VersionSyncService
    {
        private const int CurrentFormatVersion = 1;
        private const string MetadataDirectoryName = ".pztools";
        private const string ManifestFileName = "version-sync.json";
        private static readonly object SyncLock = new();

        public static VersionSyncResult Enable(ModProject project, string sourcePath)
        {
            ArgumentNullException.ThrowIfNull(project);

            lock (SyncLock)
            {
                var source = ResolveTargetPath(project, sourcePath);
                var isFolder = Directory.Exists(source.FullPath);
                if (!isFolder && !File.Exists(source.FullPath))
                    throw new FileNotFoundException("Only an existing file or folder can be synchronized.", source.FullPath);
                RejectReparsePoint(source.FullPath);

                var manifest = Load(project);
                var existing = FindCoveringRule(manifest, source.RelativePath);
                if (existing != null)
                {
                    var existingResult = ReconcileRule(project, manifest, existing, source.Target.Build);
                    Save(project, manifest);
                    return existingResult;
                }

                var rule = new VersionSyncRule
                {
                    RelativePath = source.RelativePath,
                    IsFolder = isFolder
                };
                if (isFolder)
                    manifest.Rules.RemoveAll(x => Covers(rule, x.RelativePath));
                manifest.Rules.Add(rule);

                var result = ReconcileRule(project, manifest, rule, source.Target.Build);
                Save(project, manifest);
                return result;
            }
        }

        public static bool Disable(ModProject project, string path)
        {
            ArgumentNullException.ThrowIfNull(project);

            lock (SyncLock)
            {
                var resolved = ResolveTargetPath(project, path);
                var manifest = Load(project);
                var rule = manifest.Rules.FirstOrDefault(x =>
                    PathsEqual(x.RelativePath, resolved.RelativePath));
                if (rule == null)
                    return false;

                manifest.Rules.Remove(rule);
                Save(project, manifest);
                return true;
            }
        }

        public static VersionSyncStatus GetStatus(ModProject project, string path)
        {
            ArgumentNullException.ThrowIfNull(project);

            lock (SyncLock)
            {
                if (!TryResolveTargetPath(project, path, out var resolved))
                    return VersionSyncStatus.NotAvailable;

                var rule = FindCoveringRule(Load(project), resolved.RelativePath);
                if (rule == null)
                    return VersionSyncStatus.Available;
                return PathsEqual(rule.RelativePath, resolved.RelativePath)
                    ? VersionSyncStatus.EnabledDirectly
                    : VersionSyncStatus.EnabledByParent;
            }
        }

        public static VersionSyncResult ReconcileChange(ModProject project, string changedPath)
        {
            ArgumentNullException.ThrowIfNull(project);

            lock (SyncLock)
            {
                if (!TryResolveTargetPath(project, changedPath, out var changed))
                    return new VersionSyncResult();

                var manifest = Load(project);
                var rules = manifest.Rules
                    .Where(x => Covers(x, changed.RelativePath))
                    .ToList();
                if (rules.Count == 0)
                    return new VersionSyncResult();

                var result = new VersionSyncResult();
                foreach (var rule in rules)
                    result.Add(ReconcileRule(project, manifest, rule, changed.Target.Build));
                Save(project, manifest);
                return result;
            }
        }

        public static VersionSyncResult ReconcileAll(ModProject project)
        {
            ArgumentNullException.ThrowIfNull(project);

            lock (SyncLock)
            {
                var manifest = Load(project);
                var result = new VersionSyncResult();
                foreach (var rule in manifest.Rules)
                    result.Add(ReconcileRule(project, manifest, rule, null));
                if (manifest.Rules.Count > 0)
                    Save(project, manifest);
                return result;
            }
        }

        public static string? GetDirectRuleDisplayPath(ModProject project, string path)
        {
            lock (SyncLock)
            {
                if (!TryResolveTargetPath(project, path, out var resolved))
                    return null;
                var rule = FindCoveringRule(Load(project), resolved.RelativePath);
                return rule?.RelativePath.Replace('/', Path.DirectorySeparatorChar);
            }
        }

        public static IReadOnlyList<string> GetConflictPaths(ModProject project, string path)
        {
            lock (SyncLock)
            {
                if (!TryResolveTargetPath(project, path, out var resolved))
                    return Array.Empty<string>();
                var rule = FindCoveringRule(Load(project), resolved.RelativePath);
                return rule?.Files.Where(x => x.Value.HasConflict &&
                        (PathsEqual(x.Key, resolved.RelativePath) ||
                         x.Key.StartsWith(resolved.RelativePath.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase)))
                    .Select(x => x.Key).ToArray() ?? Array.Empty<string>();
            }
        }

        private static VersionSyncResult ReconcileRule(
            ModProject project,
            VersionSyncManifest manifest,
            VersionSyncRule rule,
            double? preferredSourceBuild)
        {
            var result = new VersionSyncResult();
            if (rule.IsFolder)
                ReconcileDirectories(project, rule, result);
            var logicalFiles = EnumerateLogicalFiles(project, rule).ToList();

            foreach (var relativePath in logicalFiles)
            {
                var copies = ExistingCopies(project, relativePath);
                if (copies.Count == 0)
                    continue;

                if (!rule.Files.TryGetValue(relativePath, out var state))
                {
                    var initialSource = ChooseSource(copies, preferredSourceBuild);
                    state = new VersionSyncFileState { BaselineHash = initialSource.Hash };
                    rule.Files[relativePath] = state;

                    foreach (var target in project.Targets)
                    {
                        var destination = Path.Combine(target.Path, FromManifestPath(relativePath));
                        var existingCopy = copies.FirstOrDefault(x => x.Target == target);
                        if (existingCopy == null)
                        {
                            if (TryCopyFileAtomically(initialSource.Path, destination, expectedDestinationHash: null, initialSource.Hash))
                                result.Copied++;
                            else
                                state.HasConflict = true;
                        }
                        else if (!HashesEqual(existingCopy.Hash, initialSource.Hash))
                        {
                            state.HasConflict = true;
                        }
                    }

                    if (state.HasConflict)
                    {
                        result.Conflicts++;
                        result.ConflictPaths.Add(relativePath);
                    }
                    continue;
                }

                var distinctHashes = copies.Select(x => x.Hash).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (state.HasConflict)
                {
                    if (distinctHashes.Count != 1)
                    {
                        result.Conflicts++;
                        result.ConflictPaths.Add(relativePath);
                        continue;
                    }

                    state.HasConflict = false;
                    state.BaselineHash = distinctHashes[0];
                }

                var changedHashes = distinctHashes
                    .Where(x => !HashesEqual(x, state.BaselineHash))
                    .ToList();
                if (changedHashes.Count > 1)
                {
                    state.HasConflict = true;
                    result.Conflicts++;
                    result.ConflictPaths.Add(relativePath);
                    continue;
                }

                var sourceHash = changedHashes.Count == 1 ? changedHashes[0] : state.BaselineHash;
                var source = copies.FirstOrDefault(x => HashesEqual(x.Hash, sourceHash)) ?? ChooseSource(copies, preferredSourceBuild);

                foreach (var target in project.Targets)
                {
                    var destination = Path.Combine(target.Path, FromManifestPath(relativePath));
                    var existingCopy = copies.FirstOrDefault(x => x.Target == target);
                    if (existingCopy == null)
                    {
                        if (TryCopyFileAtomically(source.Path, destination, expectedDestinationHash: null, sourceHash))
                            result.Copied++;
                        else
                        {
                            state.HasConflict = true;
                            result.Conflicts++;
                            result.ConflictPaths.Add(relativePath);
                            break;
                        }
                    }
                    else if (!HashesEqual(existingCopy.Hash, sourceHash))
                    {
                        if (!HashesEqual(existingCopy.Hash, state.BaselineHash))
                        {
                            state.HasConflict = true;
                            result.Conflicts++;
                            result.ConflictPaths.Add(relativePath);
                            break;
                        }

                        if (TryCopyFileAtomically(source.Path, destination, existingCopy.Hash, sourceHash))
                            result.Updated++;
                        else
                        {
                            state.HasConflict = true;
                            result.Conflicts++;
                            result.ConflictPaths.Add(relativePath);
                            break;
                        }
                    }
                }

                if (!state.HasConflict)
                    state.BaselineHash = sourceHash;
            }

            return result;
        }

        private static void ReconcileDirectories(ModProject project, VersionSyncRule rule, VersionSyncResult result)
        {
            var relativeDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rule.RelativePath };
            foreach (var target in project.Targets)
            {
                var root = Path.Combine(target.Path, FromManifestPath(rule.RelativePath));
                if (!Directory.Exists(root))
                    continue;
                RejectReparsePoint(root);
                foreach (var directory in EnumerateDirectoriesWithoutReparsePoints(root))
                    relativeDirectories.Add(ToManifestPath(Path.GetRelativePath(target.Path, directory)));
            }

            foreach (var relativeDirectory in relativeDirectories.OrderBy(x => x.Count(c => c == '/')).ThenBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                foreach (var target in project.Targets)
                {
                    var destination = Path.Combine(target.Path, FromManifestPath(relativeDirectory));
                    if (Directory.Exists(destination))
                        continue;
                    if (File.Exists(destination))
                    {
                        result.Conflicts++;
                        result.ConflictPaths.Add(relativeDirectory);
                        continue;
                    }
                    Directory.CreateDirectory(destination);
                    result.FoldersCreated++;
                }
            }
        }

        private static IEnumerable<string> EnumerateLogicalFiles(ModProject project, VersionSyncRule rule)
        {
            if (!rule.IsFolder)
            {
                yield return rule.RelativePath;
                yield break;
            }

            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var target in project.Targets.OrderByDescending(x => Path.GetFullPath(x.Path).Length))
            {
                var folder = Path.Combine(target.Path, FromManifestPath(rule.RelativePath));
                if (!Directory.Exists(folder))
                    continue;
                RejectReparsePoint(folder);

                foreach (var file in EnumerateFilesWithoutReparsePoints(folder))
                    files.Add(ToManifestPath(Path.GetRelativePath(target.Path, file)));
            }

            foreach (var file in rule.Files.Keys)
                files.Add(file);

            foreach (var file in files.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                yield return file;
        }

        private static IEnumerable<string> EnumerateFilesWithoutReparsePoints(string root)
        {
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                foreach (var file in Directory.EnumerateFiles(current))
                {
                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0)
                        yield return file;
                }
                foreach (var directory in Directory.EnumerateDirectories(current))
                {
                    if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0)
                        pending.Push(directory);
                }
            }
        }

        private static IEnumerable<string> EnumerateDirectoriesWithoutReparsePoints(string root)
        {
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                foreach (var directory in Directory.EnumerateDirectories(current))
                {
                    if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                        continue;
                    yield return directory;
                    pending.Push(directory);
                }
            }
        }

        private static List<TargetFileCopy> ExistingCopies(ModProject project, string relativePath)
        {
            var result = new List<TargetFileCopy>();
            foreach (var target in project.Targets)
            {
                var path = Path.Combine(target.Path, FromManifestPath(relativePath));
                if (!File.Exists(path))
                    continue;
                RejectReparsePoint(path);
                result.Add(new TargetFileCopy(target, path, Hash(path)));
            }
            return result;
        }

        private static TargetFileCopy ChooseSource(List<TargetFileCopy> copies, double? preferredBuild)
            => preferredBuild.HasValue
                ? copies.FirstOrDefault(x => x.Target.Build == preferredBuild.Value) ?? copies[0]
                : copies.OrderByDescending(x => x.Target.IsPrimary).ThenByDescending(x => x.Target.Build).First();

        private static VersionSyncRule? FindCoveringRule(VersionSyncManifest manifest, string relativePath)
            => manifest.Rules
                .Where(x => Covers(x, relativePath))
                .OrderByDescending(x => x.RelativePath.Length)
                .FirstOrDefault();

        private static bool Covers(VersionSyncRule rule, string relativePath)
        {
            if (PathsEqual(rule.RelativePath, relativePath))
                return true;
            return rule.IsFolder && relativePath.StartsWith(rule.RelativePath.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static ResolvedTargetPath ResolveTargetPath(ModProject project, string path)
        {
            if (TryResolveTargetPath(project, path, out var resolved))
                return resolved;
            throw new InvalidOperationException("Select a file or folder inside a version-specific build target. Common files already apply to every build.");
        }

        private static bool TryResolveTargetPath(ModProject project, string path, out ResolvedTargetPath resolved)
        {
            resolved = default;
            if (string.IsNullOrWhiteSpace(path))
                return false;

            string fullPath;
            try { fullPath = Path.GetFullPath(path); }
            catch { return false; }

            var commonRoot = Path.Combine(Path.GetFullPath(project.RootPath), "common");
            if (IsPathWithin(fullPath, commonRoot))
                return false;
            var metadataRoot = Path.Combine(Path.GetFullPath(project.RootPath), MetadataDirectoryName);
            if (IsPathWithin(fullPath, metadataRoot))
                return false;

            foreach (var target in project.Targets.OrderByDescending(x => Path.GetFullPath(x.Path).Length))
            {
                var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target.Path));
                var prefix = root + Path.DirectorySeparatorChar;
                if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                var relative = ToManifestPath(Path.GetRelativePath(root, fullPath));
                if (relative.StartsWith("../", StringComparison.Ordinal) || relative == "..")
                    return false;
                resolved = new ResolvedTargetPath(target, fullPath, relative);
                return true;
            }
            return false;
        }

        private static bool IsPathWithin(string path, string root)
        {
            var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            return string.Equals(path, normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static VersionSyncManifest Load(ModProject project)
        {
            var path = ManifestPath(project);
            if (!File.Exists(path))
                return new VersionSyncManifest();
            try
            {
                var manifest = JsonConvert.DeserializeObject<VersionSyncManifest>(File.ReadAllText(path)) ?? new VersionSyncManifest();
                manifest.Rules ??= new List<VersionSyncRule>();
                foreach (var rule in manifest.Rules)
                {
                    ValidateManifestPath(rule.RelativePath, "sync rule");
                    rule.Files = new Dictionary<string, VersionSyncFileState>(
                        rule.Files ?? new Dictionary<string, VersionSyncFileState>(),
                        StringComparer.OrdinalIgnoreCase);
                    foreach (var file in rule.Files.Keys)
                    {
                        ValidateManifestPath(file, "tracked file");
                        if (!Covers(rule, file))
                            throw new InvalidDataException($"Tracked file '{file}' is outside its version sync rule '{rule.RelativePath}'.");
                    }
                }
                return manifest;
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"The version sync manifest is not valid JSON: {path}", ex);
            }
        }

        private static void Save(ModProject project, VersionSyncManifest manifest)
        {
            manifest.FormatVersion = CurrentFormatVersion;
            var path = ManifestPath(project);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonConvert.SerializeObject(manifest, Formatting.Indented));
            File.Move(temp, path, overwrite: true);
        }

        private static string ManifestPath(ModProject project)
            => Path.Combine(project.RootPath, MetadataDirectoryName, ManifestFileName);

        private static void ValidateManifestPath(string path, string description)
        {
            if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
                throw new InvalidDataException($"The version sync {description} path must be relative.");
            var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Any(x => x is "." or ".." || x.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
                throw new InvalidDataException($"The version sync {description} path cannot leave a build target: {path}");
        }

        private static bool TryCopyFileAtomically(
            string source,
            string destination,
            string? expectedDestinationHash,
            string expectedSourceHash)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var temp = destination + ".pztools-sync-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.Copy(source, temp, overwrite: false);
                if (!HashesEqual(Hash(temp), expectedSourceHash))
                    throw new IOException($"The source changed while it was being synchronized: {source}");

                if (File.Exists(destination))
                {
                    var currentDestinationHash = Hash(destination);
                    if (expectedDestinationHash == null)
                        return HashesEqual(currentDestinationHash, expectedSourceHash);
                    if (!HashesEqual(currentDestinationHash, expectedDestinationHash))
                        return false;
                }
                else if (expectedDestinationHash != null)
                {
                    return false;
                }

                File.Move(temp, destination, overwrite: true);
                return true;
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
        }

        private static string Hash(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Convert.ToHexString(SHA256.HashData(stream));
        }

        private static void RejectReparsePoint(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Version sync does not follow symbolic links or reparse points: {path}");
        }

        private static bool HashesEqual(string left, string right)
            => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

        private static bool PathsEqual(string left, string right)
            => string.Equals(left.TrimEnd('/'), right.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

        private static string ToManifestPath(string path) => path.Replace('\\', '/').Trim('/');
        private static string FromManifestPath(string path) => path.Replace('/', Path.DirectorySeparatorChar);

        private sealed class VersionSyncManifest
        {
            public int FormatVersion { get; set; } = CurrentFormatVersion;
            public List<VersionSyncRule> Rules { get; set; } = new();
        }

        private sealed class VersionSyncRule
        {
            public string RelativePath { get; set; } = string.Empty;
            public bool IsFolder { get; set; }
            public Dictionary<string, VersionSyncFileState> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class VersionSyncFileState
        {
            public string BaselineHash { get; set; } = string.Empty;
            public bool HasConflict { get; set; }
        }

        private readonly record struct ResolvedTargetPath(ModTarget Target, string FullPath, string RelativePath);
        private sealed record TargetFileCopy(ModTarget Target, string Path, string Hash);
    }

    public enum VersionSyncStatus
    {
        NotAvailable,
        Available,
        EnabledDirectly,
        EnabledByParent
    }

    public sealed class VersionSyncResult
    {
        public int Copied { get; internal set; }
        public int Updated { get; internal set; }
        public int FoldersCreated { get; internal set; }
        public int Conflicts { get; internal set; }
        public List<string> ConflictPaths { get; } = new();

        internal void Add(VersionSyncResult other)
        {
            Copied += other.Copied;
            Updated += other.Updated;
            FoldersCreated += other.FoldersCreated;
            Conflicts += other.Conflicts;
            ConflictPaths.AddRange(other.ConflictPaths);
        }
    }
}
