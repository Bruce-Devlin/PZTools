using PZTools.Core.Functions.Projects;
using PZTools.Core.Models;

var root = Path.Combine(Path.GetTempPath(), "PZTools-Playtest-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    ProjectEngine.ProjectsRootPath = Path.Combine(root, "projects");
    var project = ProjectEngine.CreateProject("PlaytestMod");
    var dependency = Path.Combine(root, "Dependency");
    Directory.CreateDirectory(dependency);
    File.WriteAllText(Path.Combine(dependency, "mod.info"), "name=Dependency\nid=Dependency");

    var workshopRoot = Path.Combine(root, "steamapps", "workshop", "content", "108600", "1234567890");
    var baseContainer = Path.Combine(workshopRoot, "Contents", "mods", "BaseLibrary");
    var baseBuild = Path.Combine(baseContainer, "42");
    var libraryContainer = Path.Combine(workshopRoot, "Contents", "mods", "FeatureLibrary");
    var libraryBuild = Path.Combine(libraryContainer, "42");
    Directory.CreateDirectory(baseBuild);
    Directory.CreateDirectory(libraryBuild);
    File.WriteAllText(Path.Combine(baseBuild, "mod.info"), "name=Base Library\nid=BaseLibrary");
    File.WriteAllText(Path.Combine(libraryBuild, "mod.info"), "name=Feature Library\nid=FeatureLibrary\nrequire=\\BaseLibrary\nloadModAfter=\\BaseLibrary");
    var discovered = ModDependencyService.Discover(new[] { Path.Combine(root, "steamapps", "workshop", "content", "108600") }, 42);
    var foundFeature = ModDependencyService.Resolve("FeatureLibrary", discovered);
    Check(foundFeature is not null && foundFeature.SourcePath == libraryContainer && foundFeature.WorkshopId == "1234567890",
        "Workshop wrapper discovery resolves the loadable mod container and item ID");
    Check(ModDependencyService.ValidateSource(new PlaytestDependency { ModId = "FeatureLibrary", SourcePath = libraryBuild }, 42).Count == 1,
        "version subfolder is rejected as a dependency copy root");

    var profile = PlaytestProfileStore.CreateDefault(project);
    profile.Name = "Two clients";
    profile.Mode = PlaytestMode.DedicatedServer;
    profile.ClientCount = 2;
    profile.ServerPort = 17261;
    profile.Dependencies.Add(new PlaytestDependency { ModId = "Dependency", SourcePath = dependency });

    project.ModInfo.Requires.Add("FeatureLibrary");
    ModDependencyService.SynchronizeRequiredDependencies(project, profile, discovered);
    Check(profile.Dependencies.Take(2).Select(x => x.ModId).SequenceEqual(new[] { "BaseLibrary", "FeatureLibrary" }) &&
          profile.Dependencies.Take(2).All(x => x.IsProjectRequired && x.WorkshopId == "1234567890"),
        "transitive project dependencies synchronize in prerequisite order with resolved Workshop metadata");
    PlaytestProfileStore.Save(project, new[] { profile });
    var loaded = PlaytestProfileStore.Load(project).Single();
    Check(loaded.ClientCount == 2 && loaded.Dependencies.Any(x => x.ModId == "Dependency") &&
          loaded.Dependencies.Take(2).Select(x => x.ModId).SequenceEqual(new[] { "BaseLibrary", "FeatureLibrary" }),
        "profile persistence keeps required and optional dependency order");
    Check(loaded.WindowMode == PlaytestWindowMode.Windowed && loaded.WindowWidth == 1280 && loaded.WindowHeight == 720, "safe windowed display defaults");

    var workspace = PlaytestWorkspaceService.Prepare(project, loaded);
    Check(workspace.ClientCachePaths.Count == 2, "multi-client cache creation");
    var sourcePreferences = Path.Combine(root, "preferences");
    Directory.CreateDirectory(sourcePreferences);
    File.WriteAllText(Path.Combine(sourcePreferences, "options.ini"), "version=8\nfullScreen=true\nborderless=true\nwidth=3840\nheight=2160\nframeRate=60\n");
    foreach (var cache in workspace.ClientCachePaths) PlaytestClientConfig.Configure(cache, loaded, sourcePreferences);
    var configuredOptions = File.ReadAllLines(Path.Combine(workspace.ClientCachePaths[0], "options.ini"));
    Check(configuredOptions.Contains("fullScreen=false") && configuredOptions.Contains("borderless=false") &&
          configuredOptions.Contains("width=1280") && configuredOptions.Contains("height=720") && configuredOptions.Contains("frameRate=60"),
        "windowed client configuration preserves unrelated preferences");
    loaded.WindowMode = PlaytestWindowMode.Fullscreen;
    PlaytestClientConfig.Configure(workspace.ClientCachePaths[0], loaded, sourcePreferences, forceWindowed: true);
    Check(File.ReadAllLines(Path.Combine(workspace.ClientCachePaths[0], "options.ini")).Contains("fullScreen=false") &&
          loaded.WindowMode == PlaytestWindowMode.Fullscreen, "docking forces windowed cache without changing saved profile");
    loaded.WindowMode = PlaytestWindowMode.Windowed;
    project.ModInfo.LoadModBefore.Add("Dependency");
    var ini = PlaytestServerConfig.Write(loaded, workspace, project.ModInfo.Id, project);
    var iniText = File.ReadAllText(ini);
    Check(iniText.Contains("DefaultPort=17261") &&
          iniText.IndexOf("BaseLibrary", StringComparison.Ordinal) < iniText.IndexOf("FeatureLibrary", StringComparison.Ordinal) &&
          iniText.IndexOf("FeatureLibrary", StringComparison.Ordinal) < iniText.IndexOf("PlaytestMod", StringComparison.Ordinal) &&
          iniText.IndexOf("PlaytestMod", StringComparison.Ordinal) < iniText.IndexOf("Dependency", StringComparison.Ordinal),
        "generated server config applies transitive and explicit load-order rules");
    project.ModInfo.LoadModAfter.Add("Dependency");
    Check(PlaytestProfileStore.Validate(loaded, project).Any(x => x.Contains("load-order rules contain a cycle", StringComparison.OrdinalIgnoreCase)),
        "contradictory dependency load-order rules block the profile before launch");
    project.ModInfo.LoadModAfter.Remove("Dependency");

    var server = Path.Combine(workspace.ServerCachePath, "mods", "PlaytestMod");
    var clients = workspace.ClientCachePaths.Select(x => Path.Combine(x, "mods", "PlaytestMod")).ToList();
    Directory.CreateDirectory(server);
    File.WriteAllText(Path.Combine(server, "shared.lua"), "return true");
    foreach (var client in clients) { Directory.CreateDirectory(client); File.Copy(Path.Combine(server, "shared.lua"), Path.Combine(client, "shared.lua")); }
    Check(ClientServerParityService.Compare(server, clients).Count == 0, "matching client/server parity");
    File.WriteAllText(Path.Combine(clients[1], "shared.lua"), "return false");
    Check(ClientServerParityService.Compare(server, clients).Any(x => x.Code == "PZP012"), "parity mismatch detection");

    var sourceSave = Path.Combine(root, "Zomboid", "Saves", "Sandbox", "Baseline");
    Directory.CreateDirectory(sourceSave);
    File.WriteAllText(Path.Combine(sourceSave, "map_ver.bin"), "baseline");
    loaded.SaveMode = PlaytestSaveMode.FreshClone;
    loaded.SourceSavePath = sourceSave;
    var cloned = PlaytestWorkspaceService.Prepare(project, loaded);
    Check(cloned.ClientCachePaths.All(x => File.Exists(Path.Combine(x, "Saves", "Sandbox", "Baseline", "map_ver.bin"))) == false &&
          File.Exists(Path.Combine(cloned.ServerCachePath, "Saves", "Sandbox", "Baseline", "map_ver.bin")), "dedicated-server save isolation");
    PlaytestWorkspaceService.Cleanup(cloned, false);
    PlaytestWorkspaceService.Cleanup(workspace, false);

    Console.WriteLine("PZTools Playtest smoke checks passed.");
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, true);
}

static void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("Playtest smoke check failed: " + name);
    Console.WriteLine("[PASS] " + name);
}
