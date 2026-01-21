using System.Diagnostics;
using System.IO;
using System.Windows;

namespace PZTools.Core.Functions.Zomboid
{
    internal class ZomboidGame
    {
        public static double latestStableBuild = 41;
        public static string? GameDirectory
        {
            get
            {
                if (GameMode == "existing")
                    return Config.GetVariable(VariableType.system, "ExistingGamePath");
                else
                    return Config.GetVariable(VariableType.system, "ManagedGamePath");
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
                return Config.GetVariable(VariableType.system, "GameMode");
            }
        }

        public static EventHandler<string> OnGameOutput = new EventHandler<string>(delegate { });

        public static async Task StartGame(string gamePath, string args)
        {
            try
            {
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

                var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

                process.OutputDataReceived += (s, ev) => OnGameOutput.Invoke(s, ev.Data ?? string.Empty);

                process.ErrorDataReceived += (s, ev) => OnGameOutput.Invoke(s, ev.Data ?? string.Empty);

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to launch: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public static bool IsGameRunning()
        {
            var process = GetGameProcess();
            if (process == null) return false;
            else return !process.HasExited;
        }

        public static Process? GetGameProcess()
        {
            var allProcesses = Process.GetProcesses();
            var zomboidProcess = allProcesses.FirstOrDefault(p => p.ProcessName.Contains("ProjectZomboid"));
            return zomboidProcess;
        }
    }
}
