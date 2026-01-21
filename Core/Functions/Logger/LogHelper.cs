using System.IO;
using System.Runtime.InteropServices;

namespace PZTools.Core.Functions.Logger
{
    internal static class LogHelper
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeConsole();

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();


        /// <summary>
        /// Determines if the console window is currently hidden.
        /// </summary>
        public static bool IsHidden => _isHidden;
        private static bool _isHidden = true;

        /// <summary>
        /// Hides the console window completely (removes taskbar icon and closes the window).
        /// </summary>
        public static void Hide()
        {
            App.cliCancelToken.Cancel();
            if (GetConsoleWindow() != IntPtr.Zero)
            {
                FreeConsole();
            }

            _isHidden = true;
        }

        /// <summary>
        /// Shows a new console window.
        /// </summary>
        public static async void Show()
        {
            if (!_isHidden)
                return;

            if (GetConsoleWindow() == IntPtr.Zero)
            {
                AllocConsole();

                // Redirect standard output/input/error to the new console
                var stdout = System.Console.OpenStandardOutput();
                var stdin = System.Console.OpenStandardInput();
                var stderr = System.Console.OpenStandardError();

                System.Console.SetOut(new StreamWriter(stdout) { AutoFlush = true });
                System.Console.SetIn(new StreamReader(stdin));
                System.Console.SetError(new StreamWriter(stderr) { AutoFlush = true });
            }

            await Console.Log("Running CLI...");
            App.cliCancelToken = new CancellationTokenSource();
            App.cliTask = Task.Run(() => App.RunCLI(true), App.cliCancelToken.Token);
            _isHidden = false;
        }

        /// <summary>
        /// Toggles the visibility of the console window.
        /// </summary>
        public static void ToggleConsole()
        {
            if (IsHidden)
                Show();
            else
                Hide();
        }
    }
}
