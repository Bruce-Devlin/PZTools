using PZTools.Core.Functions;
using PZTools.Core.Functions.Logger;
using PZTools.Core.Functions.Zomboid;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace PZTools.Core.Windows.Dialogs.Project
{
    public partial class RunProject : Window
    {
        public string ZomboidRoot => ZomboidGame.GameDirectory;
        private bool showWindow = true;
        private string? existingBuild = "";
        private string? existingArgs = "";

        private readonly object _logLock = new();
        private readonly StringBuilder _logBuffer = new();
        private DispatcherTimer? _logFlushTimer;
        private EventHandler<string>? _gameOutputHandler;

        public RunProject(bool showWindow = true)
        {
            InitializeComponent();
            this.FreeDragThisWindow();

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

            existingArgs = Config.GetVariable(VariableType.user, "lastUsedLaunchArgs");
            if (string.IsNullOrEmpty(existingArgs))
                txtLaunchArgs.Text = "-debug";
            else
                txtLaunchArgs.Text = existingArgs;

            this.showWindow = showWindow;

            var showWindowSetting = Config.GetVariable(VariableType.user, "showRunWindowBeforeLaunch");
            if (!string.IsNullOrEmpty(showWindowSetting))
            {
                showWindowBeforeRun.IsChecked = bool.Parse(showWindowSetting);
            }
            else showWindowBeforeRun.IsChecked = true;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (!showWindow)
            {
                existingBuild = Config.GetVariable(VariableType.user, "lastUsedGameBuild");
                if (!string.IsNullOrEmpty(existingBuild) && !string.IsNullOrEmpty(existingArgs))
                {
                    LaunchGame(existingBuild, existingArgs);
                    this.Close();
                }
            }
        }

        private void EnsureLogFlushTimer()
        {
            if (_logFlushTimer != null) return;

            _logFlushTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(75)
            };

            _logFlushTimer.Tick += async (_, __) =>
            {
                string chunk;

                lock (_logLock)
                {
                    if (_logBuffer.Length == 0) return;
                    chunk = _logBuffer.ToString();
                    _logBuffer.Clear();
                }

                await Console.Log(chunk);
                txtOutputLog.ScrollToEnd();
            };

            _logFlushTimer.Start();
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

        private void LaunchGame(string build, string args)
        {
            Config.StoreVariable(VariableType.user, "lastUsedLaunchArgs", args);
            Config.StoreVariable(VariableType.user, "lastUsedGameBuild", build);

            string buildPath = ZomboidRoot;
            string exePath = Path.Combine(buildPath, "ProjectZomboid64.exe");

            if (ZomboidGame.GameMode == "managed")
            {
                buildPath = Path.Combine(ZomboidRoot, "Zomboid", build);
                exePath = Path.Combine(buildPath, "ProjectZomboid64.exe");
            }

            if (!File.Exists(exePath))
            {
                MessageBox.Show($"Executable not found: {exePath}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            EnsureLogFlushTimer();

            // Prevent multiple subscriptions across repeated launches
            if (_gameOutputHandler != null)
                ZomboidGame.OnGameOutput -= _gameOutputHandler;

            _gameOutputHandler = (s, output) =>
            {
                lock (_logLock)
                {
                    _logBuffer.AppendLine(output);
                }

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_logFlushTimer != null && !_logFlushTimer.IsEnabled)
                        _logFlushTimer.Start();
                }), DispatcherPriority.Background);
            };

            ZomboidGame.OnGameOutput += _gameOutputHandler;

            _ = Task.Run(async () =>
            {
                try
                {
                    await ZomboidGame.StartGame(exePath, args);
                }
                finally
                {
                    // Stop button state back on UI
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        btnStopGame.IsEnabled = false;
                    }));
                }
            });

            btnStopGame.IsEnabled = true;
        }

        private void BtnLaunch_Click(object sender, RoutedEventArgs e)
        {
            if (cmbBuilds.SelectedItem == null)
            {
                MessageBox.Show("Please select a build to launch.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string selectedBuild = cmbBuilds.SelectedItem.ToString()!;
            LaunchGame(selectedBuild, txtLaunchArgs.Text);
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private async void btnStopGame_Click(object sender, RoutedEventArgs e)
        {
            if (!ZomboidGame.IsGameRunning()) return;

            await ZomboidGame.StopGame();
        }

        private void showWindowBeforeRun_Checked(object sender, RoutedEventArgs e)
        {
            Config.StoreVariable(VariableType.user, "showRunWindowBeforeLaunch", showWindowBeforeRun.IsChecked.ToString());
        }
    }
}
