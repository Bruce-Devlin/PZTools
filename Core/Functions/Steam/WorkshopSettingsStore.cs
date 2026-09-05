using System.IO;
using System.Text;
using Newtonsoft.Json;
using PZTools.Core.Models;

namespace PZTools.Core.Functions.Steam
{
    public static class WorkshopSettingsStore
    {
        private const string FileName = "workshop-settings.json";

        public static string GetPath(ModProject project) => Path.Combine(project.RootPath, ".pztools", FileName);

        public static WorkshopSettings Load(ModProject project)
        {
            ArgumentNullException.ThrowIfNull(project);
            var path = GetPath(project);
            WorkshopSettings settings;
            if (File.Exists(path))
            {
                try
                {
                    settings = JsonConvert.DeserializeObject<WorkshopSettings>(File.ReadAllText(path)) ?? new WorkshopSettings();
                }
                catch (Exception ex) when (ex is JsonException or IOException)
                {
                    throw new InvalidDataException($"Workshop settings could not be read: {ex.Message}", ex);
                }
            }
            else
            {
                settings = new WorkshopSettings
                {
                    Title = project.ModInfo.Name,
                    Description = project.ModInfo.Description ?? ""
                };

                var legacyId = Config.GetVariable(VariableType.user, $"{project.Name}-workshopPublishedFileId");
                if (IsPublishedFileId(legacyId))
                    settings.PublishedFileId = legacyId!;
            }

            Normalize(settings, project);
            return settings;
        }

        public static void Save(ModProject project, WorkshopSettings settings)
        {
            ArgumentNullException.ThrowIfNull(project);
            ArgumentNullException.ThrowIfNull(settings);
            Normalize(settings, project);
            var errors = Validate(settings, requireUploadFields: false);
            if (errors.Count > 0)
                throw new InvalidDataException(string.Join(Environment.NewLine, errors));

            var path = GetPath(project);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonConvert.SerializeObject(settings, Formatting.Indented) + Environment.NewLine, new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
        }

        public static IReadOnlyList<string> Validate(WorkshopSettings settings, bool requireUploadFields = true)
        {
            var errors = new List<string>();
            if (requireUploadFields && string.IsNullOrWhiteSpace(settings.Title))
                errors.Add("Workshop title is required.");
            if (settings.Title.Length > 128)
                errors.Add("Workshop title must be 128 characters or fewer.");
            if (requireUploadFields && string.IsNullOrWhiteSpace(settings.Description))
                errors.Add("Workshop description is required.");
            if (!IsPublishedFileId(settings.PublishedFileId) && settings.PublishedFileId != "0")
                errors.Add("Published Workshop item ID must be 0 for a new item or a numeric Steam Published File ID.");
            if (requireUploadFields && settings.Tags.Count == 0)
                errors.Add("Add at least one Workshop tag.");
            if (settings.Tags.Any(x => x.Contains(',') || x.Contains('\n') || x.Contains('\r')))
                errors.Add("Enter one Workshop tag per line; tags cannot contain commas or line breaks.");
            if (!Enum.IsDefined(settings.Visibility))
                errors.Add("Select a valid Workshop visibility.");
            return errors;
        }

        public static string VisibilityLabel(WorkshopVisibility visibility) => visibility switch
        {
            WorkshopVisibility.FriendsOnly => "friends-only",
            WorkshopVisibility.Private => "private",
            WorkshopVisibility.Unlisted => "unlisted",
            _ => "public"
        };

        private static void Normalize(WorkshopSettings settings, ModProject project)
        {
            settings.Title = string.IsNullOrWhiteSpace(settings.Title) ? project.ModInfo.Name.Trim() : settings.Title.Trim();
            settings.Description = string.IsNullOrWhiteSpace(settings.Description) ? (project.ModInfo.Description ?? "").Trim() : settings.Description.Trim();
            settings.PublishedFileId = string.IsNullOrWhiteSpace(settings.PublishedFileId) ? "0" : settings.PublishedFileId.Trim();
            settings.DefaultChangeNote = string.IsNullOrWhiteSpace(settings.DefaultChangeNote) ? "Updated with PZTools" : settings.DefaultChangeNote.Trim();
            settings.Tags ??= new();
            settings.Tags = settings.Tags.Select(x => x?.Trim() ?? "").Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (settings.Tags.Count == 0)
                settings.Tags.Add("Mod");
        }

        private static bool IsPublishedFileId(string? value) => ulong.TryParse(value, out var parsed) && parsed > 0;
    }
}
