namespace PZTools.Core.Models.Commands
{
    internal class CommandTemplate
    {
        public required string Title { get; set; }
        public required Func<string[], Task> Method { get; set; }
        public required bool AllowedCLI { get; set; }
        public required string Description { get; set; }
    }
}
