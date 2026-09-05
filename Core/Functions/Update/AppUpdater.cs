using System.Net.Http;
using System.Reflection;
using System.Windows;
using Newtonsoft.Json.Linq;

namespace PZTools.Core.Functions.Update
{
    internal enum UpdateChannel
    {
        Stable, Test, Dev
    }

    internal static class AppUpdater
    {
        private const string ReleasesApiUrl = "https://api.github.com/repos/Bruce-Devlin/PZTools/releases";
        private const string ReleasesPageUrl = "https://github.com/Bruce-Devlin/PZTools/releases";

        public static UpdateChannel UpdateChannel { get; private set; } = UpdateChannel.Stable;
        public static Version? AvailableVersion { get; private set; }
        public static string? AvailableVersionName { get; private set; }
        public static string? AvailableReleaseUrl { get; private set; }

        public static UpdateChannel GetChannelFromSettings()
        {
            var savedChannel = Config.GetAppSetting<string>("UpdateChannel");
            return Enum.TryParse<UpdateChannel>(savedChannel, true, out var channel) ? channel : UpdateChannel.Stable;
        }

        public static void SwitchUpdateChannel(UpdateChannel newChannel) => UpdateChannel = newChannel;

        public static async Task<bool> CheckForUpdates(CancellationToken cancellationToken = default)
        {
            SwitchUpdateChannel(GetChannelFromSettings());
            AvailableVersion = null;
            AvailableVersionName = null;
            AvailableReleaseUrl = null;

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("PZTools-update-checker");
                http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

                var json = await http.GetStringAsync(ReleasesApiUrl, cancellationToken);
                var release = JArray.Parse(json).OfType<JObject>()
                    .Where(r => r.Value<bool?>("draft") != true)
                    .Where(IsReleaseAllowedForChannel)
                    .Select(r => new
                    {
                        Version = ParseVersion(r.Value<string>("tag_name")),
                        Name = r.Value<string>("name") ?? r.Value<string>("tag_name"),
                        Url = r.Value<string>("html_url")
                    })
                    .Where(r => r.Version != null)
                    .OrderByDescending(r => r.Version)
                    .FirstOrDefault();

                if (release?.Version is null)
                    return false;
                var currentVersion = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0);
                if (release.Version <= currentVersion)
                    return false;

                AvailableVersion = release.Version;
                AvailableVersionName = release.Name ?? $"v{release.Version}";
                AvailableReleaseUrl = release.Url ?? ReleasesPageUrl;
                await Console.Log($"PZTools update available: {AvailableVersionName} (installed: {currentVersion}).");
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                await Console.Log($"Update check failed: {ex.Message}", Console.LogLevel.Warning);
                return false;
            }
        }

        public static Task<bool> StartUpdate()
        {
            if (AvailableVersion is null)
                return Task.FromResult(false);

            var result = MessageBox.Show(
                $"{AvailableVersionName ?? $"v{AvailableVersion}"} is available. Open the release page to download it?",
                "PZTools Update Available", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (result != MessageBoxResult.Yes)
                return Task.FromResult(false);

            var error = WindowsHelpers.OpenFile(AvailableReleaseUrl ?? ReleasesPageUrl);
            if (!string.IsNullOrWhiteSpace(error))
            {
                MessageBox.Show($"Could not open the release page: {error}", "Update Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return Task.FromResult(false);
            }

            return Task.FromResult(true);
        }

        private static bool IsReleaseAllowedForChannel(JObject release)
            => UpdateChannel != UpdateChannel.Stable || release.Value<bool?>("prerelease") != true;

        private static Version? ParseVersion(string? tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return null;
            var value = tag.Trim().TrimStart('v', 'V');
            var suffix = value.IndexOfAny(new[] { '-', '+' });
            if (suffix >= 0)
                value = value[..suffix];
            return Version.TryParse(value, out var version) ? version : null;
        }
    }
}
