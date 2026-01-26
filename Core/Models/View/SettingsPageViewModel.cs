using System.Collections.ObjectModel;

namespace PZTools.Core.Models.View
{
    public abstract class SettingsPageViewModel : ObservableObject
    {
        protected SettingsPageViewModel(AppSettings settings)
        {
            Settings = settings;
        }

        public AppSettings Settings { get; }

        public abstract string Title { get; }
        public abstract string Description { get; }
    }

    public sealed class SystemSettingsPageViewModel : SettingsPageViewModel
    {
        public SystemSettingsPageViewModel(AppSettings settings) : base(settings) { }

        public override string Title => "System";
        public override string Description => "Install paths, game management mode.";

        public ObservableCollection<string> GameModeOptions { get; } = new() { "Existing", "Managed" };

    }

    public sealed class GeneralSettingsPageViewModel : SettingsPageViewModel
    {
        public GeneralSettingsPageViewModel(AppSettings settings) : base(settings) { }

        public override string Title => "General";
        public override string Description => "Theme, startup defaults, confirmations.";

        public ObservableCollection<string> ThemeOptions { get; } = new() { "Dark", "Light" };
    }

    public sealed class EditorSettingsPageViewModel : SettingsPageViewModel
    {
        public EditorSettingsPageViewModel(AppSettings settings) : base(settings) { }

        public override string Title => "Editor";
        public override string Description => "Font, tabs, wrapping, default editor app.";
    }

    public sealed class UpdatesSettingsPageViewModel : SettingsPageViewModel
    {
        public UpdatesSettingsPageViewModel(AppSettings settings) : base(settings) { }

        public override string Title => "Updates";
        public override string Description => "Update channel and startup checks.";

        private string _statusText = "Idle";
        public string StatusText
        {
            get => _statusText;
            set => Set(ref _statusText, value);
        }

        public ObservableCollection<string> ChannelOptions { get; } = new() { "Stable" };

        public RelayCommand CheckNowCommand => new(() =>
        {
            StatusText = "Checking...";
            Settings.LastUpdateCheckUtc = System.DateTime.UtcNow;
            Raise(nameof(Settings));
            StatusText = "Done";
        });
    }
}
