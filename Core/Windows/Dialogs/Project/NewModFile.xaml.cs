using System.Windows;
using System.Windows.Controls;
using PZTools.Core.Functions.Projects;
using PZTools.Core.Models;

namespace PZTools.Core.Windows.Dialogs.Project
{
    public partial class NewModFile : Window
    {
        private readonly ModProject _project;
        private static readonly (string Label, ModFileTemplate Template, string Folder, string Description)[] Templates =
        {
            ("Shared Lua module", ModFileTemplate.SharedLua, "media/lua/shared", "Loaded by both clients and servers. Best for shared gameplay systems and data."),
            ("Client Lua script", ModFileTemplate.ClientLua, "media/lua/client", "Loaded on clients. Use for UI, local input, rendering, and player-facing behavior."),
            ("Server Lua script", ModFileTemplate.ServerLua, "media/lua/server", "Loaded by the host or dedicated server. Use for authoritative multiplayer logic."),
            ("Item definition", ModFileTemplate.ItemScript, "media/scripts", "Creates a ZedScript module with a starter item definition."),
            ("Recipe definition", ModFileTemplate.RecipeScript, "media/scripts", "Creates a ZedScript module with a starter recipe definition."),
            ("English translation", ModFileTemplate.Translation, "media/lua/shared/Translate/EN", "Creates Build 42 JSON localization (or the legacy Build 41 text format) based on the selected target."),
            ("Project README", ModFileTemplate.Readme, "project root", "Creates player and contributor documentation for the mod.")
        };

        public NewModFile(ModProject project)
        {
            InitializeComponent();
            _project = project;
            TargetCombo.ItemsSource = project.Targets;
            TargetCombo.SelectedIndex = 0;
            TemplateCombo.ItemsSource = Templates.Select(x => x.Label);
            TemplateCombo.SelectedIndex = 0;
            NameTextBox.Text = project.ModInfo.Id.Length > 0 ? project.ModInfo.Id : project.Name.Replace(" ", "");
        }

        private void TemplateCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TemplateCombo.SelectedIndex < 0)
                return;
            var selected = Templates[TemplateCombo.SelectedIndex];
            DestinationText.Text = selected.Folder;
            TemplateDescription.Text = selected.Description;
        }

        private void Create_Click(object sender, RoutedEventArgs e)
        {
            if (TargetCombo.SelectedItem is not ModTarget target || TemplateCombo.SelectedIndex < 0)
                return;
            try
            {
                var path = ModScaffolder.Create(_project, target, Templates[TemplateCombo.SelectedIndex].Template, NameTextBox.Text);
                App.MainWindow?.UpdateTreeView();
                if (OpenAfterCreateCheck.IsChecked == true)
                    EditorIntegration.OpenInVsCode(_project, path);
                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Could not create file", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
