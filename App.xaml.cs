using PZTools.Core.Functions;
using PZTools.Core.Functions.Logger;
using PZTools.Core.Functions.Projects;
using PZTools.Core.Models.Commands;
using PZTools.Core.Windows;
using System.Windows;

namespace PZTools
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        public static MainWindow MainWindow = null;
        public static bool IsDebug { get; private set; } = false;
        public static bool IsInHome { get; private set; } = false;

        public static string[] CommandLineArgs => Environment.GetCommandLineArgs();
        public static Task cliTask;
        public static CancellationTokenSource cliCancelToken;

        public static async Task RunCLI(bool printOld = false)
        {
            if (printOld)
            {
                foreach (string message in Console.GetAllMessages())
                {
                    System.Console.WriteLine(message);
                }
            }

            await Console.Log("Please Enter a command:");
            System.Console.Write(">");
            var input = System.Console.ReadLine();
            await Console.Log($"\"{input}\"");
            Command? command;
            if (Commands.TryParseCommand(input, out command, true))
            {
                await command.Method(command.Parameters);
            }
            else await Console.Log($"\"{input}\" is an unknown command?! Please use \"/help\" for a list of available commands.");

            if (!cliCancelToken.IsCancellationRequested) await RunCLI();
        }

        public static void ReloadApp()
        {
            Preloader preloader = new Preloader();
            MainWindow.reloading = true;
            MainWindow.Close();
            preloader.Show();
            ProjectEngine.Cleanup();
        }

        public static void CloseApp(int exitCode = 0)
        {
            cliCancelToken.Cancel();
            if (MainWindow != null && !MainWindow.isClosing) MainWindow.Close();

            System.Windows.Application.Current.Shutdown(exitCode);
        }

        private async void Application_Startup(object sender, StartupEventArgs e)
        {
            await HandleExceptions();

            await this.Log("PZTools launching...");
            await HandleArgs();
        }

        private async Task HandleArgs()
        {
            await this.Log($"Launch args: {string.Join("\", \"", CommandLineArgs)}");
            foreach (var arg in CommandLineArgs)
            {
                switch (arg)
                {
                    case "--debug":
                        IsDebug = true;
                        LogHelper.Show();
                        await this.Log("Debug Mode Enabled");
                        break;
                    case "--isInHome":
                        IsInHome = true;
                        Config.SetAppSetting("AppInstallPath", AppPaths.CurrentDirectoryPath);
                        await this.Log("Flagged this folder as AppPath");
                        break;
                }
            }
        }

        static async Task HandleExceptions()
        {
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(CurrentDomain_UnhandledException);
            Current.DispatcherUnhandledException += Current_DispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
            System.Windows.Forms.Application.ThreadException += Application_ThreadException;
        }

        static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = (e.ExceptionObject as Exception);
            Console.Log($"Error! - {ex.Message}\r\n{ex.StackTrace}", Console.LogLevel.Error);
        }

        private static void Current_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            Console.Log($"UI Thread Error! - {e.Exception.Message}\r\n{e.Exception.StackTrace}", Console.LogLevel.Error);
            e.Handled = true;
        }

        private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            Console.Log($"Task Error! - {e.Exception.Message}\r\n{e.Exception.StackTrace}", Console.LogLevel.Error);
            e.SetObserved();
        }

        private static void Application_ThreadException(object sender, System.Threading.ThreadExceptionEventArgs e)
        {
            Console.Log($"Thread Error! - {e.Exception.Message}\r\n{e.Exception.StackTrace}", Console.LogLevel.Error);
        }

        private void Application_Exit(object sender, ExitEventArgs e)
        {

        }
    }

}
