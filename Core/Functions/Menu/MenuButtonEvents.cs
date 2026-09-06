using System.IO;
using System.Windows;
using System.Windows.Input;
using PZTools.Core.Functions.Decompile;
using PZTools.Core.Functions.Logger;
using PZTools.Core.Functions.Projects;
using PZTools.Core.Functions.Steam;
using PZTools.Core.Functions.Tester;
using PZTools.Core.Functions.Undo;
using PZTools.Core.Functions.Watermark;
using PZTools.Core.Functions.Zomboid;
using PZTools.Core.Models;
using PZTools.Core.Models.Commands;
using PZTools.Core.Windows.Dialogs;
using PZTools.Core.Windows.Dialogs.Project;

namespace PZTools.Core.Functions.Menu
{
    public static class MenuButtonEvents
    {
        public static class File
        {
            public static ICommand Project_Settings { get; } =
                    new RelayCommand(() =>
                    {
                        var project = ProjectEngine.CurrentProject;
                        if (project != null)
                            App.MainWindow.ShowDialog(new ProjectSettings(project.RootPath));
                    });

            public static ICommand Close_Project { get; } =
                    new RelayCommand(() => App.ReloadApp());

            public static object? Separator2 => null;

            public static ICommand Decompile_Game_Files { get; } =
                    new RelayCommand(static async () =>
                    {
                        var gamePath = ZomboidGame.GameDirectory;
                        if (gamePath != null)
                        {
                            try
                            {
                                JavaDecompilerHelpers.OnDecompilerMessage += (_, msg) => _ = Console.Log(msg);
                                if (ZomboidGame.GameMode == "Existing")
                                    await JavaDecompilerHelpers.DecompileGame(gamePath);
                                else
                                {
                                    foreach (var buildDir in Directory.GetDirectories(gamePath))
                                    {
                                        var buildName = Path.GetFileName(buildDir);
                                        if (!await JavaDecompilerHelpers.DecompileGame(buildDir, buildName))
                                            break;
                                    }
                                }
                            }
                            finally
                            {
                                JavaDecompilerHelpers.ClearDecompilerMessageEvents();
                            }
                        }
                        else
                            MessageBox.Show("Game path not set. Please set the game path in App Options first. (File > App Options)", "Error");
                    });

            public static ICommand Upload_To_Steam_Workshop { get; } =
                new RelayCommand(static () => _ = UploadToSteamWorkshopAsync());

            public static object? Separator3 => null;

            public static ICommand App_Options { get; } =
                    new RelayCommand(() => App.MainWindow.ShowDialog(new AppOptions()));
            public static ICommand Exit { get; } =
                    new RelayCommand(() => System.Windows.Application.Current.Shutdown());

            private static async Task UploadToSteamWorkshopAsync()
            {
                var project = ProjectEngine.CurrentProject;
                if (project is null)
                {
                    MessageBox.Show(
                        "Open a project before uploading it.",
                        "Steam Workshop",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var health = await Task.Run(
                    () => ProjectHealthService.AnalyzeAsync(project, validateLua: true));
                if (!health.IsReadyToDeploy)
                {
                    MessageBox.Show(
                        $"Workshop upload is blocked by {health.ErrorCount} project error(s). Open Project > Health Dashboard for details.",
                        "Steam Workshop",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    App.MainWindow.ShowDialog(new ProjectDashboard(project));
                    return;
                }

                try
                {
                    var review = new WorkshopUploadReview(WorkshopUploader.CreatePlan(project))
                    {
                        Owner = App.MainWindow
                    };
                    if (review.ShowDialog() != true)
                        return;

                    var login = new SteamLogin { Owner = App.MainWindow };
                    if (login.ShowDialog() != true)
                        return;

                    if (!await WorkshopUploader.UploadAsync(project, login.Username, review.ChangeNote))
                    {
                        MessageBox.Show(
                            "SteamCMD did not confirm a successful Workshop upload. Review the PZTools console and SteamCMD workshop logs before retrying.",
                            "Steam Workshop",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        return;
                    }

                    ShowWorkshopUploadCompleted(project);
                }
                catch (Exception ex)
                {
                    await Console.Log($"Workshop upload failed: {ex.Message}", Console.LogLevel.Error);
                    MessageBox.Show(
                        ex.Message,
                        "Steam Workshop upload failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }

            private static void ShowWorkshopUploadCompleted(ModProject project)
            {
                var settings = WorkshopSettingsStore.Load(project);
                var hasPublishedItem = !string.IsNullOrWhiteSpace(settings.PublishedFileId) &&
                    settings.PublishedFileId != "0";
                var message = hasPublishedItem
                    ? $"Workshop upload completed for item {settings.PublishedFileId}.\n\nOpen the item page to review it and accept Steam's legal agreement if prompted?"
                    : "Workshop upload completed.";

                var result = MessageBox.Show(
                    message,
                    "Steam Workshop",
                    hasPublishedItem ? MessageBoxButton.YesNo : MessageBoxButton.OK,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                {
                    WindowsHelpers.OpenFile(
                        $"https://steamcommunity.com/sharedfiles/filedetails/?id={settings.PublishedFileId}");
                }
            }
        }

        public static class Edit
        {
            public static ICommand Undo { get; } = UndoRedoManager.Instance.UndoCommand;

            public static ICommand Redo { get; } = UndoRedoManager.Instance.RedoCommand;

            public static object? Separator => null;
        }

        public static class Project
        {
            public static ICommand Find_In_Project { get; } =
                new RelayCommand(() => App.MainWindow?.ShowProjectSearch());


            public static ICommand New_PZ_Mod_File { get; } =
                new RelayCommand(() =>
                {
                    var project = ProjectEngine.CurrentProject;
                    if (project != null)
                        App.MainWindow.ShowDialog(new NewModFile(project));
                });

            public static object? Separator1 => null;

            public static ICommand Health_Dashboard { get; } =
                new RelayCommand(() =>
                {
                    var project = ProjectEngine.CurrentProject;
                    if (project != null)
                        App.MainWindow.ShowDialog(new ProjectDashboard(project));
                });

            public static ICommand Open_In_VS_Code { get; } =
                new RelayCommand(() =>
                {
                    var project = ProjectEngine.CurrentProject;
                    if (project == null)
                        return;
                    try
                    {
                        EditorIntegration.OpenInVsCode(project);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message, "Open in VS Code", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                });

            public static ICommand Set_Up_VS_Code_Workspace { get; } =
                new RelayCommand(() =>
                {
                    var project = ProjectEngine.CurrentProject;
                    if (project == null)
                        return;
                    try
                    {
                        var result = EditorIntegration.PrepareVsCodeWorkspace(project);
                        MessageBox.Show(
                            $"VS Code workspace ready. Created {result.CreatedFiles.Count}, updated {result.UpdatedFiles.Count}, and preserved {result.PreservedFiles.Count} file(s).",
                            "VS Code Setup", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message, "VS Code Setup", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                });

            public static object? Separator => null;

            public static ICommand Open_Project_Folder { get; } =
                new RelayCommand(() =>
                {
                    var project = ProjectEngine.CurrentProject;
                    if (project != null)
                        WindowsHelpers.ShowInExplorer(project.RootPath);
                });

            public static ICommand Open_Game_Lua_Source { get; } =
                new RelayCommand(() =>
                {
                    var luaPath = Path.Combine(ZomboidGame.GameDirectory, "media", "lua");
                    if (Directory.Exists(luaPath))
                        WindowsHelpers.ShowInExplorer(luaPath);
                    else
                        MessageBox.Show("The game Lua source folder was not found. Check the game path in App Options.",
                        "Game Lua Source", MessageBoxButton.OK, MessageBoxImage.Warning);
                });
        }

        public static class View
        {
            public static ICommand Game_Code_Knowledge_Base { get; } =
                    new RelayCommand(() =>
                    {
                        var sourceRoot = Path.Combine(AppPaths.CurrentDirectoryPath, "Zomboid", "Source");
                        if (GameKnowledgeBase.DiscoverBuilds(sourceRoot).Count > 0)
                            App.MainWindow.ShowDialog(new GameKnowledgeExplorer(sourceRoot));
                        else
                            MessageBox.Show(
                                "No decompiled game source was found. Use File > Decompile Game Files first; PZTools will build the searchable knowledge base automatically.",
                                "Game Code Knowledge Base", MessageBoxButton.OK, MessageBoxImage.Information);
                    });

            public static ICommand Decompiled_Game_Files { get; } =
                    new RelayCommand(() =>
                    {
                        var decompiledSource = Path.Combine(AppPaths.CurrentDirectoryPath, "Zomboid", "Source");
                        if (Directory.Exists(decompiledSource))
                        {
                            WindowsHelpers.OpenFile(decompiledSource);
                        }
                        else
                            MessageBox.Show("Decompiled game files not found. Please decompile the game files first. (File > Decompile Game Files)", "Error");
                    });

            public static ICommand Game_Log { get; } =
                    new RelayCommand(() =>
                    {
                        WindowsHelpers.OpenFile(Path.Combine(ZomboidGame.GameUserDirectory, "console.txt"));
                    });


            public static object? Separator => null;


            public static ICommand Save_Window_Layout { get; } =
                    new RelayCommand(() =>
                    {
                        var mainWindow = App.MainWindow;
                        mainWindow?.SaveLayout();
                    });
        }

        public static class Debug
        {
            public static ICommand Run_Game_Settings { get; } =
                new RelayCommand(() =>
                {
                    var runGameWindow = new RunProject(true);
                    runGameWindow.ShowDialog();
                });

            public static object? Separator => null;


            public static class Lua_Watermark
            {
                public static ICommand Apply_Watermark_To_This_File { get; } =
                    new RelayCommand(async () =>
                    {
                        var project = ProjectEngine.CurrentProject;
                        if (project is null || App.MainWindow is null)
                            return;
                        var watermark = Config.GetVariable(VariableType.user, $"{project.Name}-watermark");
                        if (string.IsNullOrEmpty(watermark))
                        {
                            MessageBox.Show("No Lua watermark set!?");
                            return;
                        }

                        await LuaWatermarker.WatermarkFile(App.MainWindow.OpenedFilePath, watermark);
                    });

                public static ICommand Apply_Watermark_To_All_Files { get; } =
                    new RelayCommand(async () =>
                    {
                        var project = ProjectEngine.CurrentProject;
                        if (project is null)
                            return;
                        var watermark = Config.GetVariable(VariableType.user, $"{project.Name}-watermark");
                        if (string.IsNullOrEmpty(watermark))
                        {
                            MessageBox.Show("No Lua watermark set!?");
                            return;
                        }
                        foreach (var file in Directory.GetFiles(ProjectEngine.CurrentProjectPath, "*.*", SearchOption.AllDirectories))
                        {
                            var ext = Path.GetExtension(file);
                            if (ext == ".lua")
                            {
                                await LuaWatermarker.WatermarkFile(file, watermark);
                            }
                        }
                    });
            }

            public static class Test
            {
                public static ICommand Test_This_Lua_File { get; } =
                    new RelayCommand(async () =>
                    {
                        if (App.MainWindow is null)
                            return;
                        var ext = Path.GetExtension(App.MainWindow.OpenedFilePath);
                        if (ext != ".lua")
                        {
                            MessageBox.Show("The opened file is not a Lua file.", "Error");
                            return;
                        }
                        var openFilePath = App.MainWindow.OpenedFilePath;

                        var results = await LuaTester.Test(System.IO.File.ReadAllText(openFilePath), openFilePath);
                    });

                public static ICommand Test_All_Lua_Files { get; } =
                    new RelayCommand(async () =>
                    {
                        foreach (var file in Directory.GetFiles(ProjectEngine.CurrentProjectPath, "*.*", SearchOption.AllDirectories))
                        {
                            var ext = Path.GetExtension(file);
                            if (ext == ".lua")
                            {
                                await LuaTester.TestFile(file);
                            }
                        }
                    });
            }
        }

        public static class Help
        {
            public static ICommand PZ_Wiki { get; } =
                new RelayCommand(() =>
                {
                    WindowsHelpers.OpenFile("https://pzwiki.net/wiki/Project_Zomboid_Wiki");
                });

            public static ICommand About_PZTools { get; } =
                    new RelayCommand(() => App.MainWindow.ShowDialog(new AboutApp()));
        }
    }
}
