using System.IO;
using System.Text;
using PZTools.Core.Models;

namespace PZTools.Core.Functions.Projects
{
    public static class PlaytestClientConfig
    {
        private static readonly string[] PreferenceFiles = { "options.ini", "keys.ini" };

        public static string Configure(string cachePath, PlaytestProfile profile, string? sourceUserDirectory = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(cachePath);
            ArgumentNullException.ThrowIfNull(profile);
            Directory.CreateDirectory(cachePath);

            if (!string.IsNullOrWhiteSpace(sourceUserDirectory) && Directory.Exists(sourceUserDirectory))
            {
                foreach (var name in PreferenceFiles)
                {
                    var source = Path.Combine(sourceUserDirectory, name);
                    var destination = Path.Combine(cachePath, name);
                    if (File.Exists(source) && !File.Exists(destination))
                        File.Copy(source, destination);
                }
            }

            var optionsPath = Path.Combine(cachePath, "options.ini");
            var lines = File.Exists(optionsPath)
                ? File.ReadAllLines(optionsPath).ToList()
                : new List<string> { "version=8" };

            Set(lines, "fullScreen", profile.WindowMode == PlaytestWindowMode.Fullscreen ? "true" : "false");
            Set(lines, "borderless", profile.WindowMode == PlaytestWindowMode.BorderlessWindowed ? "true" : "false");
            Set(lines, "width", profile.WindowWidth.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Set(lines, "height", profile.WindowHeight.ToString(System.Globalization.CultureInfo.InvariantCulture));
            File.WriteAllLines(optionsPath, lines, new UTF8Encoding(false));
            return optionsPath;
        }

        private static void Set(List<string> lines, string key, string value)
        {
            var prefix = key + "=";
            var index = lines.FindIndex(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
                lines[index] = prefix + value;
            else
                lines.Add(prefix + value);
        }
    }
}
