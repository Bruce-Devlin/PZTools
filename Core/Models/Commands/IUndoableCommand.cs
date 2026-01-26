namespace PZTools.Core.Models.Commands
{
    public interface IUndoableCommand
    {
        string Description { get; }

        Task ExecuteAsync();

        Task UndoAsync();
    }
}