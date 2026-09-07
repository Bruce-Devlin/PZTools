using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PZTools.Core.Functions.Help;
using PZTools.Core.Windows.Dialogs;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        try
        {
            Check(AppKnowledgeBase.Articles.Count >= 30, "extensive embedded catalog loads with valid IDs and links");
            Check(AppKnowledgeBase.Search("PuBlIsHeD FiLe").Any(a => a.Id == "workshop-settings"), "case-insensitive full-text multiword search");
            Check(AppKnowledgeBase.Search("", "Steam Workshop").Count == 2, "category filter");
            Check(AppKnowledgeBase.Search("no-such-help-article-928432").Count == 0, "empty results");
            foreach (var theme in new[] { "Dark", "Light" })
            {
                app.Resources.MergedDictionaries.Clear();
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"Themes/{theme}.xaml", UriKind.Relative) });
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("Styles/AppStyles.xaml", UriKind.Relative) });
                var window = new AppKnowledgeExplorer();
                window.Show();
                Drain();
                var search = (TextBox)window.FindName("SearchBox");
                var list = (ListBox)window.FindName("ArticleList");
                var reader = (FlowDocumentScrollViewer)window.FindName("ArticleReader");
                Check(Text(reader).Contains("Welcome to PZ Tools"), "home article rendered");
                search.Text = "published file";
                list.SelectedItem = list.Items.Cast<HelpArticle>().First(a => a.Id == "workshop-settings");
                Check(Text(reader).Contains("Prepare Workshop settings"), "search selection opens article");
                var related = reader.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Hyperlink>()).Last();
                related.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
                Check(Text(reader).Contains("Review and upload a Workshop package"), "related article link navigates");
                ((Button)window.FindName("BackButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(Text(reader).Contains("Prepare Workshop settings"), "back history");
                ((Button)window.FindName("ForwardButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(Text(reader).Contains("Review and upload a Workshop package"), "forward history");
                search.Text = "no-such-help-article-928432";
                Check(list.Items.Count == 0 && ((TextBlock)window.FindName("ResultCount")).Text.Contains("No articles"), "visible empty search state");
                Check(Text(reader).Contains("Review and upload"), "empty search retains article");
                Invoke(window, "Home_Click", window, new RoutedEventArgs());
                Check(search.Text == "" && list.Items.Count == AppKnowledgeBase.Articles.Count, "home resets filters");
                var navigation = typeof(AppKnowledgeExplorer).GetMethod("Navigate", BindingFlags.Instance | BindingFlags.NonPublic)!;
                foreach (var article in AppKnowledgeBase.Articles)
                {
                    navigation.Invoke(window, [article.Id, true]);
                    Drain();
                    Check(Text(reader).Contains(article.Title), $"render {article.Id}");
                }
                Invoke(window, "Home_Click", window, new RoutedEventArgs());
                Capture(window, $"help-{theme.ToLowerInvariant()}");
                window.Width = 760;
                window.Height = 520;
                Invoke(window, "Larger_Click", window, new RoutedEventArgs());
                Drain();
                Check(reader.ActualWidth >= 350, "minimum-size reading area");
                Capture(window, $"help-{theme.ToLowerInvariant()}-compact");
                window.Close();
            }
            Console.WriteLine($"PASS: {AppKnowledgeBase.Articles.Count} articles; search, links, history, empty state, and WPF rendering in both themes.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { app.Shutdown(); }
    }

    private static string Text(FlowDocumentScrollViewer reader) => new TextRange(reader.Document.ContentStart, reader.Document.ContentEnd).Text;
    private static void Invoke(object target, string method, params object[] args) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Drain() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Capture(Window window, string name)
    {
        Drain();
        var target = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        target.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        var directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(directory);
        using var file = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(file);
    }
}
