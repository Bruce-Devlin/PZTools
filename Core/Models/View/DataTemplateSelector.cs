using PZTools.Core.Models.Menu;
using System.Windows;
using System.Windows.Controls;

namespace PZTools.Core.Models.View
{
    public class MenuItemSelector : DataTemplateSelector
    {
        public DataTemplate MenuItemTemplate { get; set; }
        public DataTemplate SeparatorTemplate { get; set; }

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
