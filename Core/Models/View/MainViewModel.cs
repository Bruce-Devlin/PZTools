using System.ComponentModel;
using System.Runtime.CompilerServices;
using PZTools.Core.Functions.Menu;
using PZTools.Core.Functions.Zomboid;
using PZTools.Core.Models.Menu;

namespace PZTools.Core.Models.View
{
    public class MainViewModel : INotifyPropertyChanged, IDisposable
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private bool _disposed;

        protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public List<MenuItemDef> Menus { get; }
        public string RunGameButtonText => ZomboidGame.IsRunning ? "Stop Game" : "Run Game";

        public MainViewModel()
        {
            Menus = MenuBuilder.BuildFrom(typeof(MenuButtonEvents));
            ZomboidGame.StateChanged += OnGameStateChanged;
        }

        private void OnGameStateChanged()
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (_disposed || dispatcher == null || dispatcher.HasShutdownStarted) return;
            _ = dispatcher.InvokeAsync(() =>
            {
                if (!_disposed) RaisePropertyChanged(nameof(RunGameButtonText));
            });
        }

        public void Dispose()
        {
            _disposed = true;
            ZomboidGame.StateChanged -= OnGameStateChanged;
        }
    }
}
