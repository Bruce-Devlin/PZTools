<p align="center">
  <img src="Core/Assets/PZTools.png" alt="PZTools mascot at a computer" width="220">
</p>

<h1 align="center">PZTools</h1>

<p align="center">
  A Windows workspace for building, validating, deploying, and playtesting Project Zomboid mods.
</p>

<p align="center">
  <img alt="Platform: Windows" src="https://img.shields.io/badge/platform-Windows-0078D4?logo=windows">
  <img alt="Framework: .NET 10" src="https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet">
  <img alt="UI: WPF" src="https://img.shields.io/badge/UI-WPF-512BD4">
  <img alt="Status: active development" src="https://img.shields.io/badge/status-active_development-F59E0B">
</p>

PZTools brings the repetitive parts of Project Zomboid mod development into one desktop application. Create or import a mod, work across Build 42 and legacy Build 41 targets, catch common mistakes before launch, deploy a known-good copy, and run repeatable local playtests without continually rearranging your normal game data.

I built PZTools after starting several Project Zomboid mods and wanting a better way to manage game builds, inspect game code, and test changes without constantly switching Steam branches or manually decompiling JAR files.

> [!IMPORTANT]
> PZTools is under active development. Back up important mods and saves before testing, and treat game- and Steam-facing workflows as requiring real-world verification.

## Key Features

- Manage multiple Project Zomboid builds (Steam-managed or custom)
- Launch specific game versions with custom JVM or debug arguments
- Automatic Java decompilation using the CFR decompiler
- An automatically generated game-code knowledge base for searching decompiled Java types, methods, constructors, fields, signatures, and source locations across installed builds
- Integrated Lua syntax testing
- Centralised project and configuration management
- Extensible WPF architecture with undoable command support
- Build 42-native project layouts with common and version targets, plus legacy Build 41 discovery
- Optional full-package copying when adding a game-version target, plus non-destructive file and folder sync between current and future targets
- Project Health checks for metadata, assets, Lua, ZedScript, and Build 42 translation JSON
- Runtime issue correlation against the Project Zomboid console log
- Hash-based deployment checks that identify missing, modified, and stale playtest files
- Reusable, isolated single-player and dedicated-server playtest profiles
- Installed-mod dependency discovery, transitive requirement synchronization, and deterministic load-order generation
- PZ-aware file scaffolds for Lua, items, recipes, translations, and project documentation
- Visual managers for professions, traits, sandbox options, and localization content
- VS Code, VS Code Insiders, and VSCodium workspace integration
- Configurable Agent MCP integration for Codex, including editor control, project inspection, validation, guarded deployment, game debug sessions, and runtime logs
- Steam Workshop packaging and upload through SteamCMD

---

## Recommended Modding Workflow

PZTools manages Project Zomboid-specific work while your preferred editor handles day-to-day editing:

1. Create, import, or select a project. Importing copies the source into the managed workspace without editing the original mod in place. New projects use a Build 42 layout and can include a Build 41 compatibility target.
2. Choose **Project > New PZ Mod File** to create code or definitions in the correct load folder.
3. Choose **Project > Set Up VS Code Workspace**. PZTools creates only missing workspace files and preserves existing editor configuration.
4. Work in VS Code, VS Code Insiders, VSCodium, or the editor configured in App Options.
5. Open **Project > Health Dashboard** to validate every build target.
6. Deploy locally and run the selected profile. Use **Debug > Run Game Settings** to configure isolated saves, dependencies, local clients, display options, or a dedicated server.
7. Complete Workshop metadata in Project Settings, then upload through SteamCMD.

### Finding and previewing files

The project picker filters workspaces by name or location. Double-click a project to open it. In the workspace, **Ctrl+Shift+F** searches content across all build targets and shared files; enable **File names only** to find a path or asset. Results open in the preview at the matching line. Content search is literal, ignores case unless requested, and reports skipped files and capped results. Internal/generated folders are excluded; text files above 2 MB and linked paths are skipped.

Select a file to see its relative path and details. Text previews are read-only and follow external saves while preserving your reading position. **Ctrl+F** opens find in the current file; **F3** and **Shift+F3** move between matches. PNG, JPEG, BMP, and GIF files up to 8 MB have a scaled image preview. **Ctrl+N** opens the PZ file scaffold dialog. The output panel follows new messages when you are at the bottom; scroll up to read earlier output, or turn off **Follow output**.

### Build 42 project structure

```text
MyMod/
├── common/                 # Optional content shared by build targets
├── 42/
│   ├── mod.info
│   └── media/
│       ├── lua/
│       │   ├── client/
│       │   ├── server/
│       │   └── shared/
│       ├── scripts/
│       └── ui/
└── 41/                     # Optional legacy target
    ├── mod.info
    └── media/
```

The current stable build family is configurable under **File > App Options > System**. This keeps target discovery usable when Project Zomboid advances without preventing legacy targets.

When you add a target from the project tree, PZTools offers to copy the complete nearest previous version package. To keep only selected content aligned, right-click a file or folder inside a build target and choose **Sync Across Game Versions**. Folder sync includes files added later, and saved sync rules also populate targets added in the future. Sync is bidirectional, but it never deletes counterparts or overwrites independently changed content; conflicting copies stay in place for manual reconciliation.

### Dependencies and load order

Use the structured dependency grid in **Project Settings > Dependencies** to add an installed mod or enter an ID manually. `Required`, `Load After`, and `Load Before` are written to each target's `mod.info`; the visible row order seeds new playtest profiles. PZTools discovers Build 41 and Build 42 layouts in local Mods and Steam Workshop content, including `Contents/mods`, `common`, and numeric build wrappers.

Playtest Lab keeps declared requirements enabled, discovers transitive requirements, and fills local source and Workshop IDs when a matching installation is available. Local dependency sources must point at the mod container (the folder containing `42`, `common`, or a root `mod.info`), because copying only a numeric build subfolder produces an invalid cache layout. Generated client and server lists apply explicit before/after rules and reject dependency cycles before launch.

### Project Health checks

The health dashboard checks:

- Missing or invalid mod metadata, IDs, version ranges, posters, and icons
- Missing loadable content and Lua files outside client/server/shared scopes
- Lua syntax across every target
- Unbalanced ZedScript blocks and missing module declarations
- Invalid or duplicate-key Build 42 translation JSON and legacy translation files in B42 targets
- Required and transitive mods that are missing, duplicated, circular, or outside their declared game-version range
- Recent project-related warnings and errors in the game console log

Errors block local deployment and Workshop upload. Warnings remain visible but do not stop testing.

---

## Prerequisites

- **Visual Studio 2026 Insiders** with the **.NET desktop development** workload
- **.NET 10 SDK**
- **Git**
- **Java (JDK)** if you intend to decompile Project Zomboid JARs
- Internet access for downloading CFR and SteamCMD

## Build and run from source

```bash
git clone https://github.com/Bruce-Devlin/PZTools.git
cd PZTools
dotnet restore PZTools.slnx
dotnet build PZTools.slnx -c Debug
dotnet run --project PZTools.csproj
```

You can instead open `PZTools.slnx` in Visual Studio and run the `PZTools` project there. PZTools does not currently have a packaged release; future builds will be published on the [Releases page](https://github.com/Bruce-Devlin/PZTools/releases).

## Project Structure

```text
PZTools
├── Resources          Editor syntax-highlighting definitions
├── PZTools.Mcp        Local Codex MCP companion process
├── Tests              Smoke-test executables
└── Core
    ├── Windows        WPF windows, dialogs, styles, and themes
    ├── Models         Application, command, project, and view models
    └── Functions      Application services grouped by responsibility
```

## Configuration

### Java Path

PZTools tries to detect `java.exe` automatically. You can also set the `JAVA_HOME` environment variable.

### Project Zomboid Path

For an existing installation, point the app to your Zomboid folder. For managed installations, PZTools downloads and sets up builds through SteamCMD.

### CFR Decompiler

The CFR JAR is downloaded automatically when it is first needed.

After CFR finishes, PZTools indexes the recovered Java declarations for the selected game build. Open **View > Game Code Knowledge Base** to search names, packages, signatures, parameters, and recovered JavaDoc, filter by symbol kind, and jump back to the exact decompiled source. Indexes are cached beside each build's source and rebuilt automatically when the source files change; **Rebuild index** is available for a manual refresh.

The index describes recovered implementation code, not a guaranteed public mod API. A method may still be private, package-restricted, unsafe to call directly, or unavailable to Lua. Use the declaration, implementation, and call sites together, then verify behaviour in the matching game build.

### Updates

When startup update checks are enabled, PZTools checks the GitHub releases feed for the selected channel. It asks before opening the release download page and never replaces the running application automatically.

### Steam Workshop

Set the title, description, tags, visibility, Published File ID, default change note, and preview image under **File > Project Settings > Steam Workshop**, then choose **File > Upload To Steam Workshop**. PZTools runs Project Health, stages a clean package, and shows it for confirmation before opening SteamCMD.

Workshop metadata is stored in `.pztools/workshop-settings.json`. Steam passwords and Steam Guard codes are handled by SteamCMD and are not stored by PZTools.

### Agent MCP and Codex

PZTools includes a local MCP server so Codex can work through the same project, validation, deployment, and game-debugging services as the desktop UI.

1. Open the mod project in PZTools.
2. Open **File > App Options > Agent MCP**.
3. Enable only the capabilities you want the agent to use. Deployment is disabled by default.
4. Select **Configure Current Project for Codex**.
5. Restart Codex in that project and use `/mcp` to confirm that `pztools` is connected.

Setup adds a clearly marked PZTools block to the project's `.codex/config.toml` and preserves other Codex settings. Keep the matching project open in PZTools while using its MCP tools.

The bridge uses a per-project Windows named pipe restricted to the current Windows user. Every request is checked against the live PZTools capability settings, and project file paths are constrained to the active project. Read-only status and inspection tools are pre-approved in the generated Codex config; editor changes, deployment, and game process control still require Codex approval. Debug launches run Project Health first and will not start from a stale playtest copy unless Agent MCP deployment is enabled.

You can inspect the generated server entry with `codex mcp list`. To change capabilities later, update them in PZTools and select **Configure Current Project for Codex** again.

## Development

Build and run the automated checks from the repository root:

```powershell
dotnet build PZTools.slnx
dotnet run --project Tests/PZTools.Smoke/PZTools.Smoke.csproj -c Debug
dotnet run --project Tests/PZTools.PlaytestSmoke/PZTools.PlaytestSmoke.csproj -c Debug
dotnet run --project Tests/PZTools.DesktopSmoke/PZTools.DesktopSmoke.csproj -c Debug
dotnet format PZTools.slnx --verify-no-changes --no-restore
```

The repository `.editorconfig` is the source of truth for whitespace and basic C# layout. Keep comments focused on intent, constraints, or surprising behaviour; names and structure should explain ordinary control flow.

## Notes

- Managed installations require Steam credentials. SteamCMD prompts for login in its own console window; PZTools does not store the password.
- Decompiling large builds may take several minutes. The progress overlay displays the status.
- Back up save files before testing new builds.

## Troubleshooting

- **Missing Java:** Ensure `java.exe` is in `PATH` or `JAVA_HOME` is set.
- **SteamCMD errors:** Make sure SteamCMD is installed and updated.
- **Decompile fails:** Check internet connectivity; CFR is downloaded automatically.
- **UI issues:** Restart Visual Studio after the first clone so the XAML designer can finish restoring dependencies.

## Contributing

Contributions are welcome. Keep changes focused, preserve existing behaviour unless the change requires otherwise, and document new user-facing functionality. Build the solution, run the smoke-test projects, and state clearly which UI, game, and Steam checks you performed. Desktop smoke checks open temporary WPF windows and use a disposable project; run them on Windows with the application closed before building.

Use [GitHub Issues](https://github.com/Bruce-Devlin/PZTools/issues) for bug reports and feature requests. Include reproduction steps and an exported Project Health report where relevant, and remove credentials or other sensitive information from logs before posting them.

## Project status and licensing

PZTools is an independent, work-in-progress community tool. Automated checks cover many core workflows, but they do not replace interactive WPF testing, a live Project Zomboid session, or a Steam Workshop upload.

A standalone license file is not currently included in this repository. Until one is added, no general permission to copy, redistribute, or reuse the source should be assumed.

Project Zomboid is a trademark of The Indie Stone. Steam is a trademark of Valve Corporation. PZTools is not affiliated with or endorsed by The Indie Stone or Valve.
