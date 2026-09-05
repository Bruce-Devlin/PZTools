using System.Globalization;
using System.IO;
using PZTools.Core.Functions.Projects;
using PZTools.Core.Models;
using PZTools.Core.Models.Commands;

namespace PZTools.Core.Functions.Undo
{
    public class FileCreateCommand : IUndoableCommand
    {
        public string Path { get; }
        public string Content { get; }
        public string Description { get; }

        public FileCreateCommand(string path, string content = "")
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));
            Content = content ?? string.Empty;
            Description = $"Create file {System.IO.Path.GetFileName(Path)}";
        }

        public Task ExecuteAsync()
        {
            var dir = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(Path, Content);
            return Task.CompletedTask;
        }

        public Task UndoAsync()
        {
            if (File.Exists(Path))
                File.Delete(Path);
            return Task.CompletedTask;
        }
    }

    public class FileDeleteCommand : IUndoableCommand
    {
        public string Path { get; }
        private readonly string? _backupTemp;
        private readonly bool _wasDirectory;
        public string Description { get; }

        public FileDeleteCommand(string path)
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));
            Description = $"Delete file {System.IO.Path.GetFileName(Path)}";
            if (Directory.Exists(Path))
            {
                _wasDirectory = true;
                _backupTemp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PZTools_DeleteBackup", Guid.NewGuid().ToString("N"));
                WindowsHelpers.CopyDirectory(Path, _backupTemp);
            }
            else if (File.Exists(Path))
            {
                _backupTemp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PZTools_DeleteBackup", Guid.NewGuid().ToString("N"), System.IO.Path.GetFileName(Path));
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_backupTemp)!);
                File.Copy(Path, _backupTemp, overwrite: true);
            }
        }

        public Task ExecuteAsync()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, true);
            if (File.Exists(Path))
                File.Delete(Path);
            return Task.CompletedTask;
        }

        public Task UndoAsync()
        {
            if (_backupTemp != null && File.Exists(_backupTemp))
            {
                var dir = System.IO.Path.GetDirectoryName(Path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.Copy(_backupTemp, Path, true);
            }
            else if (_wasDirectory && _backupTemp != null && Directory.Exists(_backupTemp))
            {
                if (Directory.Exists(Path))
                    WindowsHelpers.DeleteDirectoryRobust(Path);
                WindowsHelpers.CopyDirectory(_backupTemp, Path);
            }
            return Task.CompletedTask;
        }
    }

    public sealed class TargetDeleteCommand : IUndoableCommand
    {
        public double Target { get; }
        private string TargetFolderName => Target.ToString("0.################", CultureInfo.InvariantCulture);
        private readonly ModProject _project;
        private string targetPath => Path.Combine(_project.RootPath, TargetFolderName);

        private readonly string? _backupDir;
        public string Description { get; }

        public TargetDeleteCommand(double target)
        {
            Target = target;
            _project = ProjectEngine.CurrentProject ?? throw new InvalidOperationException("No project is loaded.");
            Description = $"Delete build target: {TargetFolderName}";

            if (Directory.Exists(targetPath))
            {
                _backupDir = Path.Combine(Path.GetTempPath(), "PZTools_TargetBackup", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_backupDir);
                WindowsHelpers.CopyDirectory(targetPath, _backupDir);
            }
        }

        public Task ExecuteAsync()
        {
            _project.Targets.RemoveAll(t => t.Build == Target);

            if (Directory.Exists(targetPath))
                WindowsHelpers.DeleteDirectoryRobust(targetPath);

            App.MainWindow?.UpdateTreeView();

            return Task.CompletedTask;
        }

        public Task UndoAsync()
        {
            if (!string.IsNullOrEmpty(_backupDir) && Directory.Exists(_backupDir))
            {
                if (Directory.Exists(targetPath))
                    WindowsHelpers.DeleteDirectoryRobust(targetPath);

                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                WindowsHelpers.CopyDirectory(_backupDir, targetPath);

                var target = new ModTarget { Build = Target, Path = targetPath };
                _project.Targets.Add(target);
                target.LoadFiles();

                App.MainWindow?.UpdateTreeView();
            }

            return Task.CompletedTask;
        }
    }

    public class FileMoveCommand : IUndoableCommand
    {
        public string Source { get; }
        public string Destination { get; }
        public string Description { get; }

        public FileMoveCommand(string source, string destination)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Destination = destination ?? throw new ArgumentNullException(nameof(destination));
            Description = $"Move {System.IO.Path.GetFileName(Source)} -> {destination}";
        }

        public Task ExecuteAsync()
        {
            if (Directory.Exists(Source))
            {
                Directory.Move(Source, Destination);
            }
            else if (File.Exists(Source))
            {
                var destDir = System.IO.Path.GetDirectoryName(Destination);
                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                    Directory.CreateDirectory(destDir);
                File.Move(Source, Destination);
            }
            return Task.CompletedTask;
        }

        public Task UndoAsync()
        {
            if (Directory.Exists(Destination))
            {
                Directory.Move(Destination, Source);
            }
            else if (File.Exists(Destination))
            {
                var srcDir = System.IO.Path.GetDirectoryName(Source);
                if (!string.IsNullOrEmpty(srcDir) && !Directory.Exists(srcDir))
                    Directory.CreateDirectory(srcDir);
                File.Move(Destination, Source);
            }
            return Task.CompletedTask;
        }
    }
}
