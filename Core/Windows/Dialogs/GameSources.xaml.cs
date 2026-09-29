using System.IO;
using System.Windows;
using PZTools.Core.Functions;
using PZTools.Core.Functions.Zomboid;

namespace PZTools.Core.Windows.Dialogs;

public partial class GameSources : Window
{
    public sealed record BuildEntry(string Name, string Path);
    private sealed record Category(string Label, string Path);
    private string? _selectedPath;

    public GameSources()
    {
        InitializeComponent();
        Categories.ItemsSource = new[]
        {
            new Category("Lua — client, server, shared, and translations", "media/lua"),
            new Category("Scripts — items, recipes, vehicles, and definitions", "media/scripts"),
            new Category("Maps", "media/maps"),
            new Category("Textures and texture packs", "media/texturepacks"),
            new Category("Models", "media/models_X"),
            new Category("Animations", "media/anims_X"),
            new Category("Sound", "media/sound"),
            new Category("All game media", "media"),
            new Category("Workshop examples", "Workshop")
        };
        try { Builds.ItemsSource = DiscoverBuilds(ZomboidGame.GameDirectory, ZomboidGame.GameMode.Equals("Managed", StringComparison.OrdinalIgnoreCase)); }
        catch (Exception ex) { SourcePath.Text = $"Could not inspect game installations: {ex.Message}"; }
        Builds.SelectedIndex = 0;
        Categories.SelectedIndex = 0;
        UpdatePath();
    }

    public static IReadOnlyList<BuildEntry> DiscoverBuilds(string root, bool managed)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return [];
        var folders = managed ? Directory.EnumerateDirectories(root) : new[] { root };
        return folders.Where(x => Directory.Exists(System.IO.Path.Combine(x, "media")))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(x => new BuildEntry(managed ? System.IO.Path.GetFileName(x) : "Configured game installation", x)).ToList();
    }

    private void Selection_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdatePath();

    private void UpdatePath()
    {
        if (SourcePath is null || OpenButton is null) return;
        _selectedPath = Builds.SelectedItem is BuildEntry build && Categories.SelectedItem is Category category
            ? System.IO.Path.Combine(build.Path, category.Path.Replace('/', System.IO.Path.DirectorySeparatorChar)) : null;
        OpenButton.IsEnabled = _selectedPath != null && Directory.Exists(_selectedPath);
        SourcePath.Text = _selectedPath is null ? "No game build with media was found. Configure your installation in File > App Options > System." :
            OpenButton.IsEnabled ? _selectedPath : $"This content folder is not present in the selected build.\n{_selectedPath}";
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            UpdatePath();
            if (OpenButton.IsEnabled) WindowsHelpers.ShowInExplorer(_selectedPath!);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Game content references", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
