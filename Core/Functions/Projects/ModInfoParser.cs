using PZTools.Core.Models;
using System.IO;

namespace PZTools.Core.Functions.Projects
{
    public static class ModInfoParser
    {
        public static ModInfo Load(string modFolderPath)
        {
            if (string.IsNullOrWhiteSpace(modFolderPath))
                throw new ArgumentException("Mod folder path is required.", nameof(modFolderPath));

            var infoPath = Path.Combine(modFolderPath, "mod.info");
            if (!File.Exists(infoPath))
                return new ModInfo();

            var info = new ModInfo();

            foreach (var raw in File.ReadAllLines(infoPath))
            {
                var line = raw?.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith("#") || line.StartsWith("//")) continue;

                var idx = line.IndexOf('=');
                if (idx <= 0) continue;

                var key = line.Substring(0, idx).Trim();
                var value = line.Substring(idx + 1).Trim();

                if (string.IsNullOrWhiteSpace(value))
                    value = "";

                switch (key)
                {
                    case "name": info.Name = value; break;
                    case "id": info.Id = value; break;

                    case "author": info.Author = EmptyToNull(value); break;
                    case "description": info.Description = EmptyToNull(value); break;
                    case "url": info.Url = EmptyToNull(value); break;
                    case "icon": info.Icon = EmptyToNull(value); break;
                    case "modversion": info.ModVersion = EmptyToNull(value); break;
                    case "category": info.Category = EmptyToNull(value); break;
                    case "versionMin": info.VersionMin = EmptyToNull(value); break;
                    case "versionMax": info.VersionMax = EmptyToNull(value); break;

                    case "poster":
                        if (!string.IsNullOrWhiteSpace(value))
                            info.Posters.Add(value);
                        break;
                    case "pack":
                        if (!string.IsNullOrWhiteSpace(value))
                            info.Packs.Add(value);
                        break;
                    case "tiledef":
                        if (!string.IsNullOrWhiteSpace(value))
                            info.Tiledefs.Add(value);
                        break;

                    case "require":
                        ParseListInto(info.Requires, value);
                        break;
                    case "incompatible":
                        ParseListInto(info.Incompatibles, value);
                        break;
                    case "loadModAfter":
                        ParseListInto(info.LoadModAfter, value);
                        break;
                    case "loadModBefore":
                        ParseListInto(info.LoadModBefore, value);
                        break;
                }
            }

            Normalize(info);
            return info;
        }

        public static void Save(string modFolderPath, ModInfo info)
        {
            if (string.IsNullOrWhiteSpace(modFolderPath))
                throw new ArgumentException("Mod folder path is required.", nameof(modFolderPath));
            if (info == null)
                throw new ArgumentNullException(nameof(info));

            var infoPath = Path.Combine(modFolderPath, "mod.info");
            Directory.CreateDirectory(modFolderPath);

            Normalize(info);

            info.Name = info.Name?.Trim() ?? "";
            info.Id = info.Id?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(info.Name))
                throw new InvalidOperationException("mod.info requires 'name'.");
            if (string.IsNullOrWhiteSpace(info.Id))
                throw new InvalidOperationException("mod.info requires 'id'.");

            File.WriteAllLines(infoPath, info.ToModInfoLines());
        }

        /// <summary>
        /// Copies an external file into the mod folder and returns the filename to store in mod.info.
        /// If the file is already inside the mod folder, no copy is performed.
        /// </summary>
        public static string? EnsureLocalAsset(string modFolderPath, string? sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath)) return null;
            if (!File.Exists(sourcePath)) return null;

            var fileName = Path.GetFileName(sourcePath);
            var destPath = Path.Combine(modFolderPath, fileName);

            if (Path.GetFullPath(sourcePath).Equals(Path.GetFullPath(destPath), StringComparison.OrdinalIgnoreCase))
                return fileName;

            File.Copy(sourcePath, destPath, overwrite: true);
            return fileName;
        }

        public static string? ResolveAssetPath(string modFolderPath, string? relativeOrPath)
        {
            if (string.IsNullOrWhiteSpace(relativeOrPath)) return null;

            var combined = Path.GetFullPath(Path.Combine(modFolderPath, relativeOrPath));
            return File.Exists(combined) ? combined : null;
        }

        private static void ParseListInto(List<string> target, string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;

            var parts = raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                           .Select(x => x.Trim())
                           .Select(x => x.StartsWith("\\") ? x.Substring(1) : x)
                           .Where(x => !string.IsNullOrWhiteSpace(x));

            target.AddRange(parts);
        }

        private static void Normalize(ModInfo info)
        {
            Dedup(info.Posters);
            Dedup(info.Packs);
            Dedup(info.Tiledefs);

            Dedup(info.Requires);
            Dedup(info.Incompatibles);
            Dedup(info.LoadModAfter);
            Dedup(info.LoadModBefore);

            info.Author = TrimToNull(info.Author);
            info.Description = TrimToNull(info.Description);
            info.Url = TrimToNull(info.Url);
            info.Icon = TrimToNull(info.Icon);
            info.ModVersion = TrimToNull(info.ModVersion);
            info.Category = TrimToNull(info.Category);
            info.VersionMin = TrimToNull(info.VersionMin);
            info.VersionMax = TrimToNull(info.VersionMax);
        }

        private static void Dedup(List<string> list)
        {
            if (list.Count == 0) return;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var cleaned = new List<string>(list.Count);

            foreach (var item in list.Select(x => x?.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                if (seen.Add(item!))
                    cleaned.Add(item!);
            }

            list.Clear();
            list.AddRange(cleaned);
        }

        private static string? EmptyToNull(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
        private static string? TrimToNull(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return s.Trim();
        }
    }
}
