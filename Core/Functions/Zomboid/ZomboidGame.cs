using System.Diagnostics;
using System.IO;
using System.Windows;

namespace PZTools.Core.Functions.Zomboid
{
    internal class ZomboidGame
    {
        // Build 42 is the current stable compatibility family. Keep this configurable
        // so PZTools does not need a release when the game's major mod target advances.
        public static double latestStableBuild
        {
            get
            {
                var configured = Config.GetAppSetting<double>("StableBuild");
                return configured > 0 ? configured : 42;
            }
        }
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
                if (p == null)
                    return false;
                if (IsGameStarting)
                    return true;
                return !p.HasExited;
            }
        }

        public static event Action? StateChanged;

        private static void RaiseStateChanged() => StateChanged?.Invoke();

        public static event EventHandler<string>? OnGameOutput;

        public static async Task StartGame(string gamePath, string args)
        {
            if (IsRunning)
            {
                await Console.Log("Project Zomboid is already running.", Console.LogLevel.Warning);
                return;
            }

            try
            {
                if (!File.Exists(gamePath))
                    throw new FileNotFoundException("Project Zomboid executable was not found.", gamePath);

                IsGameStarting = true;

                var psi = new ProcessStartInfo
                {
                    FileName = gamePath,
                    Arguments = args,
                    WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(gamePath))!,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                GameProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };

                GameProcess.OutputDataReceived += (s, ev) =>
                    OnGameOutput?.Invoke(s, ev.Data ?? string.Empty);

                GameProcess.ErrorDataReceived += (s, ev) =>
                    OnGameOutput?.Invoke(s, ev.Data ?? string.Empty);

                if (!GameProcess.Start())
                    throw new InvalidOperationException("Windows did not start Project Zomboid.");
                RaiseStateChanged();
                GameProcess.BeginOutputReadLine();
                GameProcess.BeginErrorReadLine();

                await GameProcess.WaitForExitAsync();

            }
            catch (Exception ex)
            {
                await Console.Log($"Failed to launch Project Zomboid: {ex.Message}", Console.LogLevel.Error);
            }
            finally
            {
                IsGameStarting = false;
                RaiseStateChanged();
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

        public static string GameDirectory
        {
            get
            {
                if (GameMode == "Existing")
                    return Config.GetAppSetting<string>("ExistingGamePath") ?? string.Empty;
                else
                    return Config.GetAppSetting<string>("ManagedGamePath") ?? string.Empty;
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
                return Config.GetAppSetting<string>("GameMode") ?? "Existing";
            }
        }

        public static IReadOnlyList<string> GetModSearchRoots()
        {
            var roots = new List<string>
            {
                Path.Combine(GameUserDirectory, "mods"),
                Path.Combine(GameUserDirectory, "Workshop")
            };

            if (!string.IsNullOrWhiteSpace(GameDirectory))
            {
                try
                {
                    roots.Add(Path.GetFullPath(Path.Combine(GameDirectory, "..", "..", "workshop", "content", "108600")));
                }
                catch { }
            }

            return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
