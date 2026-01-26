using System.Windows;
using Application = System.Windows.Application;

namespace PZTools.Core.Functions.Theme
{
    public static class ThemeManager
    {
        private static readonly Uri DarkThemeUri = new("pack://application:,,,/Core/Windows/Themes/Dark.xaml", UriKind.Absolute);
        private static readonly Uri LightThemeUri = new("pack://application:,,,/Core/Windows/Themes/Light.xaml", UriKind.Absolute);

        private const string ThemeMarkerKey = "PZTools.ThemeDictionaryMarker";

        public static string CurrentTheme { get; private set; } = "Dark";

        public static event EventHandler<string>? ThemeChanged;

        public static async Task ApplyThemeFromSettings()
        {
            var theme = Config.GetAppSetting<string>("Theme");

            switch (theme)
            {
                case "Light":
                    await ApplyThemeAsync("Light");
                    break;

                default:
                    await ApplyThemeAsync("Dark");
                    break;
            }
        }

        public static Task ApplyThemeAsync(string themeName)
        {
            if (string.IsNullOrWhiteSpace(themeName))
                themeName = "Dark";

            return Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var themeUri = ResolveThemeUri(themeName);

                var newThemeDict = new ResourceDictionary { Source = themeUri };

                newThemeDict[ThemeMarkerKey] = true;

                var merged = Application.Current.Resources.MergedDictionaries;

                var existingThemeDict = merged.FirstOrDefault(d => d.Contains(ThemeMarkerKey))
                                       ?? merged.FirstOrDefault(d => d.Source != null &&
                                                                    (d.Source.OriginalString.Contains("/Themes/", StringComparison.OrdinalIgnoreCase) ||
                                                                     d.Source.OriginalString.Contains("Themes/", StringComparison.OrdinalIgnoreCase)));

                if (existingThemeDict != null)
                {
                    var index = merged.IndexOf(existingThemeDict);
                    merged.RemoveAt(index);
                    merged.Insert(index, newThemeDict);
                }
                else
                {
                    merged.Insert(0, newThemeDict);
                }

                CurrentTheme = NormalizeThemeName(themeName);
                ThemeChanged?.Invoke(null, CurrentTheme);

            }).Task;
        }

        private static Uri ResolveThemeUri(string themeName)
        {
            var normalized = NormalizeThemeName(themeName);
            return normalized switch
            {
                "Light" => LightThemeUri,
                _ => DarkThemeUri
            };
        }

        private static string NormalizeThemeName(string themeName)
        {
            return themeName.Trim().Equals("Light", StringComparison.OrdinalIgnoreCase)
                ? "Light"
                : "Dark";
        }
    }
}
