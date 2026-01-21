using PZTools.Core.Functions.Menu;
using PZTools.Core.Models.Menu;

namespace PZTools.Core.Models.View
{
    public class MainViewModel
    {
        public List<MenuItemDef> Menus { get; }

        public MainViewModel()
        {
            Menus = MenuBuilder.BuildFrom(typeof(MenuButtonEvents));
        }
    }
}
