namespace PZTools.Core.Models.Commands
{
    internal class Command
    {
        public Command(string title, string[] parameters)
        {
            Title = title;
            Parameters = parameters;
        }

        public string Title { get; set; }
        public string[] Parameters { get; set; }
        public Func<string[], Task>? Method { get; set; }
    }
}
