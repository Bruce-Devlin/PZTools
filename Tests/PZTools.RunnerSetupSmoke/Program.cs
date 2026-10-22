using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PZTools;
using PZTools.Core.Functions.Projects;
using PZTools.Core.Models;
using PZTools.Core.Windows.Dialogs.Project;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var cwd = Environment.CurrentDirectory;
        var root = Path.Combine(Path.GetTempPath(), "PZToolsRunnerSetup", Guid.NewGuid().ToString("N"));
        var captures = Path.Combine(cwd, ".verify", "runner-setup"); Directory.CreateDirectory(captures);
        Directory.CreateDirectory(root); AppPaths.SetCurrentDirectory(root);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/PZTools;component/Core/Windows/Themes/Dark.xaml", UriKind.Relative) });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/PZTools;component/Core/Windows/Styles/AppStyles.xaml", UriKind.Relative) });
        try
        {
            ProjectEngine.ProjectsRootPath = Path.Combine(root, "projects");
            var project = ProjectEngine.CreateProject("RunnerSetup"); ProjectEngine.LoadProject(project);
            if (args.Length == 2 && args[0] == "--live-server-root")
            {
                using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                using var runner = new PlaytestSessionRunner();
                var profile = PlaytestSetupService.Create(project, PlaytestMode.DedicatedServer);
                profile.KeepSessionData = true;
                var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var log = Path.Combine(captures, "live-server-local-only.log");
                runner.Output += (_, line) =>
                {
                    lock (runner) File.AppendAllText(log, line + Environment.NewLine);
                    if (line.Contains("SERVER STARTED", StringComparison.OrdinalIgnoreCase))
                        ready.TrySetResult();
                };
                var workspace = PlaytestWorkspaceService.Prepare(project, profile);
                Await(ProjectDeployer.DeployProject(project, Path.Combine(workspace.ServerCachePath, "mods")));
                typeof(PlaytestSetupService).Assembly.GetType("PZTools.Core.Functions.Projects.PlaytestLocalConnection")!.GetMethod("Prepare")!.Invoke(null, new object[] { profile, workspace });
                PlaytestServerConfig.Write(profile, workspace, project.ModInfo.Id, project);
                try
                {
                    var server = (System.Diagnostics.Process)typeof(PlaytestSessionRunner).GetMethod("StartServer", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(runner, new object[] { profile, workspace, args[1] })!;
                    Await(ready.Task.WaitAsync(stop.Token));
                    Check(true, "Installed bundled dedicated server reached SERVER STARTED with an isolated project and loopback settings");
                    Await((Task)typeof(PlaytestSessionRunner).GetMethod("StopServerGracefullyAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(runner, new object[] { server, stop.Token })!);
                    Check(server.ExitCode == 0, "Dedicated server saves and exits cleanly on quit");
                }
                finally { Await(runner.StopAsync()); }
                Check(!File.ReadAllText(log).Contains("trying to setup port forwarding rules"), "Local-only server skips UPnP port forwarding");
                Console.WriteLine("Server startup and shutdown only; no client or character acceptance. Log: " + log);
                return 0;
            }
            foreach (var theme in new[] { "Dark", "Light" })
            {
                app.Resources.MergedDictionaries[0] = new ResourceDictionary { Source = new Uri($"/PZTools;component/Core/Windows/Themes/{theme}.xaml", UriKind.Relative) };
                var setup = new TestRunnerSetup(project) { Width = 720, Height = 640 };
                setup.Show(); Drain(); Await((Task)Invoke(setup, "CheckAsync")!);
                Check(!Control<Button>(setup, "UseProfile").IsEnabled, "Missing installation or character cannot be saved as ready");
                CheckFooter(setup); Capture(setup, Path.Combine(captures, $"singleplayer-{theme}.png"));
                Control<RadioButton>(setup, "Multiplayer").IsChecked = true;
                Await((Task)Invoke(setup, "CheckAsync")!); Drain();
                Check(Control<StackPanel>(setup, "SavePanel").Visibility == Visibility.Collapsed, "Multiplayer does not ask for a single-player save");
                Check(Control<TextBlock>(setup, "Connection").Text.Contains("PZTools1 and PZTools2"), "Two local identities explained");
                CheckFooter(setup); Capture(setup, Path.Combine(captures, $"multiplayer-{theme}.png"));
                Control<Expander>(setup, "ServerOptions").IsExpanded = true; Drain();
                var scroller = Descendants(setup).OfType<ScrollViewer>().First(); scroller.ScrollToEnd(); Drain();
                CheckFooter(setup); Capture(setup, Path.Combine(captures, $"multiplayer-checks-{theme}.png"));
                setup.Close(); Drain();

                var explorer = new TestExplorer(project) { Width = 800, Height = 600 };
                explorer.Show(); Drain();
                Check(Control<Button>(explorer, "Setup").IsVisible, "Setup is discoverable in Test Explorer");
                Control<TextBlock>(explorer, "EndpointStatus").Text = "client-1: awaiting_barrier | ticks 2 | World is paused.\nclient-2: awaiting_player | ticks 0 | No living playable character.\nserver: awaiting_barrier | ticks 50";
                Drain();
                Capture(explorer, Path.Combine(captures, $"explorer-{theme}.png")); explorer.Close();
                var lab = new RunProject { Width = 940, Height = 680 };
                lab.Show(); Drain();
                Check(Control<Button>(lab, "btnGuidedSetup").IsVisible, "Playtest Lab opens with guided setup");
                Capture(lab, Path.Combine(captures, $"lab-{theme}.png")); lab.Close(); Drain();
            }
            Check(!File.Exists(PlaytestProfileStore.GetPath(project)), "Viewing and closing setup does not overwrite profiles");
            Console.WriteLine("Runner setup desktop checks passed. Captures: " + captures);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { app.Shutdown(); AppPaths.SetCurrentDirectory(cwd); }
    }

    private static void CheckFooter(Window window)
    {
        foreach (var name in new[] { "Cancel", "UseProfile" })
        {
            var button = Control<Button>(window, name);
            var bounds = button.TransformToAncestor(window).TransformBounds(new Rect(button.RenderSize));
            Check(bounds.Left >= 0 && bounds.Right <= window.ActualWidth && bounds.Bottom <= window.ActualHeight, name + " remains reachable at minimum size");
            var text = Descendants(button).OfType<TextBlock>().FirstOrDefault();
            if (text is not null) Check(text.ActualWidth + button.Padding.Left + button.Padding.Right <= button.ActualWidth + 2, name + " label fits");
        }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static T Control<T>(Window window, string name) where T : class => (T)window.FindName(name);
    private static object? Invoke(object target, string name) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, null);
    private static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); Console.WriteLine("[PASS] " + message); }
    private static void Await(Task task)
    {
        var frame = new DispatcherFrame(); task.ContinueWith(_ => Application.Current.Dispatcher.BeginInvoke(() => frame.Continue = false));
        Dispatcher.PushFrame(frame); task.GetAwaiter().GetResult(); Drain();
    }
    private static void Drain()
    {
        var frame = new DispatcherFrame(); Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => frame.Continue = false); Dispatcher.PushFrame(frame);
    }
    private static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var output = File.Create(path); encoder.Save(output);
    }
}
