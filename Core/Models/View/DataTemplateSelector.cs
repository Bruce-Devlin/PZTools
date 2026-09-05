using System.Windows;
using System.Windows.Controls;
using PZTools.Core.Models.Menu;

namespace PZTools.Core.Models.View
{
    public class MenuItemSelector : DataTemplateSelector
    {
        public DataTemplate MenuItemTemplate { get; set; } = null!;
        public DataTemplate SeparatorTemplate { get; set; } = null!;

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            if (item is MenuItemDef def)
            {
                return def.IsSeparator ? SeparatorTemplate : MenuItemTemplate;
            }
            return base.SelectTemplate(item, container);
        }
    }
}
