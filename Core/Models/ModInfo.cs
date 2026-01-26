using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PZTools.Core.Models
{
    public sealed class ModInfo
    {
        // Required
        public string Name { get; set; } = "";
        public string Id { get; set; } = "";

        // Optional (single)
        public string? Author { get; set; }
        public string? Description { get; set; }
        public string? Url { get; set; }
        public string? Icon { get; set; }
        public string? ModVersion { get; set; }
        public string? Category { get; set; }
        public string? VersionMin { get; set; }
        public string? VersionMax { get; set; }

        // Optional (repeatable / list)
        public List<string> Posters { get; } = new();
        public List<string> Requires { get; } = new();
        public List<string> Incompatibles { get; } = new();
        public List<string> LoadModAfter { get; } = new();
        public List<string> LoadModBefore { get; } = new();
        public List<string> Packs { get; } = new();
        public List<string> Tiledefs { get; } = new();

        public IEnumerable<string> ToModInfoLines()
        {
            var lines = new List<string>();

            void AddKV(string key, string? value)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    lines.Add($"{key}={value.Trim()}");
            }

            if (string.IsNullOrWhiteSpace(Name))
                throw new InvalidOperationException("mod.info requires 'name'.");
            if (string.IsNullOrWhiteSpace(Id))
                throw new InvalidOperationException("mod.info requires 'id'.");

            AddKV("name", Name);
            AddKV("id", Id);

            AddKV("author", Author);
            AddKV("description", Description);
            AddKV("url", Url);
            AddKV("icon", Icon);
            AddKV("modversion", ModVersion);
            AddKV("category", Category);

            foreach (var p in Posters.Where(x => !string.IsNullOrWhiteSpace(x)))
                AddKV("poster", p);

            foreach (var p in Packs.Where(x => !string.IsNullOrWhiteSpace(x)))
                AddKV("pack", p);

            foreach (var t in Tiledefs.Where(x => !string.IsNullOrWhiteSpace(x)))
                AddKV("tiledef", t);

            AddList("require", Requires);
            AddList("incompatible", Incompatibles);
            AddList("loadModAfter", LoadModAfter);
            AddList("loadModBefore", LoadModBefore);

            AddKV("versionMin", NormalizeBuildVersion(VersionMin));
            AddKV("versionMax", NormalizeBuildVersion(VersionMax));

            return lines;

            void AddList(string key, List<string> values)
            {
                var cleaned = values
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (cleaned.Count == 0) return;

                var joined = string.Join(",", cleaned.Select(x => x.StartsWith("\\") ? x : "\\" + x));
                lines.Add($"{key}={joined}");
            }

            static string? NormalizeBuildVersion(string? v)
            {
                if (string.IsNullOrWhiteSpace(v)) return null;

                var s = v.Trim();

                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var build))
                    return build.ToString(CultureInfo.InvariantCulture) + ".0";

                if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    return s;

                return s;
            }
        }
    }

    public static class ModInfoUtil
    {
        public static string NormalizeModId(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";

            var chars = input
                .Trim()
                .Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-')
                .ToArray();

            var id = new string(chars);
            return string.IsNullOrWhiteSpace(id) ? "" : id;
        }
    }
}
