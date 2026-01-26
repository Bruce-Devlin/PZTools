using PZTools.Core.Functions.Menu;
using PZTools.Core.Functions.Zomboid;
using PZTools.Core.Models.Menu;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace PZTools.Core.Models.View
{
    public class MainViewModel : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public List<MenuItemDef> Menus { get; }
        public string RunGameButtonText => ZomboidGame.IsRunning ? "Stop Game" : "Run Game";

        public MainViewModel()
        {
            Menus = MenuBuilder.BuildFrom(typeof(MenuButtonEvents));

            ZomboidGame.StateChanged += () =>
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    RaisePropertyChanged(nameof(RunGameButtonText)));
        }
    }
}
