using PZTools.Core.Models.Menu;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Input;
using MenuItem = System.Windows.Controls.MenuItem;

namespace PZTools.Core.Functions.Menu
{
    public static class MenuBuilder
    {
        public static List<MenuItemDef> BuildFrom(Type rootType)
        {
            return rootType
                .GetNestedTypes(BindingFlags.Public)
                .Select(BuildMenuGroup)
                .ToList();
        }

        private static MenuItemDef BuildMenuGroup(Type groupType)
        {
            var menu = new MenuItemDef
            {
                Header = "_" + groupType.Name.Replace("_", " ")
            };

            foreach (var prop in groupType.GetProperties(BindingFlags.Public | BindingFlags.Static))
            {
                if (prop.Name == "Separator")
                {
                    menu.Children.Add(new MenuItemDef
                    {
                        IsSeparator = true
                    });
                    continue;
                }

                if (!typeof(ICommand).IsAssignableFrom(prop.PropertyType))
                    continue;

                var cmd = (ICommand?)prop.GetValue(null);

                menu.Children.Add(new MenuItemDef
                {
                    Header = FormatHeader(prop.Name),
                    Command = cmd
                });
            }

            foreach (var nested in groupType.GetNestedTypes(BindingFlags.Public))
            {
                menu.Children.Add(BuildMenuGroup(nested));
            }

            return menu;
        }

        private static string FormatHeader(string name)
        {
            name = name.Replace("_", " ");
            return "_" + name;
        }

        public static void ReplaceSeparators(System.Windows.Controls.Menu menu)
        {
            for (int i = 0; i < menu.Items.Count; i++)
            {
                var container = menu.ItemContainerGenerator.ContainerFromIndex(i) as MenuItem;
                if (container == null) continue;

                if (container.DataContext is MenuItemDef def && def.IsSeparator)
                {
                    int index = i;
                    menu.Items.RemoveAt(index);
                    menu.Items.Insert(index, new Separator());
                    continue;
                }

                if (container.HasItems)
                {
                    ReplaceSeparators(container);
                }
            }
        }

        private static void ReplaceSeparators(MenuItem menuItem)
        {
            for (int i = 0; i < menuItem.Items.Count; i++)
            {
                var item = menuItem.Items[i];
                if (item is MenuItemDef def && def.IsSeparator)
                {
                    menuItem.Items.RemoveAt(i);
                    menuItem.Items.Insert(i, new Separator());
                }

                if (item is MenuItem mi2 && mi2.HasItems)
                    ReplaceSeparators(mi2);
            }
        }
    }
}
