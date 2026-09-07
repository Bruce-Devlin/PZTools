using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PZTools.Core.Models;
using PZTools.Core.Windows.Dialogs;
using PZTools.Core.Functions.Decompile;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var root = Path.Combine(Path.GetTempPath(), "PZTools-Tree-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        GameKnowledgeTree? window = null;
        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            foreach (var resource in new[] { "Themes/Dark.xaml", "Styles/AppStyles.xaml" })
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/PZTools;component/Core/Windows/{resource}") });
            File.WriteAllText(Path.Combine(root, "Actor.java"), """
                package zombie.graph;
                public class Actor {
                    private Service service;
                    public void update(Service input) {
                        Service local = new Service();
                        service.run();
                        input.run();
                        local.run();
                    }
                }
                """);
            File.WriteAllText(Path.Combine(root, "Service.java"), """
                package zombie.graph;
                public class Service {
                    public Service() {
                    }
                    public void run() {
                    }
                }
                """);
            var service = typeof(GameKnowledgeExplorer).Assembly.GetType("PZTools.Core.Functions.Decompile.GameKnowledgeBase")!;
            var index = ((Task<GameKnowledgeIndex>)service.GetMethod("LoadOrBuildAsync", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, new object?[] { root, "UI fixture", false, null, CancellationToken.None })!).GetAwaiter().GetResult();
            VerifyStreaming(index, args.FirstOrDefault());
            window = new GameKnowledgeTree(index) { Width = 1440, Height = 900 };
            window.Show();
            var status = (TextBlock)window.FindName("StatusText");
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!status.Text.Contains("matching classes") && DateTime.UtcNow < deadline) Pump();
            Expect(status.Text.Contains("matching classes"), "graph loads in real WPF window: " + status.Text);
            var tree = (TreeView)window.FindName("ClassTree");
            var package = (TreeViewItem)tree.Items[0]; package.IsExpanded = true; Pump();
            var actor = package.Items.Cast<TreeViewItem>().Single(i => i.Header.ToString()!.Contains("Actor"));
            actor.IsExpanded = true; Pump();
            var update = actor.Items.Cast<TreeViewItem>().Single(i => i.Header.ToString()!.Contains("update"));
            update.IsSelected = true; update.IsExpanded = true; Pump();
            while (((TextBlock)window.FindName("LoadStateText")).Text != "Relationships loaded" && DateTime.UtcNow < deadline) Pump();
            Expect(update.Items.Count == 2, "expansion exposes parameter and local variable");
            var map = (Canvas)window.FindName("Map");
            var run = map.Children.OfType<Button>().First(b => b.Content is TextBlock t && t.Text.Contains("Call (inferred)") && t.Text.Contains("run"));
            run.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            var title = (TextBlock)window.FindName("FocusTitle");
            Expect(title.Text.EndsWith("Service.run"), "clicking relationship navigates to method");
            Expect(map.Children.OfType<Button>().Any(b => b.Content is TextBlock t && t.Text.Contains("Actor.update")), "called method shows reverse caller");
            ((Button)window.FindName("BackButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Expect(title.Text.EndsWith("Actor.update"), "back restores previous object");
            Expect(((TextBox)window.FindName("SourceText")).Text.Contains("service.run();"), "source pane contains implementation");
            var filter = (TextBox)window.FindName("FilterBox"); filter.Text = "run";
            var filterDeadline = DateTime.UtcNow.AddSeconds(2);
            while (DateTime.UtcNow < filterDeadline && !status.Text.Contains("1 matching")) Pump();
            Expect(status.Text.Contains("1 matching"), "member search filters containing classes");
            filter.Text = "";
            while (DateTime.UtcNow < filterDeadline && !status.Text.Contains("2 matching")) Pump();
            package = (TreeViewItem)tree.Items[0]; package.IsExpanded = true; Pump();
            actor = package.Items.Cast<TreeViewItem>().Single(i => i.Header.ToString()!.Contains("Actor"));
            actor.IsExpanded = true; Pump();
            update = actor.Items.Cast<TreeViewItem>().Single(i => i.Header.ToString()!.Contains("update"));
            update.IsExpanded = true; update.IsSelected = true; Pump();
            ((Slider)window.FindName("ZoomSlider")).Value = 0.8; Pump();
            Expect(map.LayoutTransform is ScaleTransform { ScaleX: 0.8 }, "map zoom responds to slider");
            if (args.Length > 0)
            {
                window.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(window);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(Path.GetFullPath(args[0])); png.Save(output);
            }
            window.Width = 1000; window.Height = 650; Pump();
            Expect(((TextBox)window.FindName("SourceText")).ActualHeight > 80, "source pane remains available at minimum window size");
            window.Close(); window = null;
            Console.WriteLine("Knowledge tree WPF checks passed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { window?.Close(); Directory.Delete(root, true); }
    }

    private static void VerifyStreaming(GameKnowledgeIndex index, string? screenshot)
    {
        var window = new GameKnowledgeTree(index);
        // Drive the production batch handler at deterministic worker boundaries.
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var load = typeof(GameKnowledgeTree).GetMethod("LoadGraph", flags)!;
        window.Loaded -= (RoutedEventHandler)Delegate.CreateDelegate(typeof(RoutedEventHandler), window, load);
        typeof(GameKnowledgeTree).GetField("_graph", flags)!.SetValue(window, new GameKnowledgeGraph());
        var apply = typeof(GameKnowledgeTree).GetMethod("ApplyBatch", flags)!;
        window.Show();
        TreeViewItem? method = null;
        TreeViewItem? actor = null;
        var batches = 0;
        var selectedSource = false;
        var dispatcher = window.Dispatcher;
        using var cancellation = new CancellationTokenSource();
        var task = Task.Run(() => GameKnowledgeGraph.BuildAsync(index, cancellationToken: cancellation.Token, publish: batch =>
            dispatcher.InvokeAsync(() =>
            {
                apply.Invoke(window, new object[] { batch });
                batches++;
                var tree = (TreeView)window.FindName("ClassTree");
                var source = (TextBox)window.FindName("SourceText");
                if (batches == 1)
                {
                    var package = (TreeViewItem)tree.Items[0]; package.IsExpanded = true;
                    actor = package.Items.Cast<TreeViewItem>().Single(i => i.Header.ToString()!.Contains("Actor"));
                    actor.IsExpanded = true;
                    method = actor.Items.Cast<TreeViewItem>().Single(i => i.Header.ToString()!.Contains("update"));
                    method.IsExpanded = true; method.IsSelected = true;
                    Expect(!batch.Finished && source.Text.Contains("loading") && method.Items.Count == 1,
                        "real tree is browsable with pending variables before any source inference");
                    ((Slider)window.FindName("ZoomSlider")).Value = 0.8;
                    if (screenshot != null)
                    {
                        window.UpdateLayout();
                        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                        bitmap.Render(window);
                        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                        using var output = File.Create(Path.ChangeExtension(Path.GetFullPath(screenshot), ".pending.png")); png.Save(output);
                    }
                }
                else
                {
                    Expect(ReferenceEquals(tree.SelectedItem, method) && method!.IsExpanded && actor!.IsExpanded,
                        "stream update preserves selected and expanded tree items");
                    if (!selectedSource && source.Text.Contains("service.run();")) { source.Select(0, 6); selectedSource = true; }
                    else if (selectedSource) Expect(source.SelectionLength == 6, "relationship updates preserve source text selection");
                    if (batch.Finished)
                    {
                        Expect(method!.Items.Count == 2 && method.Items.Cast<TreeViewItem>().All(i => i.Tag != null),
                            "new variables replace pending marker inside the already expanded method");
                        Expect(((Slider)window.FindName("ZoomSlider")).Value == 0.8, "background updates preserve map zoom");
                    }
                }
            }, DispatcherPriority.Background).Task));
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!task.IsCompleted && DateTime.UtcNow < deadline) Pump();
            Expect(task.IsCompleted, "streaming UI worker finishes");
            task.GetAwaiter().GetResult();
            Expect(batches >= 3 && selectedSource, "WPF received separate skeleton, source and relationship batches");
        }
        finally { cancellation.Cancel(); window.Close(); }
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
        Thread.Sleep(10);
    }
    private static void Expect(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
    }
}
