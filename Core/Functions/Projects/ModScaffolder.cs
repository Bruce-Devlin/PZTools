using System.IO;
using System.Text;
using PZTools.Core.Models;

namespace PZTools.Core.Functions.Projects
{
    public enum ModFileTemplate
    {
        SharedLua,
        ClientLua,
        ServerLua,
        ItemScript,
        RecipeScript,
        Translation,
        Readme
    }

    public static class ModScaffolder
    {
        public static string Create(ModProject project, ModTarget target, ModFileTemplate template, string name)
        {
            ArgumentNullException.ThrowIfNull(project);
            ArgumentNullException.ThrowIfNull(target);

            var root = template == ModFileTemplate.Readme ? project.RootPath : target.Path;
            return Create(root, target.Build, template, name);
        }

        public static string Create(ModTarget target, ModFileTemplate template, string name)
        {
            ArgumentNullException.ThrowIfNull(target);
            return Create(target.Path, target.Build, template, name);
        }

        private static string Create(string targetRoot, double build, ModFileTemplate template, string name)
        {
            name = ValidateName(name);

            var relativeFolder = template switch
            {
                ModFileTemplate.SharedLua => Path.Combine("media", "lua", "shared"),
                ModFileTemplate.ClientLua => Path.Combine("media", "lua", "client"),
                ModFileTemplate.ServerLua => Path.Combine("media", "lua", "server"),
                ModFileTemplate.ItemScript or ModFileTemplate.RecipeScript => Path.Combine("media", "scripts"),
                ModFileTemplate.Translation => Path.Combine("media", "lua", "shared", "Translate", "EN"),
                ModFileTemplate.Readme => "",
                _ => throw new ArgumentOutOfRangeException(nameof(template))
            };
            var extension = template switch
            {
                ModFileTemplate.SharedLua or ModFileTemplate.ClientLua or ModFileTemplate.ServerLua => ".lua",
                ModFileTemplate.ItemScript or ModFileTemplate.RecipeScript => ".txt",
                ModFileTemplate.Translation => build >= 42 ? ".json" : ".txt",
                ModFileTemplate.Readme => ".md",
                _ => ".txt"
            };
            var safeName = name.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? name : name + extension;
            var folder = Path.Combine(targetRoot, relativeFolder);
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, safeName);
            if (File.Exists(path))
                throw new IOException($"'{safeName}' already exists in {relativeFolder}.");

            File.WriteAllText(path, BuildContent(template, Path.GetFileNameWithoutExtension(name), build), new UTF8Encoding(false));
            return path;
        }

        private static string BuildContent(ModFileTemplate template, string name, double build)
        {
            var identifier = ToIdentifier(name);
            return template switch
            {
                ModFileTemplate.SharedLua =>
                    $"--- Shared code is loaded by clients and dedicated servers.{Environment.NewLine}" +
                    $"local {identifier} = {{}}{Environment.NewLine}{Environment.NewLine}" +
                    $"function {identifier}.init(){Environment.NewLine}    -- Register shared systems here.{Environment.NewLine}end{Environment.NewLine}{Environment.NewLine}" +
                    $"Events.OnGameBoot.Add({identifier}.init){Environment.NewLine}{Environment.NewLine}return {identifier}{Environment.NewLine}",

                ModFileTemplate.ClientLua =>
                    $"--- Client-only UI and input code.{Environment.NewLine}" +
                    $"local function onCreatePlayer(playerIndex, player){Environment.NewLine}    -- Initialise client state here.{Environment.NewLine}end{Environment.NewLine}{Environment.NewLine}" +
                    $"Events.OnCreatePlayer.Add(onCreatePlayer){Environment.NewLine}",

                ModFileTemplate.ServerLua =>
                    $"--- Dedicated-server and host-side code.{Environment.NewLine}" +
                    $"local function onServerStarted(){Environment.NewLine}    print(\"[{identifier}] server started\"){Environment.NewLine}end{Environment.NewLine}{Environment.NewLine}" +
                    $"Events.OnServerStarted.Add(onServerStarted){Environment.NewLine}",

                ModFileTemplate.ItemScript when build >= 42 =>
                    $"module {identifier}{Environment.NewLine}{{{Environment.NewLine}    item ExampleItem{Environment.NewLine}    {{{Environment.NewLine}" +
                    $"        ItemType = base:normal,{Environment.NewLine}        Weight = 1.0,{Environment.NewLine}    }}{Environment.NewLine}}}{Environment.NewLine}",

                ModFileTemplate.ItemScript =>
                    $"module {identifier}{Environment.NewLine}{{{Environment.NewLine}    item ExampleItem{Environment.NewLine}    {{{Environment.NewLine}" +
                    $"        DisplayName = Example Item,{Environment.NewLine}        Type = Normal,{Environment.NewLine}        Weight = 1.0,{Environment.NewLine}    }}{Environment.NewLine}}}{Environment.NewLine}",

                ModFileTemplate.RecipeScript when build >= 42 =>
                    $"module {identifier}{Environment.NewLine}{{{Environment.NewLine}    craftRecipe MakeExampleItem{Environment.NewLine}    {{{Environment.NewLine}" +
                    $"        Time = 50,{Environment.NewLine}        Tags = InHandCraft,{Environment.NewLine}        inputs{Environment.NewLine}        {{{Environment.NewLine}" +
                    $"            item 1 [Base.Plank],{Environment.NewLine}        }}{Environment.NewLine}        outputs{Environment.NewLine}        {{{Environment.NewLine}" +
                    $"            item 1 Base.Plank,{Environment.NewLine}        }}{Environment.NewLine}    }}{Environment.NewLine}}}{Environment.NewLine}",

                ModFileTemplate.RecipeScript =>
                    $"module {identifier}{Environment.NewLine}{{{Environment.NewLine}    recipe Make Example Item{Environment.NewLine}    {{{Environment.NewLine}" +
                    $"        Base.Plank,{Environment.NewLine}{Environment.NewLine}        Result:Base.Plank,{Environment.NewLine}        Time:50.0,{Environment.NewLine}    }}{Environment.NewLine}}}{Environment.NewLine}",

                ModFileTemplate.Translation when build >= 42 =>
                    $"{{{Environment.NewLine}  \"{identifier}_DisplayName\": \"Display name\",{Environment.NewLine}  \"{identifier}_Description\": \"Description\"{Environment.NewLine}}}{Environment.NewLine}",

                ModFileTemplate.Translation =>
                    $"{identifier}_EN = {{{Environment.NewLine}    {identifier}_DisplayName = \"Display name\",{Environment.NewLine}    {identifier}_Description = \"Description\",{Environment.NewLine}}}{Environment.NewLine}",

                ModFileTemplate.Readme =>
                    $"# {name}{Environment.NewLine}{Environment.NewLine}## Features{Environment.NewLine}{Environment.NewLine}- Describe the mod here.{Environment.NewLine}{Environment.NewLine}" +
                    $"## Compatibility{Environment.NewLine}{Environment.NewLine}- Project Zomboid build compatibility{Environment.NewLine}- Multiplayer status{Environment.NewLine}- Required mods{Environment.NewLine}",
                _ => string.Empty
            };
        }

        private static string ValidateName(string name)
        {
            name = name.Trim();
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Enter a file name.");
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains(Path.DirectorySeparatorChar) || name.Contains(Path.AltDirectorySeparatorChar))
                throw new ArgumentException("The file name contains invalid characters.");
            return name;
        }

        private static string ToIdentifier(string value)
        {
            var chars = value.Where(char.IsLetterOrDigit).ToArray();
            var result = new string(chars);
            if (string.IsNullOrWhiteSpace(result))
                return "MyMod";
            if (char.IsDigit(result[0]))
                result = "Mod" + result;
            return result;
        }
    }
}
