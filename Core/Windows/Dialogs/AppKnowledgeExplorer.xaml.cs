using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using PZTools.Core.Functions.Help;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace PZTools.Core.Windows.Dialogs
{
    public partial class AppKnowledgeExplorer : Window
    {
        private static AppKnowledgeExplorer? _openWindow;
        private readonly List<string> _history = [];
        private int _historyIndex = -1;
        private bool _updating;
        private double _fontSize = 15;

        public AppKnowledgeExplorer()
        {
            InitializeComponent();
            CategoryBox.ItemsSource = new[] { "All categories" }.Concat(AppKnowledgeBase.Articles.Select(a => a.Category).Distinct()).ToArray();
            CategoryBox.SelectedIndex = 0;
            Navigate("welcome");
        }

        public static void Open(Window? owner)
        {
            if (_openWindow != null)
            {
                if (_openWindow.WindowState == WindowState.Minimized)
                    _openWindow.WindowState = WindowState.Normal;
                _openWindow.Activate();
                return;
            }
            _openWindow = new AppKnowledgeExplorer { Owner = owner };
            _openWindow.Closed += (_, _) => _openWindow = null;
            _openWindow.Show();
        }

        private void Filters_Changed(object sender, RoutedEventArgs e)
        {
            if (ArticleList == null || CategoryBox == null || _updating)
                return;
            var matches = AppKnowledgeBase.Search(SearchBox.Text, CategoryBox.SelectedIndex > 0 ? CategoryBox.SelectedItem as string : null);
            _updating = true;
            ArticleList.ItemsSource = matches;
            ArticleList.SelectedItem = matches.FirstOrDefault(a => _historyIndex >= 0 && a.Id == _history[_historyIndex]);
            _updating = false;
            ResultCount.Text = matches.Count == 0 ? "No articles found. Try fewer words or All categories." : $"{matches.Count} of {AppKnowledgeBase.Articles.Count} articles";
        }

        private void ArticleList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_updating && ArticleList.SelectedItem is HelpArticle article)
                Navigate(article.Id);
        }

        private void Navigate(string id, bool addHistory = true)
        {
            var article = AppKnowledgeBase.Articles.First(a => a.Id == id);
            if (addHistory && (_historyIndex < 0 || _history[_historyIndex] != id))
            {
                _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
                _history.Add(id);
                _historyIndex = _history.Count - 1;
            }
            Render(article);
            _updating = true;
            ArticleList.SelectedItem = ArticleList.Items.Cast<HelpArticle>().FirstOrDefault(a => a.Id == id);
            if (ArticleList.SelectedItem != null)
                ArticleList.ScrollIntoView(ArticleList.SelectedItem);
            _updating = false;
            BackButton.IsEnabled = _historyIndex > 0;
            ForwardButton.IsEnabled = _historyIndex < _history.Count - 1;
        }

        private void Render(HelpArticle article)
        {
            var document = new FlowDocument { FontFamily = FontFamily, FontSize = _fontSize, PagePadding = new Thickness(26), TextAlignment = TextAlignment.Left };
            document.SetResourceReference(FlowDocument.ForegroundProperty, "Brush.TextPrimary");
            document.Blocks.Add(new Paragraph(new Run(article.Category)) { FontSize = 12, Margin = new Thickness(0, 0, 0, 8) });
            document.Blocks.Add(new Paragraph(new Run(article.Title)) { FontSize = 28, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) });
            var contents = new Paragraph { Margin = new Thickness(0, 0, 0, 18), FontSize = 13 };
            document.Blocks.Add(contents);
            foreach (var raw in article.Body.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0)
                    continue;
                var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 12), LineHeight = _fontSize * 1.55 };
                if (line.StartsWith("## "))
                {
                    line = line[3..];
                    paragraph.FontSize = _fontSize + 4;
                    paragraph.FontWeight = FontWeights.SemiBold;
                    paragraph.Margin = new Thickness(0, 14, 0, 10);
                    var jump = new Hyperlink(new Run(line));
                    jump.SetResourceReference(Hyperlink.ForegroundProperty, "Brush.TextInfo");
                    jump.Click += (_, _) => paragraph.BringIntoView();
                    if (contents.Inlines.Count > 0)
                        contents.Inlines.Add(new Run("   ·   "));
                    contents.Inlines.Add(jump);
                }
                else if (line.StartsWith("- "))
                {
                    line = "•  " + line[2..];
                    paragraph.Margin = new Thickness(12, 0, 0, 8);
                }
                var position = 0;
                foreach (Match match in Regex.Matches(line, @"\[([^\]]+)\]\(([^)]+)\)"))
                {
                    paragraph.Inlines.Add(new Run(line[position..match.Index]));
                    var link = new Hyperlink(new Run(match.Groups[1].Value));
                    var target = match.Groups[2].Value;
                    link.SetResourceReference(Hyperlink.ForegroundProperty, "Brush.TextInfo");
                    link.Click += (_, _) => Navigate(target);
                    paragraph.Inlines.Add(link);
                    position = match.Index + match.Length;
                }
                paragraph.Inlines.Add(new Run(line[position..]));
                document.Blocks.Add(paragraph);
            }
            ArticleReader.Document = document;
            document.Blocks.FirstBlock?.BringIntoView();
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (_historyIndex > 0) Navigate(_history[--_historyIndex], false);
        }

        private void Forward_Click(object sender, RoutedEventArgs e)
        {
            if (_historyIndex + 1 < _history.Count) Navigate(_history[++_historyIndex], false);
        }

        private void Home_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Clear();
            CategoryBox.SelectedIndex = 0;
            Navigate("welcome");
        }

        private void Smaller_Click(object sender, RoutedEventArgs e) => ResizeText(-1);
        private void Larger_Click(object sender, RoutedEventArgs e) => ResizeText(1);
        private void ResizeText(int delta)
        {
            _fontSize = Math.Clamp(_fontSize + delta, 12, 24);
            if (_historyIndex >= 0) Render(AppKnowledgeBase.Articles.First(a => a.Id == _history[_historyIndex]));
        }

        private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F)
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Alt && e.SystemKey is Key.Left or Key.Right)
            {
                if (e.SystemKey == Key.Left) Back_Click(sender, e); else Forward_Click(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape) Close();
        }
    }
}
