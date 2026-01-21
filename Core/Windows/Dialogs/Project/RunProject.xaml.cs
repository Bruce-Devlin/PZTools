using PZTools.Core.Functions.Logger;
using PZTools.Core.Functions.Zomboid;
using System.IO;
using System.Windows;

namespace PZTools.Core.Windows.Dialogs.Project
{
    public partial class RunProject : Window
    {
        public string ZomboidRoot { get; set; } = ZomboidGame.GameDirectory;

        public RunProject()
        {
            InitializeComponent();
            LoadBuilds();

            if (ZomboidGame.IsGameRunning())
            {
                txtOutputLog.Text = string.Join("\r\n", Console.GetAllMessages());
                btnLaunch.IsEnabled = false;
                btnStopGame.IsEnabled = true;
            }

            Console.OnLogMessage += (s, msg) =>
            {
                Dispatcher.Invoke(() =>
                {
                    txtOutputLog.AppendText(msg);
                    txtOutputLog.ScrollToEnd();
                });
            };

            txtLaunchArgs.Text = "-debug -debugtranslation -modfolders workshop,steam -imgui -windowed";
        }

        private void LoadBuilds()
        {
            cmbBuilds.Items.Clear();

            if (ZomboidGame.GameMode == "managed")
            {
                string buildsDir = Path.Combine(ZomboidRoot, "Zomboid");
                if (!Directory.Exists(buildsDir))
                {
                    txtOutputLog.AppendText("No builds folder found.\n");
                    return;
                }

                foreach (var dir in Directory.GetDirectories(buildsDir))
                {
                    cmbBuilds.Items.Add(Path.GetFileName(dir));
                }
            }

            cmbBuilds.Items.Add("Exisitng Steam Installation");

            if (cmbBuilds.Items.Count > 0)
                cmbBuilds.SelectedIndex = 0;
        }

        private void BtnLaunch_Click(object sender, RoutedEventArgs e)
        {
            if (cmbBuilds.SelectedItem == null)
            {
                MessageBox.Show("Please select a build to launch.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string selectedBuild = cmbBuilds.SelectedItem.ToString()!;
            string buildPath = ZomboidRoot;
            string exePath = Path.Combine(buildPath, "ProjectZomboid64.exe");

            if (ZomboidGame.GameMode == "managed")
            {
                buildPath = Path.Combine(ZomboidRoot, "Zomboid", selectedBuild);
                exePath = Path.Combine(buildPath, "ProjectZomboid64.exe");
            }

            if (!File.Exists(exePath))
            {
                MessageBox.Show($"Executable not found: {exePath}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string args = txtLaunchArgs.Text;

            ZomboidGame.OnGameOutput += (s, output) =>
            {
                Dispatcher.Invoke(() =>
                {
                    txtOutputLog.AppendText(output);
                    txtOutputLog.ScrollToEnd();
                });
            };
            ZomboidGame.StartGame(exePath, args);
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private async void btnStopGame_Click(object sender, RoutedEventArgs e)
        {
            if (!ZomboidGame.IsGameRunning()) return;

            var gameProcess = ZomboidGame.GetGameProcess();
            if (gameProcess == null) return;

            try
            {
                gameProcess.Kill();
                gameProcess.WaitForExit();
                txtOutputLog.AppendText("Game process terminated.\n");
                await this.Log("Game process terminated by user.");
                btnStopGame.IsEnabled = false;
                btnLaunch.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to stop the game: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
