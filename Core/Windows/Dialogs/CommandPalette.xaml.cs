using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PZTools.Core.Models.Menu;

namespace PZTools.Core.Windows.Dialogs;

public partial class CommandPalette : Window
{
    public sealed record Entry(string Label, ICommand Command);
    private readonly List<Entry> _entries;
    public ICommand? SelectedCommand { get; private set; }

    public CommandPalette(IEnumerable<MenuItemDef> menus)
    {
        _entries = Flatten(menus).ToList();
        InitializeComponent();
        RefreshResults();
        Loaded += (_, _) => Query.Focus();
    }

    private static IEnumerable<Entry> Flatten(IEnumerable<MenuItemDef> menus, string prefix = "")
    {
        foreach (var menu in menus.Where(x => !x.IsSeparator))
        {
            var label = prefix + menu.Header.Replace("_", "");
            if (menu.Command is { } command) yield return new Entry(label, command);
            foreach (var child in Flatten(menu.Children, label + " › ")) yield return child;
        }
    }

    private void Query_Changed(object sender, TextChangedEventArgs e)
    {
        if (Results != null) RefreshResults();
    }

    private void RefreshResults()
    {
        var words = Query.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var matches = _entries.Where(x => x.Command.CanExecute(null) &&
            words.All(word => x.Label.Contains(word, StringComparison.OrdinalIgnoreCase))).ToList();
        Results.ItemsSource = matches;
        Results.SelectedIndex = matches.Count > 0 ? 0 : -1;
        Hint.Text = matches.Count == 0 ? "No available commands match your search." :
            $"{matches.Count} commands · ↑ ↓ to select · Enter to run · Esc to close";
    }

    private void Palette_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        else if (e.Key == Key.Enter) { Accept(); e.Handled = true; }
        else if (e.Key is Key.Down or Key.Up && Results.Items.Count > 0)
        {
            Results.SelectedIndex = Math.Clamp(Results.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, Results.Items.Count - 1);
            Results.ScrollIntoView(Results.SelectedItem);
            e.Handled = true;
        }
    }

    private void Results_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(Results, e.OriginalSource as DependencyObject) is ListBoxItem) Accept();
    }

    private void Accept()
    {
        if (Results.SelectedItem is Entry entry && entry.Command.CanExecute(null))
        {
            SelectedCommand = entry.Command;
            DialogResult = true;
        }
    }
}
