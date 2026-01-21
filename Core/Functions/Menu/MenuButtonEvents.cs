using PZTools.Core.Functions.Decompile;
using PZTools.Core.Functions.Logger;
using PZTools.Core.Functions.Projects;
using PZTools.Core.Functions.Tester;
using PZTools.Core.Functions.Undo;
using PZTools.Core.Functions.Zomboid;
using PZTools.Core.Models.Commands;
using PZTools.Core.Windows.Dialogs;
using PZTools.Core.Windows.Dialogs.Project;
using System.IO;
using System.Windows.Input;

namespace PZTools.Core.Functions.Menu
{
    public static class MenuButtonEvents
    {
        public static class File
        {
            public static ICommand Project_Settings { get; } =
                    new RelayCommand(() => App.MainWindow.ShowDialog(new ProjectSettings(ProjectEngine.CurrentProject.RootPath)));

            public static ICommand Close_Project { get; } =
                    new RelayCommand(() => App.ReloadApp());

            public static object Separator2 => null;

            public static ICommand Decompile_Game_Files { get; } =
                    new RelayCommand(static async () =>
                    {
                        var gamePath = ZomboidGame.GameDirectory;
                        if (gamePath != null)
                        {
                            Task.Run(async () =>
                            {
                                JavaDecompilerHelpers.UpdateSetupStatus += async (s, msg) => { await s.Log(msg.ToString()); };
                                if (ZomboidGame.GameMode == "existing")
                                    await JavaDecompilerHelpers.DecompileGame(gamePath);
                                else
                                {
                                    foreach (var buildDir in Directory.GetDirectories(gamePath))
                                    {
                                        var buildName = Path.GetFileName(buildDir);
                                        await JavaDecompilerHelpers.DecompileGame(gamePath, buildName);
                                    }
                                }
                                JavaDecompilerHelpers.ClearUpdateStatusEvents();
                            });
                        }
                        else MessageBox.Show("Game path not set. Please set the game path in App Options first. (File > App Options)", "Error");
                    });

            public static object Separator3 => null;

            public static ICommand App_Options { get; } =
                    new RelayCommand(() => App.MainWindow.ShowDialog(new AppOptions()));
            public static ICommand Exit { get; } =
                    new RelayCommand(() => System.Windows.Application.Current.Shutdown());
        }

        public static class Edit
        {
            // Use the UndoRedoManager commands so menu state and availability are centralized
            public static ICommand Undo { get; } = UndoRedoManager.Instance.UndoCommand;

            public static ICommand Redo { get; } = UndoRedoManager.Instance.RedoCommand;

            public static object Separator => null;
        }

        public static class View
        {
            public static ICommand Decompiled_Game_Files { get; } =
                    new RelayCommand(() =>
                    {
                        if (Directory.Exists(Path.Combine(AppPaths.CurrentDirectoryPath, "Zomboid", "Source")))
                        {

                        }
                        else MessageBox.Show("Decompiled game files not found. Please decompile the game files first. (File > Decompile Game Files)", "Error");
                    });

            public static ICommand Game_Logs { get; } =
                    new RelayCommand(() =>
                    {
                        WindowsHelpers.OpenFile(Path.Combine(ZomboidGame.GameUserDirectory, "console.txt"));
                    });

            public static ICommand Save_Window_Layout { get; } =
                    new RelayCommand(() =>
                    {
                        var mainWindow = App.MainWindow;
                        mainWindow.SaveLayout();
                    });
        }

        public static class Debug
        {
            public static ICommand Run_Game { get; } =
                new RelayCommand(async () =>
                {
                    var runGameWindow = new RunProject();
                    runGameWindow.ShowDialog();
                });

            public static object Separator => null;


            public static class Lua_Watermark
            {
                public static ICommand Apply_Watermark_To_This_File { get; } =
                    new RelayCommand(() => System.Windows.Application.Current.Shutdown());

                public static ICommand Apply_Watermark_To_All_Files { get; } =
                    new RelayCommand(() => System.Windows.Application.Current.Shutdown());
            }

            public static class Test
            {
                public static ICommand Test_This_Lua_File { get; } =
                    new RelayCommand(async () =>
                    {
                        var ext = Path.GetExtension(App.MainWindow.OpenedFilePath);
                        if (ext != ".lua")
                        {
                            MessageBox.Show("The opened file is not a Lua file.", "Error");
                            return;
                        }
                        var results = await LuaTester.Test(System.IO.File.ReadAllText(App.MainWindow.OpenedFilePath));
                    });

                public static ICommand Test_All_Lua_Files { get; } =
                    new RelayCommand(async () =>
                    {
                        foreach (var file in Directory.GetFiles(ProjectEngine.CurrentProjectPath, "*.*", SearchOption.AllDirectories))
                        {
                            var ext = Path.GetExtension(file);
                            if (ext == ".lua")
                            {
                                await LuaTester.Test(System.IO.File.ReadAllText(file));
                            }
                        }
                    });
            }
        }

        public static class Help
        {
            public static ICommand About { get; } =
                    new RelayCommand(() => App.MainWindow.ShowDialog(new AboutApp()));
        }
    }
}