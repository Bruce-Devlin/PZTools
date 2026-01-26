using PZTools.Core.Functions;
using PZTools.Core.Functions.Logger;
using PZTools.Core.Functions.Theme;
using PZTools.Core.Functions.Update;
using PZTools.Core.Windows.Dialogs;
using PZTools.Core.Windows.Dialogs.Project;
using System.IO;
using System.Windows;

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
            bool directoriesSuccess = false;
            bool projectsSuccess = false;


            await this.Log("Started preloading...");
            if (await CheckDirectories()) directoriesSuccess = true;

            await Config.PrintAppSettings();
            await ThemeManager.ApplyThemeFromSettings();
            await CheckForUpdates();

            if (await CheckProjects()) projectsSuccess = true;


            bool success = directoriesSuccess && projectsSuccess;
            DonePreloading(success);
        }

        private async Task<bool> CheckDirectories()
        {
            await this.Log("Checking application directories...");
            string folderName = AppPaths.CurrentDirectory.Name;
            await this.Log($"Current Directory: {folderName}");
            if (folderName != "PZTools" && !App.CommandLineArgs.Contains("--skipSetup"))
            {
                await this.Log("PZTools isn't within it's usual folder, assuming fresh install.");
                if (!this.ShowDialog(new AppSetup())) return false;
                await this.Log("App setup and ready for restart.");

                WindowsHelpers.OpenFile(Path.Combine(AppPaths.CurrentDirectoryPath, "PZTools.exe"));
                App.CloseApp();
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
            if (!this.ShowDialog(new ProjectSelector())) return false;
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
            else App.CloseApp();
        }
    }
}
