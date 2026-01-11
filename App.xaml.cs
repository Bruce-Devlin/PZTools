using PZTools.Core.Functions.Logger;
using PZTools.Core.Functions.Projects;
using PZTools.Core.Models.Commands;
using PZTools.Core.Windows;

using System.IO;
using System.Linq;
using System.Reflection;
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
        public static string[] CommandLineArgs => Environment.GetCommandLineArgs();
        public static Task cliTask;
        public static CancellationTokenSource cliCancelToken;

        public static string currentFilePath => Assembly.GetExecutingAssembly().Location;
        public static DirectoryInfo? currentDirectory => Directory.GetParent(currentFilePath);
        public static string configsDirectory
        {
            get
            {
                var dir = Path.GetFullPath(currentDirectory + "\\Configs\\");
                if (!Directory.Exists(configsDirectory))
                    Directory.CreateDirectory(configsDirectory);
                return dir;
            }
        }

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
            MainWindow.Close();
            preloader.Show();
            ProjectEngine.Cleanup();
        }

        private async void Application_Startup(object sender, StartupEventArgs e)
        {
            await this.Log("PZTools launching...");
            await this.Log($"Launch args: {string.Join("\", \"", CommandLineArgs)}");
            if (CommandLineArgs.Contains("--debug"))
            {
                IsDebug = true;
                LogHelper.Show();
                await this.Log("Debug Mode Enabled");
            }
        }
    }

}
