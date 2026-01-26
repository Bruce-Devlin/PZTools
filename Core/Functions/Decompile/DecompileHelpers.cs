using System.IO;
using System.Windows;

namespace PZTools.Core.Functions.Decompile
{
    internal class JavaDecompilerHelpers
    {
        public static EventHandler<string> OnDecompilerMessage = new EventHandler<string>(delegate { });
        public static void ClearDecompilerMessageEvents()
        {
            OnDecompilerMessage = new EventHandler<string>(delegate { });
        }

        public static async Task<bool> DecompileGame(string gamePath, string build = "", CancellationToken cancellationToken = default)
        {
            try
            {
                await JavaDecompiler.EnsureCfrInstalledAsync();
            }
            catch (Exception ex)
            {
                await Console.Log("Failed to install CFR: " + ex.Message, Console.LogLevel.Error);
                return false;
            }

            cancellationToken.ThrowIfCancellationRequested();

            string existingJarPath = Path.Combine(gamePath, "projectzomboid.jar");
            string zomboidRoot = Path.Combine(AppPaths.CurrentDirectoryPath, "Zomboid");
            Directory.CreateDirectory(zomboidRoot);

            string sourceRoot = Path.Combine(zomboidRoot, "Source");
            Directory.CreateDirectory(sourceRoot);

            string sourcePath = string.IsNullOrWhiteSpace(build)
                ? sourceRoot
                : Path.Combine(sourceRoot, build);

            Directory.CreateDirectory(sourcePath);

            string managedJar = Path.Combine(sourcePath, "projectzomboid.jar");
            File.Copy(existingJarPath, managedJar, true);
            await Console.Log("Source files copied from existing installation.");

            string cfrPath = JavaDecompiler.CfrJarPath;
            string? javaPath = JavaDecompiler.FindJavaExecutable();
            if (string.IsNullOrWhiteSpace(javaPath))
            {
                await Console.Log("Unable to decompile Jar files without JAVA. Please install Java and then try again.", Console.LogLevel.Error);
                return false;
            }

            try
            {
                var success = await JavaDecompiler.DecompileJarAsync(
                    javaPath,
                    cfrPath,
                    managedJar,
                    sourcePath,
                    onOutput: msg => OnDecompilerMessage.Invoke(null, $"Java Decompiler: {msg}"),
                    onError: msg => OnDecompilerMessage.Invoke(null, $"Java Decompiler: {msg}"),
                    cancellationToken: cancellationToken
                );

                if (!success) return false;
            }
            catch (OperationCanceledException)
            {
                await Console.Log("Decompilation cancelled.", Console.LogLevel.Warning);
                return false;
            }
            finally
            {
                try { if (File.Exists(managedJar)) File.Delete(managedJar); } catch { }
            }

            if (!Directory.Exists(Path.Combine(sourcePath, "zombie")))
            {
                MessageBox.Show(
                    "Decompilation failed! Source files not found after decompilation.",
                    "Decompilation Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                return false;
            }

            return true;
        }
    }
}
