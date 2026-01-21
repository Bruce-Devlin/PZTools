using PZTools.Core.Functions.Logger;
using PZTools.Core.Models.Commands;
using System.Windows.Input;

namespace PZTools.Core.Functions.Undo
{
    public enum UndoRedoAction
    {
        Executed,
        Undone,
        Redone
    }

    public class UndoRedoEventArgs : EventArgs
    {
        public IUndoableCommand Command { get; }
        public UndoRedoAction Action { get; }

        public UndoRedoEventArgs(IUndoableCommand command, UndoRedoAction action)
        {
            Command = command;
            Action = action;
        }
    }

    public class UndoRedoManager
    {
        public static UndoRedoManager Instance { get; } = new UndoRedoManager();

        private readonly Stack<IUndoableCommand> _undoStack = new();
        private readonly Stack<IUndoableCommand> _redoStack = new();

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

        public ICommand UndoCommand { get; }
        public ICommand RedoCommand { get; }

        /// <summary>
        /// Raised after a command is executed, undone or redone.
        /// </summary>
        public event EventHandler<UndoRedoEventArgs>? CommandExecuted;

        private UndoRedoManager()
        {
            // Wrap async calls safely for RelayCommand (which expects Action)
            UndoCommand = new RelayCommand(() => _ = UndoAsync(), () => CanUndo);
            RedoCommand = new RelayCommand(() => _ = RedoAsync(), () => CanRedo);
        }

        public async Task ExecuteAsync(IUndoableCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));

            await command.ExecuteAsync().ConfigureAwait(false);

            _undoStack.Push(command);
            _redoStack.Clear();

            // Notify WPF that command availability may have changed
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();

            // Notify subscribers (UI) that an action completed
            CommandExecuted?.Invoke(this, new UndoRedoEventArgs(command, UndoRedoAction.Executed));
        }

        public async Task UndoAsync()
        {
            if (!CanUndo) return;

            await this.Log("Undoing last action...");

            var cmd = _undoStack.Pop();
            await cmd.UndoAsync().ConfigureAwait(false);
            _redoStack.Push(cmd);

            System.Windows.Input.CommandManager.InvalidateRequerySuggested();

            CommandExecuted?.Invoke(this, new UndoRedoEventArgs(cmd, UndoRedoAction.Undone));
        }

        public async Task RedoAsync()
        {
            if (!CanRedo) return;

            await this.Log("Redoing last action...");

            var cmd = _redoStack.Pop();
            await cmd.ExecuteAsync().ConfigureAwait(false);
            _undoStack.Push(cmd);

            System.Windows.Input.CommandManager.InvalidateRequerySuggested();

            CommandExecuted?.Invoke(this, new UndoRedoEventArgs(cmd, UndoRedoAction.Redone));
        }

        public void Clear()
        {
            _undoStack.Clear();
            _redoStack.Clear();
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }
}