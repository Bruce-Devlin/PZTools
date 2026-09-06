using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using PZTools.Core.Functions.Projects;
using PZTools.Core.Models;
using PZTools.Core.Windows;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var previousDirectory = Environment.CurrentDirectory;
        var root = Path.Combine(Path.GetTempPath(), "PZTools-Desktop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        MainWindow? window = null;
        try
        {
            Environment.CurrentDirectory = root;
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/PZTools;component/Core/Windows/Themes/Dark.xaml", UriKind.Relative) });
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/PZTools;component/Core/Windows/Styles/AppStyles.xaml", UriKind.Relative) });
            ProjectEngine.ProjectsRootPath = Path.Combine(root, "Projects");
            var project = ProjectEngine.CreateProject("DesktopChecks");
            ProjectEngine.LoadProject(project);
            var target = project.Targets.Single();
            var luaPath = Path.Combine(target.Path, "media", "lua", "shared", "Preview.lua");
            Directory.CreateDirectory(Path.GetDirectoryName(luaPath)!);
            File.WriteAllText(luaPath, string.Join('\n', Enumerable.Range(1, 300).Select(i => $"-- line {i} needle")));
            var plainPath = Path.Combine(target.Path, "notes.md");
            File.WriteAllText(plainPath, "Plain text for preview.");

            window = new MainWindow { WindowState = WindowState.Normal, Width = 1180, Height = 760 };
            typeof(MainWindow).GetProperty(nameof(MainWindow.IsReloading))!.SetValue(window, true);
            app.MainWindow = window;
            window.Show();
            Drain();
            Await(window.OpenFileForAgentAsync(luaPath, 120));
            var editor = Control<TextEditor>(window, "LuaEditor");
            Check(Control<Border>(window, "EditorEmptyState").Visibility == Visibility.Collapsed, "agent-opened file replaces empty state");
            Check(editor.TextArea.Caret.Line == 120, "preview opens requested line");
            Check(Control<TextBox>(window, "txtPropName").Text == "Preview.lua", "preview inspector follows opened file");
            CheckInspector(window, project, target, luaPath);
            using (var highlighter = new ICSharpCode.AvalonEdit.Highlighting.DocumentHighlighter(
                new ICSharpCode.AvalonEdit.Document.TextDocument("--[[\nlocal quoted = 'comment'\n]]\nlocal value = \"escaped \\\" quote\""), editor.SyntaxHighlighting!))
            {
                Check(highlighter.HighlightLine(2).Sections.Any(x => x.Color.Name == "Comment"), "Lua long comments span lines");
                Check(highlighter.HighlightLine(4).Sections.Any(x => x.Color.Name == "Keyword"), "Lua highlighting resumes after long comment");
            }
            editor.Select(editor.Document.GetLineByNumber(120).Offset, 7);
            editor.ScrollToVerticalOffset(1000);
            Drain();
            var oldOffset = editor.VerticalOffset;
            File.AppendAllText(luaPath, "\n-- appended externally");
            Await(window.RefreshFileForAgentAsync(luaPath));
            Drain();
            Check(editor.SelectedText == "-- line" && Math.Abs(editor.VerticalOffset - oldOffset) < 1, "external refresh preserves selection and scroll");

            Invoke(window, "ShowPreviewFind");
            Control<TextBox>(window, "PreviewFindQuery").Text = "needle";
            Check(editor.SelectedText == "needle", "find bar selects matching text");
            Check(Control<TextBlock>(window, "PreviewFindCount").Text == "1 / 300", "find bar counts matches");
            Capture(window, "workspace-dark");
            Invoke(window, "MoveFind", 1);
            Check(editor.Document.GetLineByOffset(editor.SelectionStart).LineNumber == 2, "find advances to next line");
            Await(window.OpenFileForAgentAsync(plainPath, null));
            Check(editor.SyntaxHighlighting == null || editor.SyntaxHighlighting.Name != "PZLua", "plain text does not inherit Lua highlighting");
            Check(Control<Border>(window, "PreviewFindBar").Visibility == Visibility.Collapsed, "changing files closes find bar");

            var imagePath = Path.Combine(target.Path, "poster.png");
            var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 128, 255, 255 }, 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var output = File.Create(imagePath)) encoder.Save(output);
            Await(window.OpenFileForAgentAsync(imagePath, null));
            Check(Control<Image>(window, "PreviewImage").Source != null && editor.Visibility == Visibility.Collapsed, "image preview renders without loading binary text");
            File.WriteAllBytes(imagePath, File.ReadAllBytes(imagePath));
            Check(true, "image preview releases file handle");

            var tree = Control<TreeView>(window, "ProjectTreeView");
            var targetItem = (TreeViewItem)tree.ItemContainerGenerator.ContainerFromItem(target);
            targetItem.IsSelected = true;
            targetItem.IsExpanded = true;
            Drain();
            Check(window.OpenedFilePath.Length == 0 && Control<Image>(window, "PreviewImage").Source == null, "build selection clears stale file context");
            var media = target.FileTree!.Children.Single(x => x.Name == "media");
            var mediaItem = (TreeViewItem)targetItem.ItemContainerGenerator.ContainerFromItem(media);
            mediaItem.IsExpanded = true;
            Drain();
            Check(((System.Collections.IDictionary)Field(window, "_folderWatchersByPath")!).Count > 0, "expanded folder is watched");

            window.Width = 900;
            window.Height = 640;
            Await(window.OpenFileForAgentAsync(luaPath, 120));
            Invoke(window, "ShowPreviewFind");
            Control<TextBox>(window, "PreviewFindQuery").Text = "needle";
            Drain();
            CheckButtonsFit(window);
            Capture(window, "workspace-dark-minimum");
            CaptureInspector(window, "inspector-dark-minimum");
            app.Resources.MergedDictionaries[0] = new ResourceDictionary { Source = new Uri("/PZTools;component/Core/Windows/Themes/Light.xaml", UriKind.Relative) };
            Invoke(window, "Workspace_ThemeChanged", window, "Light");
            Drain();
            var commentColor = editor.SyntaxHighlighting!.GetNamedColor("Comment").Foreground;
            Check(commentColor.Equals(new ICSharpCode.AvalonEdit.Highlighting.SimpleHighlightingBrush(Color.FromRgb(0x36, 0x75, 0x48))), "syntax palette follows light theme");
            CheckButtonsFit(window);
            Capture(window, "workspace-light-minimum");
            CaptureInspector(window, "inspector-light-minimum");
            window.Close();
            Drain();
            Check(((System.Collections.IDictionary)Field(window, "_folderWatchersByPath")!).Count == 0 && Field(window, "_openedFileWatcher") == null, "closing releases file watchers");
            app.Shutdown();
            Console.WriteLine("PZTools desktop smoke checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            if (window?.IsVisible == true) window.Close();
            Environment.CurrentDirectory = previousDirectory;
            if (Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()) + "PZTools-Desktop-", StringComparison.OrdinalIgnoreCase))
                Directory.Delete(root, recursive: true);
        }
    }

    private static void CheckInspector(MainWindow window, ModProject project, ModTarget target, string luaPath)
    {
        var sync = Control<CheckBox>(window, "InspectorSyncEnabled");
        Check(sync.IsEnabled && sync.IsChecked == false, "inspector offers sync for an unsynced file");
        var other = ProjectEngine.AddTarget(project, 43);
        var path = Path.Combine(target.Path, "inspector.txt");
        File.WriteAllText(path, "first");
        Await(window.OpenFileForAgentAsync(path, null));
        var readOnly = Control<CheckBox>(window, "InspectorReadOnly");
        var hidden = Control<CheckBox>(window, "InspectorHidden");
        readOnly.IsChecked = true;
        readOnly.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Check(File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly), "inspector persists read-only attribute");
        hidden.IsChecked = true;
        hidden.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Check(File.GetAttributes(path).HasFlag(FileAttributes.Hidden) && File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly), "hidden toggle preserves other attributes");
        readOnly.IsChecked = false;
        readOnly.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        hidden.IsChecked = false;
        hidden.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        window.UpdateTreeView();
        Drain();
        var tree = Control<TreeView>(window, "ProjectTreeView");
        var targetItem = (TreeViewItem)tree.ItemContainerGenerator.ContainerFromItem(target);
        targetItem.IsExpanded = true;
        Drain();
        var fileNode = target.FileTree!.Children.Single(x => x.Path == path);
        ((TreeViewItem)targetItem.ItemContainerGenerator.ContainerFromItem(fileNode)).IsSelected = true;
        Drain();
        Invoke(window, "StopVersionSyncWatcher"); // Keep manual conflict checks deterministic.
        Await((Task)window.GetType().GetMethod("SetInspectorSyncAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { true })!);
        var copy = Path.Combine(other.Path, "inspector.txt");
        Check(File.ReadAllText(copy) == "first" && sync.IsChecked == true, "inspector sync creates counterpart and updates toggle");
        Check(window.OpenedFilePath == path && Control<ScrollViewer>(window, "FilePropertiesContent").Visibility == Visibility.Visible, "sync preserves selected file inspector and preview");
        File.WriteAllText(path, "source change");
        File.WriteAllText(copy, "other change");
        Control<Button>(window, "InspectorSyncNow").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while ((bool)Field(window, "_inspectorSyncBusy")! && DateTime.UtcNow < deadline) { Drain(); Thread.Sleep(10); }
        Check(!(bool)Field(window, "_inspectorSyncBusy")!, "sync now completes");
        Check(File.ReadAllText(copy) == "other change" && Control<TextBlock>(window, "InspectorSyncStatus").Text.Contains("Conflict:"), "inspector reports conflicts without overwriting either copy");
        Await(window.OpenFileForAgentAsync(luaPath, null));
        Check(Control<TextBlock>(window, "InspectorSyncResult").Visibility == Visibility.Collapsed && sync.IsChecked == false, "changing files clears prior sync result");
        Await(window.OpenFileForAgentAsync(path, null));
        Check(sync.IsChecked == true, "sync setting survives reselecting file");
        Await((Task)window.GetType().GetMethod("SetInspectorSyncAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { false })!);
        Check(File.Exists(copy) && VersionSyncService.GetStatus(project, path) == VersionSyncStatus.Available, "disabling inspector sync retains copies and removes rule");
        var folder = Path.Combine(target.Path, "InspectorFolder");
        Directory.CreateDirectory(folder);
        var child = Path.Combine(folder, "child.txt");
        File.WriteAllText(child, "child");
        VersionSyncService.Enable(project, folder);
        Await(window.OpenFileForAgentAsync(child, null));
        Check(sync.IsChecked == true && !sync.IsEnabled && Control<Button>(window, "InspectorSyncNow").IsEnabled, "inherited sync is shown without offering an ineffective per-file toggle");
        Invoke(window, "InspectPath", target.Path);
        Check(!sync.IsEnabled && !Control<Button>(window, "InspectorSyncNow").IsEnabled && Control<StackPanel>(window, "InspectorFileSettings").Visibility == Visibility.Collapsed, "build roots disable sync and file attributes");
        Invoke(window, "InspectPath", Path.Combine(project.RootPath, "common"));
        Check(!sync.IsEnabled, "common content cannot enable version sync");
        File.Delete(child);
        Invoke(window, "InspectPath", child);
        Check(!sync.IsEnabled && !Control<StackPanel>(window, "InspectorFileSettings").IsEnabled, "missing files cannot mutate stale inspector settings");
        VersionSyncService.Disable(project, folder);
        Invoke(window, "StartVersionSyncWatcher");
        targetItem.IsSelected = true;
        targetItem.IsSelected = false;
        Await(window.OpenFileForAgentAsync(luaPath, 120));
    }

    private static void CheckButtonsFit(MainWindow window)
    {
        var button = Control<Button>(window, "RunGameBtn");
        var bounds = button.TransformToAncestor(window).TransformBounds(new Rect(button.RenderSize));
        Check(bounds.Left >= 0 && bounds.Right <= window.ActualWidth && bounds.Bottom <= window.ActualHeight, "run action fits minimum window size");
    }

    private static void CaptureInspector(MainWindow window, string name)
    {
        var panel = Control<ScrollViewer>(window, "FilePropertiesContent");
        Control<CheckBox>(window, "InspectorSyncEnabled").BringIntoView();
        Drain();
        var sync = Control<CheckBox>(window, "InspectorSyncEnabled");
        panel.ScrollToVerticalOffset(panel.VerticalOffset + sync.TransformToAncestor(panel).Transform(new Point()).Y - 26);
        Drain();
        Capture(window, name + "-sync");
        panel.ScrollToBottom();
        Drain();
        Check(panel.ScrollableHeight > 0 && panel.VerticalOffset > 0, "inspector settings remain reachable at minimum size");
        Capture(window, name + "-details");
        panel.ScrollToTop();
        Drain();
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("PZTOOLS_DESKTOP_CAPTURE");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        window.UpdateLayout();
        var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        image.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var output = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(output);
    }

    private static T Control<T>(Window window, string name) => (T)window.FindName(name);
    private static object? Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);
    private static void Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
    private static void Drain() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Await(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!task.IsCompleted && DateTime.UtcNow < deadline) { Drain(); Thread.Sleep(10); }
        if (!task.IsCompleted) throw new TimeoutException("Desktop operation timed out.");
        task.GetAwaiter().GetResult();
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("[PASS] " + message);
    }
}
