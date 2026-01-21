using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace PZTools
{
    public static class AppPaths
    {
        public static string CurrentFilePath
        {
            get
            {
                string path = Assembly.GetEntryAssembly()?.Location;

                if (string.IsNullOrEmpty(path))
                {
                    path = Process.GetCurrentProcess().MainModule?.FileName;
                }

                if (string.IsNullOrEmpty(path))
                    throw new Exception("Unable to determine executable path.");

                return Path.GetFullPath(path);
            }
        }

        public static DirectoryInfo CurrentDirectory => new DirectoryInfo(Directory.GetCurrentDirectory());

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
            Directory.CreateDirectory(path);
            return new DirectoryInfo(path);
        }

        public static string ConfigsDirectory => GetConfigDirectory().FullName;
    }
}
