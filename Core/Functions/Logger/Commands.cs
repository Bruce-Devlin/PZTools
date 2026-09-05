using System.Text.RegularExpressions;
using PZTools.Core.Models.Commands;

namespace PZTools.Core.Functions.Logger
{
    internal class Commands
    {
        public static bool TryParseCommand(string? command, out Command? parsedCommand, bool CLI = false)
        {
            if (command == null)
            {
                parsedCommand = null;
                return false;
            }

            try
            {
                if (command.StartsWith("-"))
                    command = command.TrimStart('-');
                else if (command.StartsWith("/"))
                    command = command.TrimStart('/');

                Command tryParsedCommand = ParseCommand(command);
                parsedCommand = tryParsedCommand;
                CommandTemplate? commandTemplate =
                        CommandList.allLaunchArguments
                            .FirstOrDefault(
                                x => x.Title.ToUpper()
                                    .Equals(tryParsedCommand.Title.ToUpper()));

                if (commandTemplate != null)
                {
                    if (commandTemplate.AllowedCLI.Equals(false) && CLI)
                        return false;

                    for (int i = 0; i < parsedCommand.Parameters.Length; i++)
                    {
                        string currentParam = parsedCommand.Parameters[i];
                        if (currentParam.StartsWith('\"'))
                            currentParam = currentParam.TrimStart('\"');
                        if (currentParam.EndsWith('\"'))
                            currentParam = currentParam.TrimEnd('\"');

                        parsedCommand.Parameters[i] = currentParam;
                    }

                    parsedCommand.Method = commandTemplate.Method;

                    if (parsedCommand != null)
                        return true;
                    else
                        return false;
                }
                else
                    return false;
            }
            catch
            {
                parsedCommand = null;
                return false;
            }
        }

        public static Command ParseCommand(string arg)
        {
            string[] argParts = Regex.Matches(arg, "(?:[^\\s\\\"]+|\\\"[^\\\"]*\\\")+")
                .Select(m => m.Value.Trim().Trim('"'))
                .Where(v => v.Length > 0)
                .ToArray();
            if (argParts.Length == 0)
                throw new ArgumentException("Command cannot be empty.", nameof(arg));
            Command parsedCommand = new Command(argParts[0], argParts.Skip(1).ToArray());
            return parsedCommand;
        }

        private static string[] FormatCommands(string[] commands)
        {
            string jointArgs = string.Join(" ", commands);
            return jointArgs
                .Split([" -"], StringSplitOptions.RemoveEmptyEntries)
                .Select(g => g.StartsWith("-") ? g : "-" + g.Trim()).ToArray();
        }

        public static async Task<List<Command>> ParseCommands(string[] commands)
        {
            commands = FormatCommands(commands);

            List<Command> parsedCommands = new List<Command>();
            foreach (string command in commands)
            {
                if (TryParseCommand(command, out var parsedCommand) && parsedCommand != null)
                {
                    parsedCommands.Add(parsedCommand);
                }
            }

            return parsedCommands;
        }
    }
}
