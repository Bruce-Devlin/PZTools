using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace PZTools
{
    public static class AppPaths
    {
        /// <summary>Path to the running executable</summary>
        public static string CurrentFilePath
        {
            get
            {
                // First try entry assembly
                string path = Assembly.GetEntryAssembly()?.Location;

                // Fallback for single-file publish or if Location is empty
                if (string.IsNullOrEmpty(path))
                {
                    path = Process.GetCurrentProcess().MainModule?.FileName;
                }

                if (string.IsNullOrEmpty(path))
                    throw new Exception("Unable to determine executable path.");

                return Path.GetFullPath(path);
            }
        }

        /// <summary>Directory containing the exe</summary>
        public static DirectoryInfo CurrentDirectory => new DirectoryInfo(Directory.GetCurrentDirectory());

        /// <summary>Configs folder next to exe</summary>
        public static DirectoryInfo ConfigDirectory
        {
            get
            {
                var dir = new DirectoryInfo(Path.Combine(CurrentDirectory.FullName, "Configs"));
                if (!dir.Exists) dir.Create();
                return dir;
            }
        }

        public static string CurrentDirectoryPath => CurrentDirectory.FullName;

        public static void SetCurrentDirectory(string path)
        {
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);

            Directory.SetCurrentDirectory(path);
        }

        public static DirectoryInfo GetConfigDirectory()
        {
            string path = Path.Combine(CurrentDirectoryPath, "Configs");
            Directory.CreateDirectory(path); // ensure it exists
            return new DirectoryInfo(path);
        }

        public static string ConfigsDirectory => GetConfigDirectory().FullName;
    }
}
