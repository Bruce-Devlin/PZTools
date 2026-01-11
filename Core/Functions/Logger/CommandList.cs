
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
            }
        ];

        private static async Task Exit(string[] parameters)
        {
            await Console.Log("Exiting...");
            Environment.Exit(0);
            return;

        }
    }
}
