using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace PZTools.Core.Functions.Decompile
{
    internal static class JavaDecompiler
    {
        private const string CFR_URL = "https://www.benf.org/other/cfr/cfr-0.152.jar";

        public static string ToolsDirectory =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools");

        public static string CfrJarPath =>
            Path.Combine(ToolsDirectory, "cfr.jar");

        public static async Task EnsureCfrInstalledAsync()
        {
            Directory.CreateDirectory(ToolsDirectory);

            if (File.Exists(CfrJarPath))
                return;

            using var http = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(2)
            };

            byte[] data = await http.GetByteArrayAsync(CFR_URL);

            if (data.Length < 1024 * 500)
                throw new Exception("Downloaded CFR file is too small – download failed.");

            await File.WriteAllBytesAsync(CfrJarPath, data);
        }


        public static async Task<bool> DecompileJarAsync(
            string javaExePath,
            string cfrJarPath,
            string inputJarPath,
            string outputDirectory,
            int memoryMb = 6144,
            Action<string>? onOutput = null,
            Action<string>? onError = null,
            CancellationToken cancellationToken = default)
        {
            if (!File.Exists(javaExePath))
                throw new FileNotFoundException("Java executable not found", javaExePath);

            if (!File.Exists(cfrJarPath))
                throw new FileNotFoundException("CFR jar not found", cfrJarPath);

            if (!File.Exists(inputJarPath))
                throw new FileNotFoundException("Input jar not found", inputJarPath);

            Directory.CreateDirectory(outputDirectory);

            var args =
                $"-Xmx{memoryMb}M -jar \"{cfrJarPath}\" \"{inputJarPath}\" --outputdir \"{outputDirectory}\"";

            var psi = new ProcessStartInfo
            {
                FileName = javaExePath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    onOutput?.Invoke(e.Data);
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    onError?.Invoke(e.Data);
            };

            try
            {
                process.Start();
            }
            catch (Win32Exception ex)
            {
                onError?.Invoke(
                    $"Failed to start process. NativeErrorCode={ex.NativeErrorCode}, Message={ex.Message}\n" +
                    $"FileName: {psi.FileName}\nArguments: {psi.Arguments}\nWorkingDirectory: {psi.WorkingDirectory}"
                );
                throw;
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested) return false;

            return process.ExitCode == 0;
        }

        public static string? FindJavaExecutable()
        {
            var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrEmpty(javaHome))
            {
                var javaPath = Path.Combine(javaHome, "bin", "java.exe");
                if (File.Exists(javaPath))
                    return javaPath;
            }

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            foreach (var baseDir in new[] { programFiles, programFilesX86 })
            {
                if (!Directory.Exists(baseDir)) continue;

                foreach (var dir in Directory.GetDirectories(baseDir, "Java*", SearchOption.TopDirectoryOnly))
                {
                    var javaExe = Path.Combine(dir, "bin", "java.exe");
                    if (File.Exists(javaExe))
                        return javaExe;
                }
            }

            return null;
        }
    }
}
