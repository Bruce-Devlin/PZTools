using PZTools.Core.Functions.Projects;
using PZTools.Core.Models;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using TreeView = System.Windows.Controls.TreeView;


namespace PZTools.Core.Windows.Dialogs.Project
{
    public partial class ProjectSelector : Window
    {
        public ObservableCollection<ModProject> Mods { get; set; } = new();

        public ModProject? SelectedMod { get; private set; }
        public ModTarget? SelectedTarget { get; private set; }




        public ProjectSelector()
        {
            InitializeComponent();

            var loaded = ProjectEngine.LoadMods();
            foreach (var mod in loaded)
                Mods.Add(mod);

            ModFolderTxt.Text = "Mods Folder: " + ProjectEngine.ModsRootPath;

            ModsTreeView.ItemsSource = Mods;

            ModsTreeView.SelectedItemChanged += ModsTreeView_SelectedItemChanged;
        }

        private void ModsTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (ModsTreeView.SelectedItem is ModTarget target)
            {
                SelectedTarget = target;
                foreach (var mod in Mods)
                {
                    if (mod.Targets.Contains(target))
                    {
                        SelectedMod = mod;
                        break;
                    }
                }
            }
            else if (ModsTreeView.SelectedItem is ModProject mod)
            {
                SelectedMod = mod;
                SelectedTarget = null;
            }
        }

        private void SelectTreeViewItem(TreeView treeView, object itemToSelect)
        {
            if (itemToSelect == null) return;

            TreeViewItem? treeViewItem = GetTreeViewItem(treeView, itemToSelect);
            if (treeViewItem != null)
            {
                treeViewItem.IsSelected = true;
                treeViewItem.BringIntoView();
            }
        }

        private TreeViewItem? GetTreeViewItem(ItemsControl container, object item)
        {
            if (container == null) return null;

            for (int i = 0; i < container.Items.Count; i++)
            {
                var currentItem = container.Items[i];

                TreeViewItem? treeViewItem = container.ItemContainerGenerator.ContainerFromItem(currentItem) as TreeViewItem;
                if (treeViewItem == null) continue;

                if (currentItem == item)
                    return treeViewItem;

                TreeViewItem? child = GetTreeViewItem(treeViewItem, item);
                if (child != null) return child;
            }

            return null;
        }


        private void NewModButton_Click(object sender, RoutedEventArgs e)
        {
            var inputDialogs = new InputDialogs("Enter new mod name:", "New Mod");
            if (inputDialogs.ShowDialog() == true)
            {
                string modName = inputDialogs.ResponseText.Trim();
                if (string.IsNullOrWhiteSpace(modName))
                {
                    MessageBox.Show("Mod name cannot be empty.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                try
                {
                    var newMod = ProjectEngine.CreateMod(modName);
                    Mods.Add(newMod);
                    ModsTreeView.Items.Refresh();

                    SelectedMod = newMod;
                    SelectedTarget = null;
                    SelectTreeViewItem(ModsTreeView, newMod);

                    MessageBox.Show($"Mod '{modName}' created successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to create mod: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedMod == null)
            {
                System.Windows.MessageBox.Show("Please select a mod.", "Select Mod", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ProjectEngine.LoadProject(SelectedMod);

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
