using PZTools.Core.Functions.Projects;
using PZTools.Core.Functions.Tester;
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

            public static object Separator => null;

            public static ICommand App_Options { get; } =
                    new RelayCommand(() => App.MainWindow.ShowDialog(new AppOptions()));
            public static ICommand Exit { get; } =
                    new RelayCommand(() => System.Windows.Application.Current.Shutdown());
        }

        public static class Edit
        {
            public static ICommand Undo { get; } =
                    new RelayCommand(() => System.Windows.Application.Current.Shutdown());

            public static ICommand Redo { get; } =
                    new RelayCommand(() => System.Windows.Application.Current.Shutdown());

            public static object Separator => null;
        }

        public static class View
        {
            public static ICommand Game_Logs { get; } =
                    new RelayCommand(() =>
                    {
                        WindowsHelpers.OpenFile(Path.Combine(ZomboidGame.GameUserDirectory, "console.txt"));
                    });

            public static ICommand Search { get; } =
                    new RelayCommand(() =>
                    {
                        WindowsHelpers.OpenFile(Path.Combine(ZomboidGame.GameUserDirectory, "console.txt"));
                    });
        }

        public static class Debug
        {
            public static ICommand Run_Game { get; } =
                new RelayCommand(async () =>
                {
                    var runGameWindow = new RunProject();
                    runGameWindow.Show();
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
