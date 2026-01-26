using System.Diagnostics;
using System.IO;
using System.Windows;

namespace PZTools.Core.Functions.Zomboid
{
    internal class ZomboidGame
    {
        public static double latestStableBuild = 41;
        public static Process? GameProcess { get; private set; } = null;
        public static bool IsGameStarting { get; private set; } = false;
        public static string? GameDirectory
        {
            get
            {
                if (GameMode == "Existing")
                    return Config.GetAppSetting<string>("ExistingGamePath");
                else
                    return Config.GetAppSetting<string>("ManagedGamePath");
            }
        }


        public static string GameUserDirectory
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Zomboid");
            }
        }

        public static string GameMode
        {
            get
            {
                return Config.GetAppSetting<string>("GameMode");
            }
        }

        public static EventHandler<string> OnGameOutput = new EventHandler<string>(delegate { });

        public static async Task StartGame(string gamePath, string args)
        {
            try
            {
                IsGameStarting = true;
                var psi = new ProcessStartInfo
                {
                    FileName = gamePath,
                    Arguments = args,
                    WorkingDirectory = Directory.GetParent(gamePath).FullName,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                GameProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };

                GameProcess.OutputDataReceived += (s, ev) => OnGameOutput.Invoke(s, ev.Data ?? string.Empty);

                GameProcess.ErrorDataReceived += (s, ev) => OnGameOutput.Invoke(s, ev.Data ?? string.Empty);

                GameProcess.Start();
                GameProcess.BeginOutputReadLine();
                GameProcess.BeginErrorReadLine();
                GameProcess.WaitForExit();
                IsGameStarting = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to launch: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public static async Task StopGame()
        {
            var process = GameProcess;
            if (process != null && !process.HasExited)
            {
                var actualProcess = Process.GetProcessById(process.Id);

                actualProcess.Kill();
                await Console.Log("Game process terminated by user.");
            }
            else
            {
                await Console.Log("No running game process found.");
            }
        }

        public static bool IsGameRunning()
        {
            var process = GameProcess;
            if (process == null) return false;
            else if (IsGameStarting) return true;
            else return !process.HasExited;
        }
    }
}
