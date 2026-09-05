using System.IO;
using System.Windows;
using PZTools.Core.Functions;
using PZTools.Core.Functions.Logger;
using PZTools.Core.Functions.Theme;
using PZTools.Core.Functions.Update;
using PZTools.Core.Windows.Dialogs;
using PZTools.Core.Windows.Dialogs.Project;

namespace PZTools.Core.Windows
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class Preloader : Window
    {
        public Preloader()
        {
            InitializeComponent();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await this.Log("Started preloading...");
            if (!await CheckDirectories())
            {
                DonePreloading(false);
                return;
            }

            await Config.PrintAppSettings();
            await ThemeManager.ApplyThemeFromSettings();
            await CheckForUpdates();

            if (!await CheckProjects())
            {
                DonePreloading(false);
                return;
            }

            DonePreloading(true);
        }

        private async Task<bool> CheckDirectories()
        {
            await this.Log("Checking application directories...");
            string currentDirectory = AppPaths.CurrentDirectoryPath;
            string configuredInstall = Config.GetAppSetting<string>("AppInstallPath") ?? string.Empty;
            bool configuredHere = !string.IsNullOrWhiteSpace(configuredInstall) &&
                string.Equals(Path.GetFullPath(configuredInstall), Path.GetFullPath(currentDirectory), StringComparison.OrdinalIgnoreCase);
            await this.Log($"Current Directory: {currentDirectory}");
            if (!configuredHere && !App.CommandLineArgs.Contains("--skipSetup"))
            {
                await this.Log("PZTools isn't within it's usual folder, assuming fresh install.");
                var appSetupResult = this.ShowDialog(new AppSetup());
                if (!appSetupResult)
                    return false;

                await this.Log("App setup and ready for restart.");

                WindowsHelpers.OpenFile(Path.Combine(AppPaths.CurrentDirectoryPath, "PZTools.exe"));
                App.CloseApp();
                return false;
            }
            return true;
        }

        private async Task CheckForUpdates()
        {
            if (Config.GetAppSetting<bool>("CheckUpdatesOnStartup"))
            {
                if (await AppUpdater.CheckForUpdates())
                {
                    await AppUpdater.StartUpdate();
                }
            }
        }

        private async Task<bool> CheckProjects()
        {
            await this.Log("Checking Projects...");
            if (!this.ShowDialog(new ProjectSelector()))
                return false;
            return true;
        }

        public async void DonePreloading(bool success)
        {
            if (success)
            {
                MainWindow mainWindow = new MainWindow();
                App.MainWindow = mainWindow;
                mainWindow.Show();
                await this.Log("Preloading complete, opening main window.");
                this.Close();
            }
            else
            {
                App.CloseApp();
            }
        }
    }
}
