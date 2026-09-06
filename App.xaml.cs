using System.Windows;
using PZTools.Core.Functions;
using PZTools.Core.Functions.Logger;
using PZTools.Core.Functions.Projects;
using PZTools.Core.Models.Commands;
using PZTools.Core.Windows;

namespace PZTools
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        public static new MainWindow? MainWindow = null;
        public static bool IsDebug { get; private set; } = false;
        public static bool IsInHome { get; private set; } = false;

        public static string[] CommandLineArgs => Environment.GetCommandLineArgs();
        public static Task? cliTask = null;
        public static CancellationTokenSource? cliCancelToken = null;

        public static async Task RunCLI(bool printOld = false)
        {
            if (printOld)
            {
                foreach (string message in Console.GetAllMessages())
                {
                    System.Console.WriteLine(message);
                }
            }

            cliCancelToken ??= new CancellationTokenSource();
            while (!cliCancelToken.IsCancellationRequested)
            {
                await Console.Log("Please enter a command:");
                System.Console.Write(">");
                var input = System.Console.ReadLine();
                if (input is null)
                    break;

                await Console.Log($"\"{input}\"");
                if (Commands.TryParseCommand(input, out var command, true) && command?.Method != null)
                    await command.Method(command.Parameters);
                else
                    await Console.Log($"\"{input}\" is an unknown command. Use \"/help\" for available commands.");
            }
        }

        public static void ReloadApp()
        {
            if (MainWindow is null)
                return;
            Preloader preloader = new Preloader();
            MainWindow.IsReloading = true;
            MainWindow.Close();
            preloader.Show();
            ProjectEngine.Cleanup();
        }

        public static void CloseApp(int exitCode = 0)
        {
            if (MainWindow != null && !MainWindow.IsClosing)
            {
                MainWindow.Close();
                if (!MainWindow.IsClosing) return;
            }

            cliCancelToken?.Cancel();

            System.Windows.Application.Current.Shutdown(exitCode);
        }

        private async void Application_Startup(object sender, StartupEventArgs e)
        {
            if (ProjectTaskCommand.IsRequested(e.Args))
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var exitCode = Task.Run(() => ProjectTaskCommand.RunAsync(e.Args, System.Console.Out, System.Console.Error))
                    .GetAwaiter().GetResult();
                Shutdown(exitCode);
                return;
            }

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

        static Task HandleExceptions()
        {
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(CurrentDomain_UnhandledException);
            Current.DispatcherUnhandledException += Current_DispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
            System.Windows.Forms.Application.ThreadException += Application_ThreadException;
            return Task.CompletedTask;
        }

        static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is not Exception ex)
                return;
            else if (ex == null)
                return;

            _ = Console.Log($"Error! - {ex.Message}\r\n{ex.StackTrace}", Console.LogLevel.Error);
        }

        private static void Current_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            _ = Console.Log($"UI Thread Error! - {e.Exception.Message}\r\n{e.Exception.StackTrace}", Console.LogLevel.Error);
            e.Handled = true;
        }

        private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            _ = Console.Log($"Task Error! - {e.Exception.Message}\r\n{e.Exception.StackTrace}", Console.LogLevel.Error);
            e.SetObserved();
        }

        private static void Application_ThreadException(object sender, System.Threading.ThreadExceptionEventArgs e)
        {
            _ = Console.Log($"Thread Error! - {e.Exception.Message}\r\n{e.Exception.StackTrace}", Console.LogLevel.Error);
        }

        private void Application_Exit(object sender, ExitEventArgs e)
        {

        }
    }

}
