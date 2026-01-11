using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text;

namespace PZTools.Core.Functions
{
    static class WindowsHelpers
    {
        public static bool ShowDialog(this System.Windows.Window parentWindow, System.Windows.Window child)
        {
            System.Windows.Window window = child;
            window.Owner = parentWindow;
            var result = window.ShowDialog();
            if (result.HasValue && result is bool) return (bool)result;
            else return true;
        }

        public static void CreateShortcut(string shortcutPath, string targetPath, string description)
        {
            var shell = new IWshRuntimeLibrary.WshShell();
            var shortcut = (IWshRuntimeLibrary.IWshShortcut)shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = targetPath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
            shortcut.Description = description;
            shortcut.Save();
        }

        public static bool NotNullOrEmpty(this string value)
        {
            return !string.IsNullOrEmpty(value);
        }

        public static string OpenFile(string filePath)
        {
            try
            {
                var process = new System.Diagnostics.Process();
                process.StartInfo = new System.Diagnostics.ProcessStartInfo()
                {
                    FileName = filePath,
                    UseShellExecute = true,
                };
                process.Start();
                return "";
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        public static string GetTargetFrameworkMoniker()
        {
            var assembly = Assembly.GetEntryAssembly();
            var attribute = assembly.GetCustomAttribute<TargetFrameworkAttribute>();
            if (attribute == null) return "unknown";

            string frameworkName = attribute.FrameworkName;
            // Example: ".NETCoreApp,Version=v10.0,Profile=Windows"

            // Split by commas
            var parts = frameworkName.Split(',');
            if (parts.Length < 2) return "unknown";

            // Map base framework
            string baseFramework = parts[0].Trim(); // ".NETCoreApp" or ".NETFramework"
            string tfm = "";

            switch (baseFramework)
            {
                case ".NETCoreApp":
                    tfm = "net";
                    break;
                case ".NETFramework":
                    tfm = "netframework";
                    break;
                case ".NETStandard":
                    tfm = "netstandard";
                    break;
                default:
                    tfm = baseFramework.ToLowerInvariant();
                    break;
            }

            // Extract version
            string versionPart = parts[1].Trim(); // "Version=v10.0"
            if (versionPart.StartsWith("Version=v"))
            {
                string version = versionPart.Substring("Version=v".Length);
                tfm += version;
            }

            // Check for Profile (like windows)
            if (parts.Length >= 3)
            {
                string profile = parts[2].Trim(); // "Profile=Windows"
                if (profile.StartsWith("Profile=", StringComparison.OrdinalIgnoreCase))
                {
                    string profileName = profile.Substring("Profile=".Length).ToLowerInvariant();
                    tfm += "-" + profileName;
                }
            }

            return tfm; // e.g. "net10.0-windows"
        }
    }
}
