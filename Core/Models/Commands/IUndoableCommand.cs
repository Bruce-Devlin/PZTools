namespace PZTools.Core.Models.Commands
{
    public interface IUndoableCommand
    {
        /// <summary>
        /// Human readable description (optional).
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Execute the action.
        /// </summary>
        Task ExecuteAsync();

        /// <summary>
        /// Undo the action.
        /// </summary>
        Task UndoAsync();
    }
}