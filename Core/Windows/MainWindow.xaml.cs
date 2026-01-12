using PZTools.Core.Functions.Projects;
using PZTools.Core.Functions.Tester;
using PZTools.Core.Models;
using PZTools.Core.Models.View;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Xml;
using Application = System.Windows.Application;

namespace PZTools.Core.Windows
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public ModProject ModProject { get; set; }
        public System.Windows.Controls.TextBox ConsoleOutputControl => ConsoleOutput;
        public static MainWindow Instance { get; private set; }
        public string OpenedFilePath = string.Empty;


        public MainWindow()
        {
            InitializeComponent();

            DataContext = new MainViewModel();
            ModProject = ProjectEngine.CurrentProject;
            Instance = this;

            Title = $"PZ Tools - {ProjectEngine.CurrentProject}";
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ModsTreeView.SelectedItemChanged += ModsTreeView_SelectedItemChanged;
            ModsTreeView.MouseDoubleClick += ModsTreeView_MouseDoubleClick;

            foreach (var target in ModProject.Targets)
            {
                target.LoadFiles();
            }

            ModsTreeView.ItemsSource = ModProject.Targets;

            foreach (var msg in Console.GetAllMessages())
            {
                await WriteToConsole(msg);
            }

            Console.OnLogMessage += async (s, msg) => await WriteToConsole(msg);
        }

        public static Task WriteToConsole(string message)
        {
            string formattedMessage = $"[{DateTime.Now:HH:mm:ss}] {message}";

            return Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var window = MainWindow.Instance;
                if (window == null)
                    return;

                window.ConsoleOutputControl.AppendText(formattedMessage + Environment.NewLine);
                window.ConsoleScroll.ScrollToBottom();
            }).Task;
        }


        private async void ModsTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var selected = ModsTreeView.SelectedItem;
            if (selected is ProjectFileNode node)
            {
                var extension = Path.GetExtension(node.Path);
                var supportedExtensions = new[] { ".txt", ".info", ".lua", ".xml", ".json", ".cfg", ".ini", ".md" };
                if (!node.IsFolder && supportedExtensions.Contains(extension))
                {
                    try
                    {
                        OpenedFilePath = node.Path;

                        var text = System.IO.File.ReadAllText(node.Path);
                        LuaEditor.Text = text;
                        LoadHighlighting(extension);

                        if (extension == ".lua") await LuaTester.Test(text);
                    }
                    catch
                    {
                        LuaEditor.Text = $"-- Error loading file: {node.Path}";
                    }
                }
                else
                {
                    LuaEditor.Clear();
                    LuaEditor.Text = $"-- Preview not available for this file type. ({extension})";
                }

                txtPropName.Text = node.Name;
                txtPropPath.Text = node.Path;

                if (System.IO.File.Exists(node.Path))
                {
                    var info = new FileInfo(node.Path);
                    txtPropSize.Text = $"{info.Length / 1024.0:F2} KB";

                    txtPropEncoding.Text = GetFileEncoding(node.Path).WebName;
                }
                else
                {
                    txtPropSize.Text = "-";
                    txtPropEncoding.Text = "-";
                }
            }
            else if (selected is ModTarget target)
            {
                if (target.Build == 0) txtPropName.Text = ProjectEngine.CurrentProject.Name;
                else txtPropName.Text = $"Build {target.Build}";

                txtPropPath.Text = target.Path;
                txtPropSize.Text = "-";
                txtPropEncoding.Text = "-";
            }
            else
            {
                txtPropName.Text = "";
                txtPropPath.Text = "";
                txtPropSize.Text = "";
                txtPropEncoding.Text = "";
            }
        }

        private System.Text.Encoding GetFileEncoding(string filename)
        {
            using (var reader = new StreamReader(filename, true))
            {
                if (reader.Peek() >= 0)
                    reader.Read();
                return reader.CurrentEncoding;
            }
        }

        private void LoadHighlighting(string ext)
        {
            string extension = ext.Replace(".", "").ToLower();
            foreach (var n in Assembly.GetExecutingAssembly().GetManifestResourceNames())
                Debug.WriteLine(n);

            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream($"PZTools.Resources.PZ{extension}.xshd");
            if (stream != null)
            {
                using (XmlTextReader reader = new XmlTextReader(stream))
                {
                    LuaEditor.SyntaxHighlighting = ICSharpCode.AvalonEdit.Highlighting.Xshd.HighlightingLoader.Load
                        (reader, ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance);
                }

            }
        }


        private void ModsTreeView_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (ModsTreeView.SelectedItem is ProjectFileNode node)
            {
                if (!node.IsFolder && System.IO.File.Exists(node.Path))
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = node.Path,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to open file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }
    }
}