using System.IO;
using System.Text;
using Newtonsoft.Json;
using PZTools.Core.Functions.Zomboid;
using PZTools.Core.Models;

namespace PZTools.Core.Functions.Projects
{
    public static class PlaytestProfileStore
    {
        private const string DirectoryName = ".pztools";
        private const string FileName = "playtest-profiles.json";

        public static string GetPath(ModProject project) => Path.Combine(project.RootPath, DirectoryName, FileName);

        public static List<PlaytestProfile> Load(ModProject project)
        {
            ArgumentNullException.ThrowIfNull(project);
            var path = GetPath(project);
            if (!File.Exists(path))
                return new List<PlaytestProfile> { CreateDefault(project) };

            try
            {
                var profiles = JsonConvert.DeserializeObject<List<PlaytestProfile>>(File.ReadAllText(path)) ?? new();
                foreach (var profile in profiles)
                    Normalize(profile, project);
                return profiles.Count > 0 ? profiles : new List<PlaytestProfile> { CreateDefault(project) };
            }
            catch (Exception ex)
            {
                throw new InvalidDataException($"Playtest profiles could not be read: {ex.Message}", ex);
            }
        }

        public static void Save(ModProject project, IEnumerable<PlaytestProfile> profiles)
        {
            ArgumentNullException.ThrowIfNull(project);
            ArgumentNullException.ThrowIfNull(profiles);
            var list = profiles.ToList();
            if (list.Count == 0)
                throw new InvalidOperationException("At least one playtest profile is required.");
            foreach (var profile in list)
            {
                Normalize(profile, project);
                var errors = Validate(profile, project);
                if (errors.Count > 0)
                    throw new InvalidDataException(string.Join(Environment.NewLine, errors));
            }

            var path = GetPath(project);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonConvert.SerializeObject(list, Formatting.Indented) + Environment.NewLine, new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
        }

        public static PlaytestProfile CreateDefault(ModProject project)
        {
            var primary = project.Targets.FirstOrDefault(x => x.IsPrimary) ?? project.Targets.FirstOrDefault();
            var profile = new PlaytestProfile
            {
                Build = primary?.Build ?? ZomboidGame.latestStableBuild,
                Dependencies = project.ModInfo.Requires.Select(x => new PlaytestDependency { ModId = x }).ToList()
            };
            Normalize(profile, project);
            return profile;
        }

        public static IReadOnlyList<string> Validate(PlaytestProfile profile, ModProject? project = null)
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(profile.Name))
                errors.Add("Profile name is required.");
            if (!double.IsFinite(profile.Build) || profile.Build <= 0)
                errors.Add("Build must be a positive number.");
            if (profile.ClientCount is < 1 or > 8)
                errors.Add("Client count must be between 1 and 8.");
            if (profile.ServerPort is < 1 or > 65534)
                errors.Add("Server port must be between 1 and 65534.");
            if (profile.MaxPlayers is < 1 or > 100)
                errors.Add("Maximum players must be between 1 and 100.");
            if (profile.ServerStartupTimeoutSeconds is < 10 or > 600)
                errors.Add("Server startup timeout must be between 10 and 600 seconds.");
            if (profile.WindowWidth is < 800 or > 7680)
                errors.Add("Game window width must be between 800 and 7680 pixels.");
            if (profile.WindowHeight is < 600 or > 4320)
                errors.Add("Game window height must be between 600 and 4320 pixels.");
            if (profile.Mode == PlaytestMode.DedicatedServer && string.IsNullOrWhiteSpace(profile.ServerName))
                errors.Add("Server name is required.");
            if (profile.ServerName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                errors.Add("Server name contains invalid filename characters.");
            if (profile.SaveMode == PlaytestSaveMode.FreshClone && !Directory.Exists(profile.SourceSavePath))
                errors.Add("The source save folder does not exist.");
            if (profile.Dependencies.Where(x => x.Enabled).Any(x => string.IsNullOrWhiteSpace(x.ModId)))
                errors.Add("Every enabled dependency needs a mod ID.");
            if (profile.Dependencies.Where(x => x.Enabled).GroupBy(x => x.ModId, StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1))
                errors.Add("Dependency mod IDs must be unique.");
            foreach (var dependency in profile.Dependencies.Where(x => x.Enabled && !string.IsNullOrWhiteSpace(x.SourcePath)))
                errors.AddRange(ModDependencyService.ValidateSource(dependency, profile.Build));
            if (profile.Mode == PlaytestMode.DedicatedServer && profile.ClientCount > 1 && !profile.NoSteam)
                errors.Add("Repeatable multi-client sessions require No-Steam mode because one Steam account cannot run multiple local clients.");
            if (profile.Mode == PlaytestMode.DedicatedServer && profile.NoSteam &&
                profile.Dependencies.Any(x => x.Enabled && string.IsNullOrWhiteSpace(x.SourcePath)))
                errors.Add("Every No-Steam dependency needs a local source folder because Workshop content is unavailable in No-Steam sessions.");
            if ((profile.LaunchArguments ?? "").IndexOfAny(new[] { '&', '|', '<', '>', '^' }) >= 0)
                errors.Add("Launch arguments contain shell control characters.");
            try
            {
                PlaytestServerConfig.ParseAdditionalOptions(profile.AdditionalServerOptions);
            }
            catch (Exception ex) { errors.Add(ex.Message); }
            if (project is not null)
            {
                try { DependencyLoadOrder.Resolve(project, profile); }
                catch (InvalidDataException ex) { errors.Add(ex.Message); }
            }
            return errors;
        }

        private static void Normalize(PlaytestProfile profile, ModProject project)
        {
            if (profile.Id == Guid.Empty)
                profile.Id = Guid.NewGuid();
            profile.Name = profile.Name?.Trim() ?? "";
            profile.ServerName = SanitizeServerName(profile.ServerName);
            profile.Dependencies ??= new();
            foreach (var dependency in profile.Dependencies)
                dependency.ModId = dependency.ModId?.Trim().TrimStart('\\') ?? "";
            foreach (var required in project.ModInfo.Requires.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                var id = required.Trim().TrimStart('\\');
                var dependency = profile.Dependencies.FirstOrDefault(x => x.ModId.Equals(id, StringComparison.OrdinalIgnoreCase));
                if (dependency is null)
                {
                    dependency = new PlaytestDependency { ModId = id };
                    profile.Dependencies.Add(dependency);
                }
                dependency.Enabled = true;
                dependency.IsProjectRequired = true;
            }
        }

        private static string SanitizeServerName(string value)
        {
            value = value?.Trim() ?? "";
            return value.Replace(' ', '_');
        }
    }
}
