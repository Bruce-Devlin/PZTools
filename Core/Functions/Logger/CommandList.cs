using PZTools.Core.Models.Commands;

namespace PZTools.Core.Functions.Logger
{
    internal class CommandList
    {
        public static List<CommandTemplate> allLaunchArguments =
        [
            new CommandTemplate()
            {
                Title = "exit",
                Method = Exit,
                AllowedCLI = true,
                Description = "Exit the application. [Use: \"/exit\"]"
            },
            new CommandTemplate()
            {
                Title = "help",
                Method = Help,
                AllowedCLI = true,
                Description = "Display all commands and their usage. [Use: \"/help\"]"
            }
        ];

        private static async Task Exit(string[] parameters)
        {
            await Console.Log("Exiting...");
            App.CloseApp();
            return;
        }

        private static async Task Help(string[] parameters)
        {
            await Console.Log("Here is a list of commands:");
            var commands = allLaunchArguments.Where(c => c.AllowedCLI).ToList();
            foreach (var command in commands)
            {
                await Console.Log($"/{command.Title} - {command.Description}");
            }

            return;
        }
    }
}
