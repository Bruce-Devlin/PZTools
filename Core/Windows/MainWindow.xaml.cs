using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using PZTools.Core.Functions;
using PZTools.Core.Functions.Agent;
using PZTools.Core.Functions.Projects;
using PZTools.Core.Functions.Undo;
using PZTools.Core.Functions.Zomboid;
using PZTools.Core.Models;
using PZTools.Core.Models.View;
using PZTools.Core.Windows.Dialogs.Project;
using Application = System.Windows.Application;
using ContextMenu = System.Windows.Controls.ContextMenu;
using LayoutSettings = PZTools.Core.Models.View.LayoutSettings;

namespace PZTools.Core.Windows
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public ModProject ModProject { get; }
        public System.Windows.Controls.TextBox ConsoleOutputControl => ConsoleOutput;
        public static MainWindow? Instance { get; private set; }
        public string OpenedFilePath { get; private set; } = string.Empty;

        private readonly HashSet<string> _expandedFolderPaths = new(StringComparer.OrdinalIgnoreCase);
        private ProjectFileNode? _commonTree;
        private AgentMcpEditorServer? _agentMcpServer;

        public bool IsReloading { get; internal set; }
        public bool IsClosing { get; private set; }

        public MainWindow()
        {
            InitializeComponent();
            this.FreeDragThisWindow();

            ApplyAppSettings();

            DataContext = new MainViewModel();
            ModProject = ProjectEngine.CurrentProject
                ?? throw new InvalidOperationException("A project must be loaded before opening the main window.");
            Instance = this;
            _agentMcpServer = new AgentMcpEditorServer(this);
            _agentMcpServer.Start();
            UndoRedoManager.Instance.CommandExecuted += UndoRedo_CommandExecuted;

            Title = $"PZ Tools - {ModProject}";
            LoadLayout();
            InitializeWorkspace();
        }

        public void ApplyAppSettings()
        {
            UpdateFontSize();
            UpdateWordWrap();
        }

        private void UpdateFontSize()
        {
            try
            {
                var fontSize = Config.GetAppSetting<double>("EditorFontSize");
                this.Resources["EditorFontSize"] = fontSize;

                if (LuaEditor != null)
                    LuaEditor.FontSize = fontSize;
                if (ConsoleOutput != null)
                    ConsoleOutput.FontSize = fontSize;
            }
            catch
            {
                this.Resources["EditorFontSize"] = 14.0;
            }
        }

        private void UpdateWordWrap()
        {
            var enableWordWrap = Config.GetAppSetting<bool>("EditorWordWrap");
            LuaEditor.WordWrap = enableWordWrap;
        }

        private async void UndoRedo_CommandExecuted(object? sender, UndoRedoEventArgs e)
        {
            try
            {
                switch (e.Command)
                {
                    case FileMoveCommand move:
                        {
                            var oldParent = SafeFullPath(Path.GetDirectoryName(move.Source) ?? string.Empty);
                            var newParent = SafeFullPath(Path.GetDirectoryName(move.Destination) ?? string.Empty);

                            if (!string.IsNullOrWhiteSpace(oldParent))
                                await RefreshFolderPathUIAsync(oldParent);

                            if (!string.IsNullOrWhiteSpace(newParent) && !string.Equals(oldParent, newParent, StringComparison.OrdinalIgnoreCase))
                                await RefreshFolderPathUIAsync(newParent);
                        }
                        break;

                    case FileCreateCommand create:
                        {
                            var parent = SafeFullPath(Path.GetDirectoryName(create.Path) ?? string.Empty);
                            if (!string.IsNullOrWhiteSpace(parent))
                                await RefreshFolderPathUIAsync(parent);
                        }
                        break;

                    case FileDeleteCommand del:
                        {
                            var parent = SafeFullPath(Path.GetDirectoryName(del.Path) ?? string.Empty);
                            if (!string.IsNullOrWhiteSpace(parent))
                                await RefreshFolderPathUIAsync(parent);
                        }
                        break;

                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                await WriteToConsole($"Undo/Redo UI refresh error: {ex.Message}");
            }
        }

        public async Task RefreshFolderPathUIAsync(string folderFullPath)
        {
            var full = SafeFullPath(folderFullPath);
            if (full == null)
                return;

            var node = FindNodeByPath(full);
            if (node != null && node.IsFolder)
            {
                await RefreshFolderNodeAsync(node);
                return;
            }

            var parent = SafeFullPath(Path.GetDirectoryName(full) ?? string.Empty);
            if (parent != null)
            {
                var parentNode = FindNodeByPath(parent);
                if (parentNode != null && parentNode.IsFolder)
                {
                    await RefreshFolderNodeAsync(parentNode);
                }
            }
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ProjectTreeView.SelectedItemChanged += ProjectTreeView_SelectedItemChanged;
            ProjectTreeView.MouseDoubleClick += ProjectTreeView_MouseDoubleClick;

            ProjectTreeView.AddHandler(TreeViewItem.ExpandedEvent, new RoutedEventHandler(ProjectTreeView_ItemExpanded));
            ProjectTreeView.AddHandler(TreeViewItem.CollapsedEvent, new RoutedEventHandler(ProjectTreeView_ItemCollapsed));

            if (ProjectTreeView.ContextMenu == null)
                ProjectTreeView.ContextMenu = new ContextMenu();

            foreach (var target in ModProject.Targets)
            {
                target.LoadFiles();
            }

            LoadExplorerRoots();
            StartVersionSyncWatcher();

            try
            {
                var initialSync = await Task.Run(() => VersionSyncService.ReconcileAll(ModProject));
                if (initialSync.Copied + initialSync.Updated + initialSync.FoldersCreated > 0)
                    UpdateTreeView();
                if (initialSync.Conflicts > 0)
                    await WriteToConsole($"Version sync preserved {initialSync.Conflicts} conflicting file(s). Open the matching files in each build to reconcile them manually.");
            }
            catch (Exception ex)
            {
                await WriteToConsole($"Version sync startup error: {ex.Message}");
            }

            foreach (var msg in Console.GetAllMessages())
            {
                await WriteToConsole(msg);
            }

            if (!IsClosing) Console.OnLogMessage += Console_OnLogMessage;
        }

        private async void Console_OnLogMessage(object? sender, string message)
        {
            if (!IsClosing) await WriteToConsole(message);
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (Config.GetAppSetting<bool>("ConfirmOnExit") && !IsReloading)
            {
                var result = MessageBox.Show(
                    this,
                    "Are you sure you want to exit?",
                    "Confirm Exit",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                }
            }

            if (!e.Cancel)
            {
                IsClosing = true;
                Console.OnLogMessage -= Console_OnLogMessage;
                PZTools.Core.Functions.Theme.ThemeManager.ThemeChanged -= Workspace_ThemeChanged;
                UndoRedoManager.Instance.CommandExecuted -= UndoRedo_CommandExecuted;
                if (DataContext is MainViewModel viewModel) viewModel.Dispose();
                StopWatchingOpenedFile();
                lock (_folderWatchLock)
                    foreach (var path in _folderWatchersByPath.Keys.ToArray()) StopFolderWatcher(path);
                StopVersionSyncWatcher();
                if (_agentMcpServer != null)
                    _ = _agentMcpServer.DisposeAsync();
                if (!IsReloading)
                    App.CloseApp();
            }
        }

        public Task<object> GetAgentEditorStateAsync()
            => Dispatcher.InvokeAsync<object>(() => new
            {
                project = ModProject.Name,
                projectRoot = ModProject.RootPath,
                openFile = string.IsNullOrWhiteSpace(OpenedFilePath) ? null : Path.GetRelativePath(ModProject.RootPath, OpenedFilePath),
                line = LuaEditor.TextArea.Caret.Line,
                column = LuaEditor.TextArea.Caret.Column,
                selectionLength = LuaEditor.SelectionLength,
                documentLength = LuaEditor.Text.Length,
                previewReadOnly = LuaEditor.IsReadOnly,
                windowActive = IsActive
            }).Task;

        public Task<object> OpenFileForAgentAsync(string fullPath, int? line)
            => Dispatcher.InvokeAsync<object>(() =>
            {
                PreviewFile(fullPath);
                var text = LuaEditor.Text;
                if (line > 0)
                {
                    var safeLine = Math.Min(line.Value, Math.Max(1, LuaEditor.Document.LineCount));
                    LuaEditor.ScrollToLine(safeLine);
                    LuaEditor.TextArea.Caret.Line = safeLine;
                }
                Activate();
                return new
                {
                    path = Path.GetRelativePath(ModProject.RootPath, fullPath),
                    line = LuaEditor.TextArea.Caret.Line,
                    length = text.Length,
                    opened = true
                };
            }).Task;

        public async Task RefreshFileForAgentAsync(string fullPath)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (string.Equals(OpenedFilePath, fullPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (new FileInfo(fullPath).Length <= ProjectSearchService.MaxFileBytes &&
                        PreviewableFileExtensions.Contains(Path.GetExtension(fullPath)))
                        UpdatePreviewText(File.ReadAllText(fullPath));
                    else
                        PreviewFile(fullPath);
                }
                UpdateTreeView();
            });
        }

        public void SaveLayout()
        {
            var layoutSetting = new LayoutSettings(
                LayoutExplorer.ActualWidth,
                LayoutEditor.ActualWidth,
                LayoutProperties.ActualWidth,
                LayoutConsole.ActualHeight);

            Config.StoreObject(VariableType.system, "MainWindowLayout", layoutSetting);
        }

        public void LoadLayout()
        {
            var saved = Config.GetObject<LayoutSettings>(VariableType.system, "MainWindowLayout");
            if (saved == null)
                return;

            double total = saved.ExplorerWidth + saved.EditorWidth + saved.PropertiesWidth;
            if (total <= 0)
                return;

            LayoutExplorer.Width = new GridLength(saved.ExplorerWidth / total, GridUnitType.Star);
            LayoutEditor.Width = new GridLength(saved.EditorWidth / total, GridUnitType.Star);
            LayoutProperties.Width = new GridLength(saved.PropertiesWidth / total, GridUnitType.Star);

            LayoutConsole.Height = new GridLength(saved.ConsoleHeight);
        }


        public static Task WriteToConsole(string message)
        {
            string formattedMessage = message.StartsWith('[') ? message : $"[{DateTime.Now:HH:mm:ss}] {message}";

            return Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var window = MainWindow.Instance;
                if (window == null || window.IsClosing) return;
                var follow = window.FollowOutput.IsChecked == true &&
                    window.ConsoleScroll.VerticalOffset >= window.ConsoleScroll.ScrollableHeight - 4;
                window.ConsoleOutputControl.AppendText(formattedMessage + Environment.NewLine);
                if (follow) window.ConsoleScroll.ScrollToBottom();
            }).Task;
        }

        private async void DeployProject_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button)
                button.IsEnabled = false;

            try
            {
                StatusBarText.Text = "Validating project...";
                var health = await Task.Run(() => ProjectHealthService.AnalyzeAsync(ModProject, validateLua: true));
                if (!health.IsReadyToDeploy)
                {
                    StatusBarText.Text = "Deployment blocked by project errors";
                    var dashboard = new ProjectDashboard(ModProject) { Owner = this };
                    dashboard.ShowDialog();
                    return;
                }

                StatusBarText.Text = "Deploying project...";
                await ProjectDeployer.DeployProject(DeployFolder.Mods);
                StatusBarText.Text = health.WarningCount > 0
                    ? $"Project deployed with {health.WarningCount} warning(s)"
                    : "Project deployed";
            }
            catch (Exception ex)
            {
                StatusBarText.Text = "Deployment failed";
                await Console.Log($"Deployment failed: {ex.Message}", Console.LogLevel.Error);
            }
            finally
            {
                if (sender is System.Windows.Controls.Button finishedButton)
                    finishedButton.IsEnabled = true;
            }
        }

        private void ShowEditorEmptyState(string title, string body)
        {
            EditorEmptyTitle.Text = title;
            EditorEmptyBody.Text = body;
            EditorEmptyState.Visibility = Visibility.Visible;
        }

        private void NewModFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new NewModFile(ModProject) { Owner = this };
            dialog.ShowDialog();
        }

        private void ContentManagers_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentManagers(ModProject) { Owner = this };
            dialog.ShowDialog();
        }

        private void ProjectHealth_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ProjectDashboard(ModProject) { Owner = this };
            dialog.ShowDialog();
        }

        private void OpenInVsCode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                EditorIntegration.OpenInVsCode(ModProject, string.IsNullOrWhiteSpace(OpenedFilePath) ? null : OpenedFilePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Open in VS Code", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OpenGameLog_Click(object sender, RoutedEventArgs e)
        {
            var path = Path.Combine(ZomboidGame.GameUserDirectory, "console.txt");
            if (File.Exists(path))
                WindowsHelpers.OpenFile(path);
            else
                MessageBox.Show("No Project Zomboid console log was found. Run the game once and try again.",
                "Game Log", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ClearConsole_Click(object sender, RoutedEventArgs e)
        {
            ConsoleOutput.Clear();
        }

        private async void RunGame_Click(object sender, RoutedEventArgs e)
        {
            if (ZomboidGame.IsRunning)
            {
                await ZomboidGame.StopGame();
                return;
            }

            bool showWindow = true;
            var showSettingsWindow = Config.GetVariable(VariableType.user, "showRunWindowBeforeLaunch");
            if (!string.IsNullOrEmpty(showSettingsWindow))
                bool.TryParse(showSettingsWindow, out showWindow);

            var runGameWindow = new RunProject(showWindow);
            runGameWindow.ShowDialog();
        }
    }
}
