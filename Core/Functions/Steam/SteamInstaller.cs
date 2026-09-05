using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;

namespace PZTools.Core.Functions.Steam
{
    internal sealed class SteamInstaller
    {
        private const string SteamCmdZipUrl =
            "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip";

        private static readonly HttpClient HttpClient = new();

        public string SteamCmdDirectory { get; private set; } = "";
        public string SteamCmdExecutable => Path.Combine(SteamCmdDirectory, "steamcmd.exe");

        public event EventHandler<string>? SteamMessage;

        public async Task SetupSteamCmdAsync(
            string steamCmdDirectory,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(steamCmdDirectory);

            SteamCmdDirectory = Path.GetFullPath(steamCmdDirectory);
            Directory.CreateDirectory(SteamCmdDirectory);

            if (File.Exists(SteamCmdExecutable))
            {
                Emit("SteamCMD is already installed.");
                return;
            }

            var archivePath = Path.Combine(SteamCmdDirectory, "steamcmd.zip");
            try
            {
                Emit("Downloading SteamCMD...");
                await DownloadArchiveAsync(archivePath, cancellationToken);

                Emit("Extracting SteamCMD...");
                ZipFile.ExtractToDirectory(archivePath, SteamCmdDirectory, overwriteFiles: true);

                Emit("Installing SteamCMD...");
                await RunInitialSetupAsync(cancellationToken);
            }
            finally
            {
                TryDelete(archivePath);
            }
        }

        public async Task<bool> InstallAppAsync(
            string appId,
            string installDirectory,
            string username,
            string beta = "",
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(SteamCmdDirectory))
            {
                throw new InvalidOperationException(
                    "SteamCMD must be set up before installing an application.");
            }

            ArgumentException.ThrowIfNullOrWhiteSpace(appId);
            ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);
            ArgumentException.ThrowIfNullOrWhiteSpace(username);
            cancellationToken.ThrowIfCancellationRequested();

            Directory.CreateDirectory(installDirectory);
            Emit($"Installing {appId} [beta={beta}]...");

            using var process = new Process
            {
                StartInfo = CreateInstallStartInfo(appId, installDirectory, username, beta)
            };

            try
            {
                if (!process.Start())
                    return false;

                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                process.TryKillProcessTree();
                return false;
            }

            return process.ExitCode == 0 &&
                File.Exists(Path.Combine(installDirectory, "projectzomboid.jar"));
        }

        private static async Task DownloadArchiveAsync(
            string destinationPath,
            CancellationToken cancellationToken)
        {
            using var response = await HttpClient.GetAsync(
                SteamCmdZipUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var destination = new FileStream(
                destinationPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None);
            await response.Content.CopyToAsync(destination, cancellationToken);
        }

        private async Task RunInitialSetupAsync(CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = SteamCmdExecutable,
                WorkingDirectory = SteamCmdDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true,
                Arguments = "+quit",
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = startInfo };
            process.OutputDataReceived += (_, args) => EmitIfPresent(args.Data);
            process.ErrorDataReceived += (_, args) => EmitIfPresent(args.Data);

            if (!process.Start())
                throw new InvalidOperationException("Windows did not start SteamCMD.");

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                process.TryKillProcessTree();
                throw;
            }

            if (process.ExitCode != 0 || !File.Exists(SteamCmdExecutable))
            {
                throw new InvalidOperationException(
                    $"SteamCMD setup failed with exit code {process.ExitCode}.");
            }
        }

        private ProcessStartInfo CreateInstallStartInfo(
            string appId,
            string installDirectory,
            string username,
            string beta)
        {
            // Steam Guard and password prompts must stay visible to the user. PZTools
            // deliberately passes only the account name and never captures credentials.
            var startInfo = new ProcessStartInfo
            {
                FileName = SteamCmdExecutable,
                WorkingDirectory = SteamCmdDirectory,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal
            };

            startInfo.ArgumentList.Add("+login");
            startInfo.ArgumentList.Add(username);
            startInfo.ArgumentList.Add("+force_install_dir");
            startInfo.ArgumentList.Add(installDirectory);
            startInfo.ArgumentList.Add("+app_update");
            startInfo.ArgumentList.Add(appId);

            if (!string.IsNullOrWhiteSpace(beta))
            {
                startInfo.ArgumentList.Add("-beta");
                startInfo.ArgumentList.Add(beta);
            }

            startInfo.ArgumentList.Add("validate");
            startInfo.ArgumentList.Add("+quit");
            return startInfo;
        }

        private void EmitIfPresent(string? message)
        {
            if (!string.IsNullOrWhiteSpace(message))
                Emit(message);
        }

        private void Emit(string message)
        {
            _ = Console.Log(message);
            SteamMessage?.Invoke(this, message);
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // A failed cleanup does not invalidate an otherwise usable installation.
            }
            catch (UnauthorizedAccessException)
            {
                // Antivirus or another process can briefly retain the downloaded archive.
            }
        }
    }
}
