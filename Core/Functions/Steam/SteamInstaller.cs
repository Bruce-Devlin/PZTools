using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace PZTools.Core.Functions.Steam
{
    class SteamInstaller
    {
        public string SteamCMDDirectory { get; private set; } = "";
        private string steamCMDExe => Path.Combine(SteamCMDDirectory, "steamcmd.exe");

        public EventHandler<string> onSteamMessage = new EventHandler<string>(delegate { });

        private async void onMessage(string message)
        {
            await Console.Log(message);
            onSteamMessage.Invoke(this, message);
        }

        public async Task SetupSteamCMD(string steamCMDDirectory)
        {
            await Console.Log($"Installing SteamCMD...");

            SteamCMDDirectory = steamCMDDirectory;
            if (!Directory.Exists(SteamCMDDirectory))
                Directory.CreateDirectory(SteamCMDDirectory);

            string steamCmdExe = Path.Combine(SteamCMDDirectory, "steamcmd.exe");

            if (!File.Exists(steamCmdExe))
            {
                onMessage("Downloading SteamCMD...");
                using (var client = new HttpClient())
                {
                    var steamCmdZipUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip";
                    var zipBytes = await client.GetByteArrayAsync(steamCmdZipUrl);
                    var zipPath = Path.Combine(SteamCMDDirectory, "steamcmd.zip");
                    await File.WriteAllBytesAsync(zipPath, zipBytes);

                    onMessage("Extracting SteamCMD...");
                    System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, SteamCMDDirectory, true);
                    File.Delete(zipPath);
                }
            }

            onMessage("Installing SteamCMD...");
            var setupSteamCMD = new ProcessStartInfo
            {
                FileName = steamCmdExe,
                WorkingDirectory = SteamCMDDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                Arguments = "+quit"
            };


            using var setupProc = Process.Start(setupSteamCMD);
            setupProc.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    onMessage(e.Data);
            };

            setupProc.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    onMessage(e.Data);
            };

            setupProc.WaitForExit();
        }

        public async Task<bool> InstallApp(
             string appId,
             string installDir,
             string username,
             string beta = "",
             CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(SteamCMDDirectory))
                throw new ArgumentNullException(nameof(SteamCMDDirectory),
                    "Setup SteamCMD first! (SteamInstaller.SetupSteamCMD().InstallApp())");

            cancellationToken.ThrowIfCancellationRequested();

            await Console.Log($"Installing {appId} [beta={beta}]...");

            Directory.CreateDirectory(installDir);

            var betaArgs = string.IsNullOrWhiteSpace(beta) ? "" : $"-beta {beta}";

            onMessage($"Installing {appId} [beta={beta}]...");

            var startInfo = new ProcessStartInfo
            {
                FileName = steamCMDExe,
                WorkingDirectory = SteamCMDDirectory,

                UseShellExecute = false,
                CreateNoWindow = true,

                Arguments =
                    $"+login {username} " +
                    $"+force_install_dir \"{installDir}\" " +
                    $"+app_update {appId} {betaArgs} validate " +
                    $"+quit"
            };

            using var proc = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

            try
            {
                if (!proc.Start())
                    return false;

                await proc.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                TryKillProcessTree(proc);
                return false;
            }

            if (proc.ExitCode != 0)
                return false;

            return File.Exists(Path.Combine(installDir, "projectzomboid.jar"));
        }

        private static void TryKillProcessTree(Process proc)
        {
            try
            {
                if (!proc.HasExited)
                    proc.Kill(entireProcessTree: true);
            }
            catch
            {
            }
        }
    }
}
