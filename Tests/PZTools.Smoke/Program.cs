using PZTools.Core.Functions.Projects;
using PZTools.Core.Models;
using PZTools.Core.Functions.Agent;
using PZTools.Core.Functions.Steam;
using PZTools.Core.Functions.Decompile;

var testRoot = Path.Combine(Path.GetTempPath(), "PZTools-Smoke-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);

try
{
    ProjectEngine.ProjectsRootPath = testRoot;
    WorkshopUploadChecks.Run(testRoot);
    ProjectSearchChecks.Run(testRoot);
    ExpectThrows<ArgumentException>(() => ProjectEngine.CreateProject("../Escaped"), "project path traversal rejected");
    var project = ProjectEngine.CreateProject("ProductionReady", "41");

    Expect(Directory.Exists(Path.Combine(project.RootPath, "common")), "B42 common folder");
    Expect(File.Exists(Path.Combine(project.RootPath, "42", "mod.info")), "B42 metadata");
    Expect(File.Exists(Path.Combine(project.RootPath, "41", "mod.info")), "B41 compatibility metadata");
    Expect(project.Targets.Count == 2, "two build targets");
    Expect(project.Targets.Single(x => x.IsPrimary).Build == 42, "B42 primary target");
    ExpectThrows<ArgumentOutOfRangeException>(() => ProjectEngine.AddTarget(project, double.NaN), "invalid build target rejected");

    EditorIntegration.PrepareVsCodeWorkspace(project);
    var tasksPath = Path.Combine(project.RootPath, ".vscode", "tasks.json");
    var generatedTasks = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(tasksPath))["tasks"]!.OfType<Newtonsoft.Json.Linq.JObject>().ToList();
    Expect(generatedTasks.Any(x => x.Value<string>("label") == "PZTools: Test Mod"), "VS Code test task generated");
    Expect(generatedTasks.Any(x => x.Value<string>("label") == "PZTools: Deploy Mod"), "VS Code deploy task generated");
    var generatedTestTask = generatedTasks.Single(x => x.Value<string>("label") == "PZTools: Test Mod");
    Expect(generatedTestTask.Value<string>("command") == "dotnet" &&
           generatedTestTask["args"]!.Values<string>().Any(x => x?.EndsWith("PZTools.dll", StringComparison.OrdinalIgnoreCase) == true),
        "VS Code tasks use the console-visible PZTools DLL runner");
    generatedTasks.Add(new Newtonsoft.Json.Linq.JObject { ["label"] = "User Task", ["type"] = "shell", ["command"] = "echo preserved" });
    File.WriteAllText(tasksPath, new Newtonsoft.Json.Linq.JObject
    {
        ["version"] = "2.0.0",
        ["tasks"] = new Newtonsoft.Json.Linq.JArray(generatedTasks)
    }.ToString());
    EditorIntegration.PrepareVsCodeWorkspace(project);
    var mergedTasks = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(tasksPath))["tasks"]!.OfType<Newtonsoft.Json.Linq.JObject>().ToList();
    Expect(mergedTasks.Count(x => x.Value<string>("label") == "PZTools: Test Mod") == 1, "VS Code tasks are not duplicated");
    Expect(mergedTasks.Any(x => x.Value<string>("label") == "User Task"), "existing VS Code task preserved");

    var malformedImportSource = Path.Combine(testRoot, "_malformed-import-source");
    Directory.CreateDirectory(Path.Combine(malformedImportSource, "Lua", "Shared"));
    File.WriteAllText(Path.Combine(malformedImportSource, "Lua", "Shared", "Existing.lua"), "return true");
    File.WriteAllLines(Path.Combine(malformedImportSource, "mod.info.txt"), new[]
    {
        "name=Existing Legacy Mod",
        "versionMin=41.0"
    });
    var inspectedImport = ModImportService.Inspect(malformedImportSource);
    Expect(inspectedImport.SuggestedProjectName == "Existing Legacy Mod", "import inspection reads incorrectly named metadata");
    var importedLegacy = ModImportService.Import(malformedImportSource, "ImportedLegacy");
    var importedLegacyTarget = importedLegacy.Project.Targets.Single();
    Expect(importedLegacyTarget.Build == 41, "flat import infers target from versionMin");
    Expect(File.Exists(Path.Combine(importedLegacyTarget.Path, "mod.info")), "incorrect metadata filename repaired");
    Expect(File.Exists(Path.Combine(importedLegacyTarget.Path, "media", "lua", "Shared", "Existing.lua")), "misplaced Lua folder moved under media");
    Expect(importedLegacy.Project.ModInfo.Id == "ImportedLegacy", "missing imported mod ID generated");
    Expect(importedLegacy.WarningCount > 0 && importedLegacy.RepairCount > 0, "import reports repairs and unresolved review warnings");
    Expect(File.Exists(Path.Combine(malformedImportSource, "mod.info.txt")), "import leaves original source unchanged");
    Directory.Delete(importedLegacy.Project.RootPath, recursive: true);

    var workshopImportSource = Path.Combine(testRoot, "_workshop-import-source");
    var workshopMod = Path.Combine(workshopImportSource, "Contents", "mods", "WorkshopInner");
    Directory.CreateDirectory(Path.Combine(workshopMod, "media"));
    File.WriteAllLines(Path.Combine(workshopMod, "mod.info"), new[]
    {
        "name=Workshop Import",
        "id=WorkshopImport",
        "versionMin=42.0"
    });
    var importedWorkshop = ModImportService.Import(workshopImportSource, "ImportedWorkshop");
    Expect(importedWorkshop.Project.Targets.Single().Build == 42, "Workshop package unwrapped into Build 42 target");
    Expect(importedWorkshop.Issues.Any(x => x.Message.Contains("Workshop", StringComparison.OrdinalIgnoreCase)), "Workshop unwrapping reported");
    Directory.Delete(importedWorkshop.Project.RootPath, recursive: true);

    var primary = project.Targets.Single(x => x.IsPrimary);
    var luaPath = ModScaffolder.Create(primary, ModFileTemplate.SharedLua, "ProductionReady");
    var translationPath = ModScaffolder.Create(primary, ModFileTemplate.Translation, "UI");
    var itemPath = ModScaffolder.Create(primary, ModFileTemplate.ItemScript, "Items");
    var recipePath = ModScaffolder.Create(primary, ModFileTemplate.RecipeScript, "Recipes");
    var readmePath = ModScaffolder.Create(project, primary, ModFileTemplate.Readme, "README");
    Expect(luaPath.EndsWith(".lua", StringComparison.OrdinalIgnoreCase), "Lua scaffold");
    Expect(translationPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase), "B42 JSON translation scaffold");
    Expect(File.ReadAllText(itemPath).Contains("ItemType = base:normal"), "B42 ItemType scaffold");
    Expect(File.ReadAllText(recipePath).Contains("craftRecipe"), "B42 craftRecipe scaffold");
    Expect(readmePath == Path.Combine(project.RootPath, "README.md"), "project README scaffolded at project root");

    var professions = new List<ProfessionDefinition>
    {
        new() { Id = "FieldMedic", TranslationKey = "UI_prof_FieldMedic", DescriptionKey = "UI_profdesc_FieldMedic", Icon = "profession_fieldmedic", Cost = -4 }
    };
    professions[0].FreeTraits.Add("FirstAid");
    professions[0].XpBoosts.Add(new PerkBoostDefinition { Perk = "Doctor", Level = 3 });
    professions[0].FreeRecipes.Add("MakeBandage");
    var professionLua = ManagedContentService.BuildProfessions(professions);
    var parsedProfession = ManagedContentService.ParseProfessions(professionLua).Single();
    Expect(parsedProfession.Id == "FieldMedic" && parsedProfession.DescriptionKey == "UI_profdesc_FieldMedic" && parsedProfession.FreeTraits.Single() == "FirstAid" && parsedProfession.XpBoosts.Single().Level == 3 && parsedProfession.FreeRecipes.Single() == "MakeBandage",
        "profession manager round-trips registration, traits, recipes, and XP boosts");

    var traits = new List<TraitDefinition>
    {
        new() { Id = "SteadyHands", TranslationKey = "UI_trait_SteadyHands", DescriptionKey = "UI_trait_SteadyHandsDesc", Cost = 4 }
    };
    traits[0].XpBoosts.Add(new PerkBoostDefinition { Perk = "Aiming", Level = 1 });
    traits[0].FreeRecipes.Add("MakeTarget");
    var parsedTrait = ManagedContentService.ParseTraits(ManagedContentService.BuildTraits(traits)).Single();
    Expect(parsedTrait.Id == "SteadyHands" && parsedTrait.DescriptionKey == "UI_trait_SteadyHandsDesc" && parsedTrait.XpBoosts.Single().Perk == "Aiming" && parsedTrait.FreeRecipes.Single() == "MakeTarget",
        "trait manager round-trips registration, recipes, and XP boosts");
    Expect(ManagedContentService.ParseTraits("local oldTrait = TraitFactory.addTrait('OldStyle', getText('UI_trait_old'), 1, getText('UI_trait_olddesc'), false)").Single().Id == "OldStyle",
        "trait manager parses legacy five-argument declarations");

    const string build42Traits = """
        module GettingOld {
            character_trait_definition GettingOld:young
            {
                IsProfessionTrait = false,
                DisabledInMultiplayer = false,
                CharacterTrait = GettingOld:young,
                Cost = 10,
                UIName = Age: Young,
                UIDescription = A younger survivor,
                MutuallyExclusiveTraits = GettingOld:adult;GettingOld:elderly,
            }
        }
        """;
    var parsedBuild42Trait = ManagedContentService.ParseTraits(build42Traits).Single();
    Expect(parsedBuild42Trait.Id == "GettingOld:young" && parsedBuild42Trait.TranslationKey == "Age: Young" && parsedBuild42Trait.Cost == 10 && parsedBuild42Trait.ExtraProperties["MutuallyExclusiveTraits"].Contains("GettingOld:adult"),
        "trait manager parses Build 42 character trait definitions and retains additional properties");
    var rebuiltBuild42Trait = ManagedContentService.BuildTraits(new[] { parsedBuild42Trait }, true, ManagedContentService.FindScriptModuleName(build42Traits, "Fallback"));
    Expect(ManagedContentService.ParseTraits(rebuiltBuild42Trait).Single().ExtraProperties.ContainsKey("MutuallyExclusiveTraits") && rebuiltBuild42Trait.Contains("module GettingOld"),
        "trait manager round-trips Build 42 definitions and their module");

    const string build42Professions = """
        module PrisonerProfession
        {
            character_profession_definition prisonerprofession:prisoner
            {
                CharacterProfession = prisonerprofession:prisoner,
                Cost = 2,
                UIName = Prisoner,
                UIDescription = Start as a prisoner,
                IconPathName = profession_prisoner,
                XPBoosts = Sneak=1;Lightfoot=1;Strength=2,
            }
        }
        """;
    var parsedBuild42Profession = ManagedContentService.ParseProfessions(build42Professions).Single();
    Expect(parsedBuild42Profession.Id == "prisonerprofession:prisoner" && parsedBuild42Profession.Icon == "profession_prisoner" && parsedBuild42Profession.XpBoosts.Count == 3,
        "profession manager parses Build 42 character profession definitions");
    var declarationPath = Path.Combine(primary.Path, "media", "scripts", "ExistingProfessions.txt");
    Directory.CreateDirectory(Path.GetDirectoryName(declarationPath)!);
    File.WriteAllText(declarationPath, build42Professions);
    Expect(ManagedContentService.FindDeclarationFiles(primary, true).Contains(declarationPath, StringComparer.OrdinalIgnoreCase),
        "content manager discovers Build 42 profession definition files");

    var localized = new List<LocalizationEntry> { new() { Key = "UI_prof_FieldMedic", Value = "Field Medic" } };
    Expect(ManagedContentService.ParseLocalization(ManagedContentService.BuildLocalization(localized, true, "UI"), true).Single().Value == "Field Medic",
        "Build 42 localization manager round-trips JSON");
    Expect(ManagedContentService.ParseLocalization(ManagedContentService.BuildLocalization(localized, false, "UI"), false).Single().Key == "UI_prof_FieldMedic",
        "Build 41 localization manager round-trips translation tables");

    var sandboxOptions = new List<SandboxOptionDefinition>
    {
        new() { Id = "ProductionReady.SpawnChance", Type = "integer", Default = "10", Min = "0", Max = "100", Step = "1", Page = "ProductionReady", Translation = "SpawnChance", Tooltip = "SpawnChance_tooltip" }
    };
    sandboxOptions[0].ExtraProperties["customField"] = "kept";
    var parsedSandbox = ManagedContentService.ParseSandboxOptions(ManagedContentService.BuildSandboxOptions(sandboxOptions)).Single();
    Expect(parsedSandbox.Id == "ProductionReady.SpawnChance" && parsedSandbox.Max == "100" && parsedSandbox.Tooltip == "SpawnChance_tooltip" && parsedSandbox.ExtraProperties["customField"] == "kept",
        "sandbox option manager round-trips standard and additional properties");
    var inlineSandbox = ManagedContentService.ParseSandboxOptions("VERSION = 1,\noption Demo.Enabled = { type = boolean, default = true, page = Demo, translation = Demo_Enabled, }\n").Single();
    Expect(inlineSandbox.Type == "boolean" && inlineSandbox.Default == "true" && inlineSandbox.Translation == "Demo_Enabled",
        "sandbox manager parses optional equals and inline properties");

    var previewFile = Path.Combine(primary.Path, "poster.png");
    File.WriteAllBytes(previewFile, new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
    project.ModInfo.Posters.Add("poster.png");
    ModInfoParser.Save(primary.Path, project.ModInfo);
    var workshopSettings = new WorkshopSettings
    {
        Title = "Production Ready Workshop",
        Description = "A quoted \"description\"\nwith another line.",
        Tags = new List<string> { "Mod", "Build 42" },
        Visibility = WorkshopVisibility.Unlisted,
        PublishedFileId = "0",
        DefaultChangeNote = "Smoke test"
    };
    WorkshopSettingsStore.Save(project, workshopSettings);
    var loadedWorkshopSettings = WorkshopSettingsStore.Load(project);
    Expect(loadedWorkshopSettings.Tags.SequenceEqual(new[] { "Mod", "Build 42" }) && loadedWorkshopSettings.Visibility == WorkshopVisibility.Unlisted,
        "Workshop metadata persists with the project");
    Expect(WorkshopSettingsStore.Validate(new WorkshopSettings { PublishedFileId = "not-an-id" }).Any(x => x.Contains("numeric", StringComparison.OrdinalIgnoreCase)),
        "invalid Workshop Published File ID rejected");
    var originalWorkingDirectory = Directory.GetCurrentDirectory();
    string packageRoot;
    try
    {
        Directory.SetCurrentDirectory(testRoot);
        packageRoot = WorkshopUploader.PreparePackage(project, loadedWorkshopSettings, "Review upload", CancellationToken.None);
    }
    finally
    {
        Directory.SetCurrentDirectory(originalWorkingDirectory);
    }
    var workshopManifest = File.ReadAllText(Path.Combine(packageRoot, "workshop.txt"));
    var uploadVdf = File.ReadAllText(packageRoot + ".vdf");
    Expect(workshopManifest.Contains("tags=Mod;Build 42") && workshopManifest.Contains("visibility=unlisted"),
        "Workshop package includes project tags and visibility");
    Expect(uploadVdf.Contains("\\\"description\\\"") && uploadVdf.Contains("\\nwith another line") && uploadVdf.Contains("\"visibility\" \"3\""),
        "SteamCMD VDF safely escapes description text and maps visibility");
    Expect(!Directory.EnumerateFiles(Path.Combine(packageRoot, "Contents"), "workshop-settings.json", SearchOption.AllDirectories).Any(),
        "Workshop package excludes private PZTools project metadata");

    var report = await ProjectHealthService.AnalyzeAsync(project, validateLua: true);
    Expect(report.ErrorCount == 0, "health check without errors: " +
        string.Join(" | ", report.Diagnostics.Where(x => x.Severity == PZTools.Core.Models.DiagnosticSeverity.Error).Select(x => x.Message)));
    Expect(report.LuaFileCount == 1, "Lua file counted");

    var registryPath = Path.Combine(primary.Path, "media", "registries.lua");
    File.WriteAllText(registryPath, "TestRegistry = {}" + Environment.NewLine);
    var apostropheScript = Path.Combine(primary.Path, "media", "scripts", "Apostrophe.txt");
    File.WriteAllText(apostropheScript, "module Test {\nitem Thing\n{\nDisplayName = You're prepared,\n}\n}\n");
    var validSpecialFilesReport = await ProjectHealthService.AnalyzeAsync(project, validateLua: true);
    Expect(!validSpecialFilesReport.Diagnostics.Any(x => x.Code == "PZT022" && x.FilePath == registryPath),
        "Build 42 media/registries.lua accepted");
    Expect(!validSpecialFilesReport.Diagnostics.Any(x => x.Code == "PZS004" && x.FilePath == apostropheScript),
        "apostrophe in unquoted ZedScript value does not hide braces");

    var brokenScript = Path.Combine(primary.Path, "media", "scripts", "Broken.txt");
    var brokenTranslation = Path.Combine(primary.Path, "media", "lua", "shared", "Translate", "EN", "Broken.json");
    File.WriteAllText(brokenScript, "module Broken { item Thing {");
    File.WriteAllText(brokenTranslation, "{ not-valid-json }");
    var brokenReport = await ProjectHealthService.AnalyzeAsync(project, validateLua: true);
    Expect(brokenReport.Diagnostics.Any(x => x.Code == "PZS004"), "unbalanced ZedScript detected");
    Expect(brokenReport.Diagnostics.Any(x => x.Code == "PZT200"), "invalid B42 translation JSON detected");
    var markdown = ProjectHealthReportMarkdown.Format(brokenReport);
    Expect(markdown.Contains("PZS004") && markdown.Contains("Broken.txt"), "health report exports findings");
    Expect(!markdown.Contains(testRoot, StringComparison.OrdinalIgnoreCase), "health report omits absolute local paths");
    File.Delete(brokenScript);
    File.Delete(brokenTranslation);
    File.Delete(registryPath);
    File.Delete(apostropheScript);

    var installedRoot = Path.Combine(testRoot, "_installed");
    var dependencyRoot = Path.Combine(installedRoot, "SharedLibrary", "42");
    Directory.CreateDirectory(Path.Combine(dependencyRoot, "media", "scripts"));
    File.WriteAllLines(Path.Combine(dependencyRoot, "mod.info"), new[]
    {
        "name=Shared Library",
        "id=SharedLib",
        "require=\\ProductionReady"
    });
    File.Copy(itemPath, Path.Combine(dependencyRoot, "media", "scripts", "Items.txt"));
    project.ModInfo.Requires.Add("SharedLib");
    var conflicts = ProjectConflictAnalyzer.AnalyzeRequiredMods(project, new[] { installedRoot });
    Expect(conflicts.Any(x => x.Code == "PZC010"), "required-mod file collision predicted");
    Expect(conflicts.Any(x => x.Code == "PZC011"), "required-mod ZedScript collision predicted");
    Expect(conflicts.Any(x => x.Code == "PZC020"), "direct dependency cycle predicted");
    project.ModInfo.Requires.Remove("SharedLib");

    var manifestSource = Path.Combine(testRoot, "_manifest-source");
    var manifestDeploy = Path.Combine(testRoot, "_manifest-deploy");
    Directory.CreateDirectory(manifestSource);
    Directory.CreateDirectory(manifestDeploy);
    File.WriteAllText(Path.Combine(manifestSource, "test.lua"), "return true");
    File.Copy(Path.Combine(manifestSource, "test.lua"), Path.Combine(manifestDeploy, "test.lua"));
    DeploymentManifestService.Write(manifestDeploy, project.ModInfo.Id);
    Expect(DeploymentManifestService.Verify(manifestSource, manifestDeploy, project.ModInfo.Id).Count == 0,
        "deployment manifest verifies exact payload");
    File.WriteAllText(Path.Combine(manifestSource, "test.lua"), "return false");
    Expect(DeploymentManifestService.Verify(manifestSource, manifestDeploy, project.ModInfo.Id).Any(x => x.Code == "PZD012"),
        "stale deployed source predicted");
    File.WriteAllText(Path.Combine(manifestDeploy, "test.lua"), "modified runtime copy");
    Expect(DeploymentManifestService.Verify(manifestSource, manifestDeploy, project.ModInfo.Id).Any(x => x.Code == "PZD011"),
        "modified deployment predicted");

    var fakeMcpServer = Path.Combine(testRoot, "PZTools.Mcp.exe");
    File.WriteAllText(fakeMcpServer, string.Empty);
    var codexSettings = new AppSettings { AgentMcpAllowDeployment = false };
    var codexConfigPath = Path.Combine(project.RootPath, ".codex", "config.toml");
    Directory.CreateDirectory(Path.GetDirectoryName(codexConfigPath)!);
    File.WriteAllText(codexConfigPath, "model = \"test-model\"" + Environment.NewLine);
    AgentMcpConfiguration.ConfigureProject(project.RootPath, fakeMcpServer, codexSettings);
    var codexConfig = File.ReadAllText(codexConfigPath);
    Expect(codexConfig.Contains("model = \"test-model\"") && codexConfig.Contains("[mcp_servers.pztools]"),
        "Codex MCP setup preserves existing config");
    Expect(codexConfig.Contains("pztools_start_game") && !codexConfig.Contains("pztools_deploy_project"),
        "Codex MCP enabled tools follow capability settings");
    codexSettings.AgentMcpAllowDeployment = true;
    AgentMcpConfiguration.ConfigureProject(project.RootPath, fakeMcpServer, codexSettings);
    codexConfig = File.ReadAllText(codexConfigPath);
    Expect(codexConfig.Contains("pztools_deploy_project") &&
           codexConfig.Split(AgentMcpConfiguration.ManagedBlockStart).Length == 2,
        "Codex MCP setup updates one managed block");
    Expect(AgentMcpConfiguration.GetPipeName(project.RootPath) == AgentMcpConfiguration.GetPipeName(project.RootPath + Path.DirectorySeparatorChar),
        "Agent MCP pipe identity is stable");

    var sessionLog = Path.Combine(testRoot, "console-session.txt");
    File.WriteAllText(sessionLog, "ERROR: old ProductionReady failure" + Environment.NewLine);
    var logSession = GameLogAnalyzer.BeginSession(sessionLog);
    File.AppendAllText(sessionLog, "LOG: [ProductionReady] warning stage reached" + Environment.NewLine);
    File.AppendAllText(sessionLog, "WARN: new ProductionReady playtest failure" + Environment.NewLine);
    File.AppendAllText(sessionLog, "WARN: new ProductionReady playtest failure" + Environment.NewLine);
    File.AppendAllText(sessionLog, "ERROR: Mod > tiledef=dependency_tiles 14001 file number must be from 100 to 8189" + Environment.NewLine);
    var sessionFindings = GameLogAnalyzer.FindProjectIssues(project, logSession);
    Expect(sessionFindings.Count == 2 && sessionFindings.All(x => x.Target == "Current playtest") &&
           sessionFindings.Any(x => x.Message.Contains("new ProductionReady") && x.Message.Contains("repeated 2 times")) &&
           sessionFindings.Any(x => x.Message.Contains("tiledef=dependency_tiles")),
        "playtest log analysis excludes stale sessions, collapses duplicates, and reports mod-loader errors");

    var profile = PlaytestProfileStore.CreateDefault(project);
    profile.Name = "Two-client integration";
    profile.Mode = PlaytestMode.DedicatedServer;
    profile.ClientCount = 2;
    profile.ServerPort = 18261;
    var firstDependencyRoot = Path.Combine(installedRoot, "FirstLibrary");
    var secondDependencyRoot = Path.Combine(installedRoot, "SecondLibrary");
    Directory.CreateDirectory(firstDependencyRoot);
    Directory.CreateDirectory(secondDependencyRoot);
    File.WriteAllText(Path.Combine(firstDependencyRoot, "mod.info"), "name=First Library\nid=FirstLibrary");
    File.WriteAllText(Path.Combine(secondDependencyRoot, "mod.info"), "name=Second Library\nid=SecondLibrary");
    profile.Dependencies = new List<PlaytestDependency>
    {
        new() { ModId = "FirstLibrary", SourcePath = firstDependencyRoot },
        new() { ModId = "SecondLibrary", SourcePath = secondDependencyRoot },
        new() { ModId = "SharedLib", SourcePath = Path.GetDirectoryName(dependencyRoot)! }
    };
    PlaytestProfileStore.Save(project, new[] { profile });
    var loadedProfile = PlaytestProfileStore.Load(project).Single();
    Expect(loadedProfile.ClientCount == 2 && loadedProfile.Dependencies[0].ModId == "FirstLibrary",
        "playtest profile persists client count and dependency order");
    var workspace = PlaytestWorkspaceService.Prepare(project, loadedProfile);
    Expect(workspace.ClientCachePaths.Count == 2 && workspace.ClientCachePaths.All(Directory.Exists),
        "multi-client isolated caches prepared");
    var serverIni = PlaytestServerConfig.Write(loadedProfile, workspace, project.ModInfo.Id);
    var serverText = File.ReadAllText(serverIni);
    Expect(serverText.IndexOf("FirstLibrary", StringComparison.Ordinal) < serverText.IndexOf("SecondLibrary", StringComparison.Ordinal),
        "server configuration preserves dependency load order");
    var serverParityRoot = Path.Combine(workspace.ServerCachePath, "mods", "ParityMod");
    var clientParityRoots = workspace.ClientCachePaths.Select(x => Path.Combine(x, "mods", "ParityMod")).ToList();
    Directory.CreateDirectory(serverParityRoot);
    File.WriteAllText(Path.Combine(serverParityRoot, "shared.lua"), "return true");
    foreach (var root in clientParityRoots) { Directory.CreateDirectory(root); File.Copy(Path.Combine(serverParityRoot, "shared.lua"), Path.Combine(root, "shared.lua")); }
    Expect(ClientServerParityService.Compare(serverParityRoot, clientParityRoots).Count == 0, "matching client/server payloads pass parity");
    File.WriteAllText(Path.Combine(clientParityRoots[1], "shared.lua"), "return false");
    Expect(ClientServerParityService.Compare(serverParityRoot, clientParityRoots).Any(x => x.Code == "PZP012"), "client/server hash mismatch detected");
    PlaytestWorkspaceService.Cleanup(workspace, keepSessionData: false);

    var legacyTarget = project.Targets.Single(x => x.Build == 41);
    var syncedFolder = Path.Combine(primary.Path, "media", "lua", "shared", "CrossVersion");
    var syncedSourceFile = Path.Combine(syncedFolder, "SharedRules.lua");
    Directory.CreateDirectory(syncedFolder);
    File.WriteAllText(syncedSourceFile, "return 'v1'");
    var enabledSync = VersionSyncService.Enable(project, syncedFolder);
    var legacySyncedFile = Path.Combine(legacyTarget.Path, "media", "lua", "shared", "CrossVersion", "SharedRules.lua");
    Expect(enabledSync.Copied == 1 && File.ReadAllText(legacySyncedFile) == "return 'v1'",
        "folder version sync seeds missing counterparts");
    File.WriteAllText(syncedSourceFile, "return 'v2'");
    var updatedSync = VersionSyncService.ReconcileChange(project, syncedSourceFile);
    Expect(updatedSync.Updated == 1 && File.ReadAllText(legacySyncedFile) == "return 'v2'",
        "version sync propagates a one-sided edit");
    File.WriteAllText(legacySyncedFile, "return 'legacy-only'");
    File.WriteAllText(syncedSourceFile, "return 'future-only'");
    var conflictingSync = VersionSyncService.ReconcileChange(project, syncedSourceFile);
    Expect(conflictingSync.Conflicts > 0 && File.ReadAllText(legacySyncedFile) == "return 'legacy-only'" && File.ReadAllText(syncedSourceFile) == "return 'future-only'",
        "version sync preserves independently diverged files");
    File.WriteAllText(legacySyncedFile, "return 'future-only'");
    VersionSyncService.ReconcileChange(project, legacySyncedFile);
    var futureFile = Path.Combine(legacyTarget.Path, "media", "lua", "shared", "CrossVersion", "AddedLater.lua");
    File.WriteAllText(futureFile, "return true");
    VersionSyncService.ReconcileChange(project, futureFile);
    Expect(File.Exists(Path.Combine(syncedFolder, "AddedLater.lua")),
        "synced folders include files created later in either version");
    var laterEmptyFolder = Path.Combine(legacyTarget.Path, "media", "lua", "shared", "CrossVersion", "EmptyButRequired");
    Directory.CreateDirectory(laterEmptyFolder);
    VersionSyncService.ReconcileChange(project, laterEmptyFolder);
    Expect(Directory.Exists(Path.Combine(syncedFolder, "EmptyButRequired")),
        "synced folders preserve empty subfolders added later");

    var copiedTarget = ProjectEngine.AddTarget(project, 43, primary);
    Expect(File.ReadAllText(Path.Combine(copiedTarget.Path, "media", "lua", "shared", "CrossVersion", "SharedRules.lua")) == "return 'future-only'",
        "new target can copy the complete previous version package");
    var futureTarget = ProjectEngine.AddTarget(project, 44);
    var futureTargetSync = VersionSyncService.ReconcileAll(project);
    Expect(File.Exists(Path.Combine(futureTarget.Path, "media", "lua", "shared", "CrossVersion", "SharedRules.lua")) && futureTargetSync.Copied > 0,
        "saved sync rules populate future game-version targets");

    var reloaded = ProjectEngine.LoadProjects().Single();
    Expect(reloaded.Targets.Any(x => x.Build == 42), "B42 target rediscovered");
    Expect(reloaded.Targets.Any(x => x.Build == 41), "B41 target rediscovered");
    Expect(reloaded.Targets.Any(x => x.Build == 43) && reloaded.Targets.Any(x => x.Build == 44), "copied and future synced targets rediscovered");

    var decompiledRoot = Path.Combine(testRoot, "decompiled", "42.12.3");
    var zombieSource = Path.Combine(decompiledRoot, "zombie", "characters");
    Directory.CreateDirectory(zombieSource);
    File.WriteAllText(Path.Combine(zombieSource, "IsoPlayer.java"), """
        package zombie.characters;

        /** Represents the locally controlled survivor. */
        public class IsoPlayer {
            public static final int MAX_PLAYERS = 4;

            /** Returns the player's current panic level. */
            public float getPanicLevel() {
                return 0.0f;
            }

            protected void updateMovement(float delta) {
            }
        }
        """);
    var discoveredBuilds = GameKnowledgeBase.DiscoverBuilds(Path.Combine(testRoot, "decompiled"));
    Expect(discoveredBuilds.Single().Name == "42.12.3", "decompiled game build discovered for knowledge base");
    var knowledge = await GameKnowledgeBase.LoadOrBuildAsync(decompiledRoot, "42.12.3", force: true);
    Expect(knowledge.TypeCount == 1 && knowledge.MethodCount == 2 && knowledge.FieldCount == 1,
        "game knowledge base indexes Java types, methods, and fields");
    var panicMethod = GameKnowledgeBase.Search(knowledge, "panic", GameSymbolKind.Method).Single();
    Expect(panicMethod.QualifiedName == "zombie.characters.IsoPlayer.getPanicLevel" && panicMethod.Line == 8 &&
           panicMethod.Documentation.Contains("panic level"),
        "game knowledge search returns ranked source-linked declarations and recovered documentation");
    var cachedKnowledge = await GameKnowledgeBase.LoadOrBuildAsync(decompiledRoot, "42.12.3");
    Expect(cachedKnowledge.SourceFingerprint == knowledge.SourceFingerprint && File.Exists(GameKnowledgeBase.GetIndexPath(decompiledRoot)),
        "unchanged game knowledge index is persisted and reusable");
    Expect(!File.ReadAllText(GameKnowledgeBase.GetIndexPath(decompiledRoot)).Contains("SearchText", StringComparison.Ordinal),
        "game knowledge cache omits derived search data");

    Console.WriteLine("PZTools smoke checks passed.");
}
finally
{
    if (Directory.Exists(testRoot) &&
        Path.GetFullPath(testRoot).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
        Directory.Delete(testRoot, recursive: true);
}

static void Expect(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("Smoke check failed: " + name);
    Console.WriteLine("[PASS] " + name);
}

static void ExpectThrows<TException>(Action action, string name) where TException : Exception
{
    try
    {
        action();
        throw new InvalidOperationException("Smoke check failed: " + name);
    }
    catch (TException)
    {
        Console.WriteLine("[PASS] " + name);
    }
}
