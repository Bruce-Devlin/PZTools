using System.Windows.Input;

namespace PZTools.Core.Models.Menu
{
    public class MenuItemDef
    {
        public string Header { get; set; } = "";
        public ICommand? Command { get; set; }
        public List<MenuItemDef> Children { get; set; } = new();
        public bool IsSeparator { get; set; } = false;
    }

}
