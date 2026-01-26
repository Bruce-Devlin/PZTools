using MoonSharp.Interpreter.Interop;
using PZTools.Core.Models.View;

namespace PZTools.Core.Models
{
    public sealed class AppSettings : ObservableObject
    {
        public AppSetting[] GetAll()
        {
            var allProps = this.GetType().GetProperties();
            List<AppSetting> result = new List<AppSetting>();
            foreach (var prop in allProps)
            {
                if (prop.IsPropertyInfoPublic() && prop.CanWrite)
                {
                    var valueObj = prop.GetValue(this);
                    if (valueObj == null) continue;

                    var valueString = valueObj.ToString() ?? string.Empty;

                    result.Add(new AppSetting(prop.Name, valueString));
                }
            }

            return result.ToArray();
        }

        public void Reset()
        {
            var excludedProps = new HashSet<string>
            {
                nameof(AppInstallPath),
                nameof(GameMode),
                nameof(ExistingGamePath),
                nameof(ManagedGamePath),
                nameof(UpdateChannel)
            };

            var defaultSettings = new AppSettings();

            foreach (var prop in GetType().GetProperties())
            {
                if (!prop.CanWrite || !prop.IsPropertyInfoPublic())
                    continue;

                if (excludedProps.Contains(prop.Name))
                    continue;

                var defaultValue = prop.GetValue(defaultSettings);
                prop.SetValue(this, defaultValue);
            }
        }


        // System
        private string _appInstallPath = string.Empty;
        public string AppInstallPath { get => _appInstallPath; set => Set(ref _appInstallPath, value); }

        private string _gameMode = "Existing";
        public string GameMode { get => _gameMode; set => Set(ref _gameMode, value); }

        private string _existingGamePath = string.Empty;
        public string ExistingGamePath { get => _existingGamePath; set => Set(ref _existingGamePath, value); }

        private string _managedGamePath = string.Empty;
        public string ManagedGamePath { get => _managedGamePath; set => Set(ref _managedGamePath, value); }

        // General
        private string _theme = "Dark";
        public string Theme { get => _theme; set => Set(ref _theme, value); }

        private bool _confirmOnExit = true;
        public bool ConfirmOnExit { get => _confirmOnExit; set => Set(ref _confirmOnExit, value); }

        // Editor
        private double _editorFontSize = 14;
        public double EditorFontSize { get => _editorFontSize; set => Set(ref _editorFontSize, value); }

        private bool _editorWordWrap = true;
        public bool EditorWordWrap { get => _editorWordWrap; set => Set(ref _editorWordWrap, value); }

        private string _defaultFileEditorApp = string.Empty;
        public string DefaultFileEditorApp { get => _defaultFileEditorApp; set => Set(ref _defaultFileEditorApp, value); }

        private string _defaultFileEditorArgs = string.Empty;
        public string DefaultFileEditorArgs { get => _defaultFileEditorArgs; set => Set(ref _defaultFileEditorArgs, value); }

        // Updates
        private bool _checkUpdatesOnStartup = true;
        public bool CheckUpdatesOnStartup { get => _checkUpdatesOnStartup; set => Set(ref _checkUpdatesOnStartup, value); }

        private string _updateChannel = "Stable";
        public string UpdateChannel { get => _updateChannel; set => Set(ref _updateChannel, value); }

        private DateTime? _lastUpdateCheckUtc;
        public DateTime? LastUpdateCheckUtc { get => _lastUpdateCheckUtc; set => Set(ref _lastUpdateCheckUtc, value); }
    }

    public class AppSetting
    {
        public AppSetting(string name, string value)
        {
            this.Name = name;
            this.Value = value;
        }

        public string Name { get; set; }
        public string Value { get; set; }
    }
}
