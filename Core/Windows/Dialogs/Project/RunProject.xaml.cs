using PZTools.Core.Functions.Zomboid;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

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
            }

            Console.OnLogMessage += (s, msg) =>
            {
                Dispatcher.Invoke(() =>
                {
                    txtOutputLog.AppendText(msg);
                    txtOutputLog.ScrollToEnd();
                });
            };
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

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = args,
                    WorkingDirectory = buildPath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

                process.OutputDataReceived += (s, ev) =>
                {
                    if (!string.IsNullOrEmpty(ev.Data))
                        Dispatcher.Invoke(() => Console.Log(ev.Data + "\n"));
                };

                process.ErrorDataReceived += (s, ev) =>
                {
                    if (!string.IsNullOrEmpty(ev.Data))
                        Dispatcher.Invoke(() => Console.Log("[ERROR] " + ev.Data + "\n"));
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                txtOutputLog.AppendText($"Launched build: {selectedBuild}\n");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to launch: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
