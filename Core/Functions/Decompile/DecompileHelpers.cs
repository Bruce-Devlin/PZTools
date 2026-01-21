using System.IO;
using System.Windows;

namespace PZTools.Core.Functions.Decompile
{
    internal class JavaDecompilerHelpers
    {
        public static EventHandler<string> UpdateSetupStatus = new EventHandler<string>(delegate { });
        public static void ClearUpdateStatusEvents()
        {
            UpdateSetupStatus = new EventHandler<string>(delegate { });
        }

        public static async Task<bool> DecompileGame(string gamePath, string build = "")
        {
            string existingJarPath = Path.Combine(gamePath, "projectzomboid.jar");
            string zomboidRoot = Path.Combine(AppPaths.CurrentDirectoryPath, "Zomboid");
            if (!Directory.Exists(zomboidRoot)) Directory.CreateDirectory(zomboidRoot);

            string sourcePath = Path.Combine(zomboidRoot, "Source");
            if (!Directory.Exists(sourcePath)) Directory.CreateDirectory(sourcePath);

            if (string.IsNullOrEmpty(build)) sourcePath = Path.Combine(sourcePath, build);

            File.Copy(existingJarPath, Path.Combine(sourcePath, "projectzomboid.jar"), true);
            await Console.Log("Source files copied from existing installation.");

            string managedJar = Path.Combine(sourcePath, "projectzomboid.jar");
            string cfrPath = JavaDecompiler.CfrJarPath;

            await JavaDecompiler.DecompileJarAsync(
                existingJarPath,
                cfrPath,
                managedJar,
                sourcePath,
                onOutput: msg => UpdateSetupStatus.Invoke(null, $"Decompiler: {msg}"),
                onError: msg => UpdateSetupStatus.Invoke(null, $"Decompiler Error: {msg}")
            );

            File.Delete(managedJar);

            if (!Directory.Exists(Path.Combine(sourcePath, "zombie")))
            {
                MessageBox.Show("Decompilation failed! Source files not found after decompilation.", "Decompilation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            else return true;
        }
    }
}
