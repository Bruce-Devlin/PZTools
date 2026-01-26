using System.Diagnostics;
using System.IO;
using System.Windows;

namespace PZTools.Core.Functions.Zomboid
{
    internal class ZomboidGame
    {
        public static double latestStableBuild = 41;
        public static Process? GameProcess { get; private set; } = null;

        private static bool _isGameStarting;
        public static bool IsGameStarting
        {
            get => _isGameStarting;
            private set
            {
                if (_isGameStarting != value)
                {
                    _isGameStarting = value;
                    RaiseStateChanged();
                }
            }
        }

        public static bool IsRunning
        {
            get
            {
                var p = GameProcess;
                if (p == null) return false;
                if (IsGameStarting) return true;
                return !p.HasExited;
            }
        }

        public static event Action? StateChanged;

        private static void RaiseStateChanged() => StateChanged?.Invoke();

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
                    WorkingDirectory = Directory.GetParent(gamePath)!.FullName,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                GameProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };

                GameProcess.OutputDataReceived += (s, ev) =>
                    OnGameOutput.Invoke(s, ev.Data ?? string.Empty);

                GameProcess.ErrorDataReceived += (s, ev) =>
                    OnGameOutput.Invoke(s, ev.Data ?? string.Empty);

                GameProcess.Exited += (s, ev) =>
                {
                    IsGameStarting = false;
                    RaiseStateChanged();
                };

                GameProcess.Start();
                RaiseStateChanged();
                GameProcess.BeginOutputReadLine();
                GameProcess.BeginErrorReadLine();

                await GameProcess.WaitForExitAsync();

                IsGameStarting = false;
                RaiseStateChanged();
            }
            catch (Exception ex)
            {
                IsGameStarting = false;
                RaiseStateChanged();
                MessageBox.Show($"Failed to launch: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public static async Task StopGame()
        {
            var process = GameProcess;
            if (process != null && !process.HasExited)
            {
                try
                {
                    process.Kill(true);
                    await process.WaitForExitAsync();
                    await Console.Log("Game process terminated by user.");
                }
                catch (Exception ex)
                {
                    await Console.Log($"Failed to stop game: {ex.Message}");
                }
            }
            else
            {
                await Console.Log("No running game process found.");
            }

            RaiseStateChanged();
        }

        public static bool IsGameRunning() => IsRunning;

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
    }
}
