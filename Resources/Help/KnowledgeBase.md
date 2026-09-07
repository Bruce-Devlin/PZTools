# welcome | Getting started | Welcome to PZ Tools
Learn how to use PZ Tools to organise projects, inspect files, manage app-generated content, validate changes, and run repeatable tests.

## Start here
This knowledgebase documents the desktop application and its controls. Project Zomboid modding concepts, code examples, game APIs, and content design belong in the PZ Modding Wiki. The separate Game Code Knowledge Base is a browser for recovered game source; it is not this app manual.
1. [Set up the app and game paths](setup).
2. [Create or import a project](projects).
3. [Find your way around the workspace](workspace).
4. [Choose an external editor](external-editor) and [find project files](search).
5. [Check project health](health), [deploy](deployment), and [run a playtest](playtest).

## Browse by task
- Organising work: [project settings](metadata), [build targets](targets), [version sync](version-sync), and [dependencies](dependencies).
- Working with content: [file operations](files), [previews](preview), [file scaffolds](scaffolds), and [Content Managers](content-managers).
- Checking changes: [Test Explorer](test-explorer), [game tests](game-tests), [logs](logs), and [deployment checks](deployment).
- Running the game: [profiles](playtest), [save modes](save-modes), [multiplayer sessions](multiplayer), and [docking](game-panel).
- Finishing work: [Workshop settings](workshop-settings) and [upload review](workshop-upload).
- Customising the app: [App Options](options), [Agent MCP](agent), [updates](updates), and [shortcuts](shortcuts).

## Using this knowledgebase
Search matches words anywhere in an article, including its title and category. Every word you enter must match; search is case-insensitive and does not use regular expressions. A category narrows the search. Select a result to read it; clearing the search restores the list. A search with no matches leaves your current article open.
Use the links below an article title to jump to a section. Links in the text open related articles. Back and Forward retrace the articles you have read; Home clears the filters and returns here. A− and A+ change reading size. Select article text to copy it with Ctrl+C.
The window can stay open while you work in PZ Tools. Articles ship inside the application and do not require an internet connection, a game installation, or a generated source index to read.

# setup | Getting started | First setup and game installations
Choose where PZ Tools works and which game installation it uses before decompiling or launching a game.

## Choose an installation mode
During setup, choose an app installation folder and the game management mode. Existing uses a game installation already on disk. Managed uses SteamCMD to install selected builds into a separate location. Managed downloads need network access and a Steam account with access to the game.
For Existing mode, browse to the game installation directory, not a shortcut, save folder, or Workshop mod. For Managed mode, choose the managed installation location and requested builds. Follow the setup progress messages; downloads and decompilation can take time.
Steam login happens through SteamCMD. Complete its password and Steam Guard prompts there. PZ Tools does not store those credentials.

## Review paths later
Open File > App Options > System to check Game Management Mode, Existing Game Install Path, Managed Game Install Path, and Current Stable Build. The two game paths serve different modes; changing the inactive one does not fix the active mode's path.
Current Stable Build controls how the app understands build families. It does not install or upgrade the game. Review the actual Game build selection in Playtest Lab before running a profile.

## Optional source browsing
Decompilation is needed for recovered Java source browsing, not for reading this help. Java must be available through PATH or JAVA_HOME. PZ Tools downloads CFR when needed and reports progress. See [game source tools](game-source).
Once setup finishes, use the project picker to [create or import a project](projects). If setup or a launch cannot find the game, start with [troubleshooting](troubleshooting).

# projects | Getting started | Create, import, and open projects
The project picker is the entry point for managed workspaces. An imported project is a working copy, so edit the copy opened by PZ Tools.

## Create a project
1. Use the new-project action in the project picker.
2. Enter the requested project details and choose whether to include the legacy compatibility target when offered.
3. Open the created project and review File > Project Settings.
4. Select a target in Project Explorer before adding files or using content tools.
New projects use a versioned layout. The project name identifies the workspace; metadata such as the mod ID is managed through Project Settings. See [build targets](targets).

## Import existing work
Use the import action and select the source mod folder. Import copies recognised content into the managed project area rather than editing the original folder in place. Read the import result for warnings and detected targets, then inspect the project tree and run Project > Health Dashboard.
After importing, use Project > Open Project Folder to locate the working copy. An external editor still pointed at the original folder will not change the imported project.

## Find and reopen projects
Filter the picker by name or location and double-click a project to open it. Open folder reveals the project storage location. File > Close Project returns through the app's reload flow so you can select another project.
For a portable backup, keep the complete project folder, including its hidden configuration folders. See [storage and backups](storage).

# workspace | Getting started | Workspace tour
The main window combines a project tree, file preview, Inspector, output, and shortcuts to the app's main workflows.

## Main areas
- Project Explorer, on the left, groups files by build target. Expand a target or folder and select an item to inspect it.
- Code shows the selected file preview. Game hosts a running windowed playtest when docking is available.
- Inspector shows the selected item's name, full path, file details, attributes, and version-sync controls where applicable.
- Output shows PZ Tools operation messages. The status area gives current activity, while the bottom action buttons open common workflows.
Use the pane dividers to resize the workspace. View > Save Window Layout stores the current layout for later use.

## Main action buttons
New PZ File opens a guided file scaffold. Content Managers opens the visual content tools. Project Health opens diagnostics. Open in VS Code sends the project to the configured compatible editor. Deploy Project copies the project to the local testing destination. Run Game uses the playtest workflow.
Menu actions expose further tools, including Project Settings, Test Explorer, source browsing, and App Options. This manual gives menu paths when the same action is not always visible in the workspace.

## A normal work cycle
Select a file, open it in your external editor, save changes there, and return to its refreshed preview. Run health checks, deploy current content, then launch the intended profile. Review output and game logs if the result differs from expectations.
See [previews](preview), [file operations](files), [health](health), and [Playtest Lab](playtest) for the individual steps.

# files | Files and editing | Project Explorer and file operations
Project Explorer manages files on disk. Its context menus depend on whether you select a project, target, folder, or file.

## Create and organise files
Right-click the destination folder to create a file or folder. New File asks for a name and extension; use [New PZ Mod File](scaffolds) when you want one of the app's prepared templates instead of a basic file.
Use the selected item's context menu for rename, delete, opening externally, and revealing its location. Check the Inspector's full path before an operation if several targets contain the same filename.
Drag files or folders onto the intended destination in the tree to organise content. Read any collision or validation prompt before continuing. Moving a file changes its path; external editors and tools may need to reopen it at the new location.

## Undo and redo
Edit > Undo and Edit > Redo operate on the app's undoable command history, including supported file operations. Availability depends on what is in that history. They are not a project-wide version-control system and do not reverse arbitrary external edits, uploads, or game sessions.
For lasting recovery points, keep backups or use source control. Do not rely on an undo history surviving an app restart.

## Missing or unexpected files
External saves and filesystem changes are watched by the workspace. If a selected file was renamed or removed, select its current location again. If a file seems missing, use [project search](search) and Project > Open Project Folder to compare the on-disk project with the tree.
For keeping corresponding files aligned across targets, use [version sync](version-sync).

# preview | Files and editing | Read-only previews and finding text
The Code pane lets you inspect saved files. Make text changes in an external editor; the PZ Tools preview is read-only.

## Preview text
Select a text file in Project Explorer. The preview header shows its relative path and kind. Syntax highlighting depends on the file type. External saves refresh the preview while preserving your reading position where possible.
Use File > App Options > Editor to adjust preview font size and word wrap. If text looks unchanged, confirm that your editor saved the same full path shown in Inspector rather than another target or an original import source.

## Find inside the current file
Press Ctrl+F, enter the text, and use F3 or the down-arrow button for the next match. Shift+F3 or the up arrow moves backwards. The find bar reports the current match and total. Escape closes the find bar. Changing files closes it so the old search does not obscure a new preview.
For matches across multiple files, use [Find in Project](search).

## Preview images and other assets
PNG, JPEG, BMP, and GIF images up to 8 MB have scaled previews. Unsupported or oversized files show file information instead of being loaded as text. Use the external-open action when a format needs a dedicated application.
The image preview releases its file handle, allowing an image editor to save an updated copy. Use Inspector to check the path and size when diagnosing the wrong asset.

# search | Files and editing | Find in Project
Search across the current project's targets and shared files without opening every file individually.

## Search content
1. Choose Project > Find In Project, use the workspace search button, or press Ctrl+Shift+F.
2. Enter a literal word or phrase. Content search ignores case unless you enable case matching.
3. Run the search and inspect the matching path, line, and text.
4. Open a result to show the matching location in the file preview.
Use a distinctive phrase to reduce a large result set. This searches project files; it does not search the app manual or generated game-code index.

## Search filenames
Enable File names only when looking for a path or asset rather than text inside it. This is useful for finding a particular poster, icon, or filename that exists in several targets.

## Understand search limits
Internal and generated folders are excluded. Linked paths and text files above 2 MB are skipped. Results can be capped; read the search status for skipped files and truncation before concluding that there are no other matches.
If you need an excluded file, reveal the project folder and inspect it directly. To search one open file, use [preview find](preview). To search this manual, focus its search box with Ctrl+F.

# inspector | Files and editing | Inspector and file attributes
Inspector describes the current selection and provides direct access to its path, Windows attributes, and sync status.

## Inspect the selection
Select an item in Project Explorer. Check Name and Full Path first; files with the same name can belong to different targets. Size, encoding, and details provide context for supported file types. Selecting a target or folder changes the information available.
Copy path copies the selected path for use in another tool. Reveal opens its location in Windows Explorer. Refresh rereads the selected item's details.

## Change file attributes
Read-only file and Hidden file change Windows attributes for the selected copy. They do not apply to all versions of a file. A read-only file cannot be updated by version sync until it is unlocked.
If an editor or sync operation reports access problems, check Read-only file and the path before trying again. Hidden changes visibility in tools that honour the Windows attribute; it is not encryption or a backup feature.

## Version sync controls
Eligible target items show Sync across game versions and Sync now. The checkbox controls participation in the saved sync rules; Sync now requests a pass immediately. Review status and output for conflicts rather than assuming every counterpart was overwritten.
See [version sync](version-sync) for the workflow and conflict handling.

# external-editor | Files and editing | External editors and VS Code workspaces
PZ Tools handles project operations while an external editor handles text editing.

## Configure your editor
Open File > App Options > Editor. Set Default File Editor to your editor executable and review Default File Editor Args for that editor's invocation. Use the file's external-open action to edit it, then save in the editor and return to the PZ Tools preview.
If opening a file fails, check that the configured executable still exists and that the arguments are appropriate for it. Reveal the file to confirm it is present.

## Prepare a compatible workspace
Choose Project > Set Up VS Code Workspace to create the app's workspace integration. Read the result showing which files were created, updated, or preserved. Existing user configuration is retained; inspect preserved files if an expected integration is absent.
Project > Open In VS Code opens the project in an available supported editor, including VS Code, VS Code Insiders, or VSCodium. Install a compatible editor separately if the app cannot find one.

## Work on the right copy
The opened project is the source of truth for preview, health, and deployment. Save in that folder, not a deployed copy or the original folder used during import. Files saved externally should refresh in the app.
Agent integration is configured separately under [Agent MCP](agent). Preparing a VS Code workspace does not by itself enable every agent capability.

# scaffolds | Files and editing | New PZ Mod File
The scaffold dialog creates a file from an app-provided template in the selected project target.

## Create a scaffold
1. Choose Project > New PZ Mod File, click New PZ File, or press Ctrl+N.
2. Select the intended target and template.
3. Enter the requested name and any template-specific fields. Review the resulting destination before creating it.
4. Open the generated file in your editor, save your changes, and run Project Health.
Templates cover Lua files, item and recipe definitions, translations, and project documentation. The dialog handles the initial placement and starter content; it does not implement your feature for you.

## Choose the right tool
Use a scaffold when starting a supported file from a template. Use the folder context menu's New File action for a basic file. Use [Content Managers](content-managers) when editing the app's structured content through forms and grids.
If the destination already exists, choose another name or open the existing file. Verify the selected build target when similar names appear in multiple versions.
For the meaning of mod content or how to write it, use the PZ Modding Wiki. This knowledgebase covers only the creation workflow inside PZ Tools.

# targets | Project management | Build targets and shared content
A project can contain multiple game-version targets. Select the target you intend to work on before creating or managing content.

## Understand the tree
The project is the overall working folder. Numeric targets hold version-specific packages. A common folder holds shared content when present. PZ Tools discovers supported versioned layouts and legacy projects, and presents their files in Project Explorer.
Game build selection in Playtest Lab determines the installed executable to launch. It is separate from selecting a project target in the tree. Review both when testing a version-specific change.

## Add a target
Right-click a target in the project tree and choose Add Target. Enter the requested version and review the offer to copy the complete nearest previous version package. Accepting the copy gives the target an initial package, including its existing content; it is not only a metadata copy.
After adding it, expand the new target, review Project Settings, and run health checks. A copied package is a starting point; copying does not establish that its contents work with another game version.
The target context menu also offers Remove Target. Confirm the exact target and retain a backup before removing a version you may need later. Check the remaining project and profile selections after the removal.

## Keep selected content aligned
Adding a target and continuously syncing files are separate operations. Use [Sync Across Game Versions](version-sync) to maintain chosen files or folders across existing and future targets. Review sync results when creating a target because saved rules can also populate it.
Use File > App Options > System > Current Stable Build when the app's build-family setting needs updating. That setting does not convert project content.

# version-sync | Project management | Sync across game versions
Version sync keeps selected files or folders aligned across targets while preserving independently changed copies for review.

## Enable a sync rule
1. Select a file or folder inside a build target.
2. Right-click and choose Sync Across Game Versions, or use the Inspector checkbox.
3. Check the sync status and output. Use Sync now in Inspector when you want an immediate pass.
Folder rules include files added later. Saved rules also populate targets added in the future. Sync is bidirectional, so editing a participating copy can affect its eligible counterparts.

## Resolve a conflict
Sync does not delete counterpart files or overwrite independently changed content. When versions have diverged, open each reported path and compare them in your editor. Reconcile the content deliberately, save the intended copies, and run sync again.
Check the Inspector's Read-only file attribute if a counterpart cannot be updated. The flag applies only to that copy. Unlock it only if you intend to allow updates there.

## Stop syncing an item
Disable the item's sync rule when its versions need to evolve separately. Disabling sync does not remove copies already created. A folder rule may also cover an item, so review the parent selection if it still participates.
Sync is not a backup and does not replace source control. Keep [project backups](storage) before broad changes, and check [health](health) afterwards.

# metadata | Project management | Project Settings
File > Project Settings edits the project's metadata, assets, compatibility, dependencies, and Workshop details.

## Edit and save
Open Project Settings with the intended project active. Work through the tabs, review the values, and click Save to persist changes. Cancel dismisses the settings dialog without applying its pending form changes.
- Basic contains the project's identifying and descriptive metadata.
- Media manages the displayed project artwork; use its browse and preview controls to choose the intended files.
- Dependencies contains the structured mod list and ordering preferences.
- Compatibility contains game-version constraints.
- Steam Workshop holds publication metadata and the preview used during upload.
Project settings and App Options have different save behaviour: Project Settings has Save and Cancel, while App Options saves changes automatically.

## Verify the result
After saving, run Project > Health Dashboard. The dashboard can detect metadata problems and missing or invalid referenced assets. Check diagnostics for the exact target and file rather than assuming the issue affects every version.
Changing a project value does not automatically publish it to Steam. Workshop changes are sent only through the upload workflow.
See [dependencies](dependencies), [Workshop settings](workshop-settings), and [health](health) for detailed steps.

# dependencies | Project management | Dependencies and load order
Manage declared project dependencies in Project Settings and the concrete test-session copies in Playtest Lab.

## Declare project dependencies
1. Open File > Project Settings > Dependencies.
2. Use Refresh to rescan installed content if needed. Choose an installed entry and Add installed, or use Add ID for a manual entry.
3. Set Required, Load After, and Load Before as appropriate for the relationship you intend to record.
4. Use Up, Down, or Remove to organise the grid, then Save.
The app writes these declarations to the project targets. The visible row order seeds new playtest profiles. Availability tells you what PZ Tools found locally; a manually entered ID does not download or install content.

## Configure a test profile
Open Debug > Run Game Settings > Dependencies & Load Order. Use Sync required to reconcile the profile with project requirements. Required entries stay enabled; add optional compatibility-test mods separately and arrange their preferred order.
Use Browse source to provide a local mod container when it is not discovered automatically. Select the container containing the version folders or root metadata, rather than only a numeric subfolder. Workshop ID and Mod ID are different fields; do not swap them.

## Fix a blocked load order
Generated client and server lists apply explicit before/after constraints and reject dependency cycles before launch. Resolve the conflicting declarations and rerun health checks. A missing transitive dependency may need installing or a valid local source even when the directly selected mod is present.
See [multiplayer profiles](multiplayer) and [troubleshooting](troubleshooting).

# content-managers | Content tools | Content Managers overview
Content Managers provides visual editors for professions, traits, sandbox options, and localization files managed by PZ Tools.

## Choose the destination first
Click Content Managers in the main workspace. Select the target at the top and then the appropriate tab. Check the displayed destination path before loading or saving, especially when several targets contain similarly named content.
Load reads existing supported content into the form or grid. Add and Remove change that in-memory list. The tab's Save action writes its content to disk; editing a field alone is not the same as saving the file. Save before changing target or closing the window if you want to keep the edits.

## Work with generated content
These tools handle the content shapes they support. They are not general-purpose editors for arbitrary handwritten Lua or scripts. Review the output path, validation messages, and saved file. Existing files written through the managed save service receive a backup; keep independent backups for long-term recovery.
Run Project Health after saving. For game-specific field meanings and design advice, use the PZ Modding Wiki.

## Pick a manager
- [Professions](professions) uses a list and detail form.
- [Traits](traits) uses a list and detail form.
- [Sandbox options](sandbox-options) uses an editable options grid.
- [Localization](localization) manages translation keys and values.
To create a standalone template instead, use [New PZ Mod File](scaffolds).

# professions | Content tools | Using the professions manager
The professions tab edits the app-managed profession list for the selected target.

## Add or edit an entry
Open Content Managers, choose the target, and select the professions tab. Load existing supported content when you want to continue editing it. Select an entry in the list to populate the detail form, or use Add to create an entry with a generated identifier.
Edit the identifier and the displayed fields, including name or description translation keys, icon, cost, boosts, and recipes where offered. Follow the field format shown by the form. Select the entry again if the detail area is disabled because nothing is selected.
Remove removes the selected entry from the current list. Review the complete list before saving; the save writes the managed collection, not just the highlighted entry.

## Save and inspect
Use the professions Save action and read the saved destination. PZ Tools chooses the supported output form for the target. Open the generated file in Project Explorer and run health checks to catch invalid output or missing references.
If a field fails validation, fix its input and save again. Do not treat an unchanged file on disk as saved just because the form contains your new values.
See [localization](localization) for managing the text keys referenced by the form and [Content Managers](content-managers) for shared behaviour.

# traits | Content tools | Using the traits manager
The traits tab manages a selected target's supported trait entries through a list and a detail form.

## Edit the list
Choose the target, open the traits tab, and load existing content if required. Add creates a new entry; selecting it enables the detail editor. The form includes ID, name and description keys, cost, profession-only and multiplayer options, boosts, and recipes.
Enter values in the formats expected by the fields. Keep the selected entry in view while editing to avoid changing a similarly named item. Use Remove to take the selected entry out of the list before saving.

## Persist changes
Use the tab's Save action after reviewing all entries. It writes the managed list for that target and reports the path. Inspect the generated file and run Project Health. When several targets need the same output, deliberately configure [version sync](version-sync) rather than assuming the manager edits every target.
The manager supplies an app workflow for recording these values. It does not validate that a gameplay design behaves as intended; that needs an actual playtest.
See [Content Managers](content-managers) and [Playtest Lab](playtest).

# sandbox-options | Content tools | Using the sandbox options manager
The sandbox tab presents supported options in an editable grid for the current target.

## Work with rows
Select the target, open the sandbox tab, and load its existing options. Add a row for a new option or select and remove one you no longer want. Review the option name, type, default, limits, step, values, page, and translation-related columns shown by the grid.
Commit the active cell by moving to another row or control before saving. Use the horizontal scroll area if some columns are outside the visible width. Read validation feedback when a value does not match the selected option type.

## Save and review
Save writes the options to the displayed destination. Unknown properties are preserved by the supported load/save workflow. Inspect the saved file and run health checks before deployment.
Avoid editing the same file externally while holding an older copy in the manager; reload before continuing so you do not save stale form contents over newer work.
For the in-game meaning of option types and values, consult the PZ Modding Wiki. For text keys used by the options, see [localization](localization).

# localization | Content tools | Using the localization manager
Use the localization tab to load, edit, and save translation keys and values through the app.

## Select the file context
Open Content Managers and select the intended target. Choose the localization tab, then set its language and translation file or category controls before loading. Check the path so that you do not edit a different language or target by mistake.
Load existing values before editing an existing file. Add and remove rows as needed, and edit the key/value cells. Commit the current cell before saving. The manager writes the format supported for the selected target.

## Validate your changes
Save, inspect the generated file, and open Project Health. Invalid JSON or duplicate translation keys can appear as diagnostics in supported version targets. Resolve the reported file and key, then refresh health.
Switching language or target changes the working destination; save pending changes first. If you already changed the file in an external editor, reload it before making further form edits.
The app helps maintain files; it does not translate your content or explain game translation naming rules. See the PZ Modding Wiki for those details and [Content Managers](content-managers) for the common workflow.

# health | Validation and testing | Project Health Dashboard
Project Health checks the saved project and reports issues before deployment or upload.

## Run checks
Open Project > Health Dashboard or click Project Health. Leave Validate every Lua file during health checks enabled for a complete Lua syntax pass. Use Refresh health after fixing files or changing settings; an old report describes the earlier state.
The dashboard checks metadata, referenced assets, supported content structure and syntax, dependencies, and recent project-related game-log issues. Review each diagnostic's severity, code, path, and location. Open a diagnostic to inspect the affected file where supported.

## Understand readiness
Errors block local deployment and Workshop upload. Warnings remain visible but do not stop testing. A clean health report means these checks passed; it is not evidence that every feature works in the game.
Lua syntax checks, unit tests, and game tests are separate tools. If your problem requires executing a test suite, open [Test Explorer](test-explorer). If it appears only during play, review [logs](logs) after reproducing it.

## Share a report
When analysis is complete, use Export report to save a readable health report. Include it with a bug report when it helps identify the failure. The dashboard also provides shortcuts to Project Settings, the project folder, game log, and VS Code setup.
After correcting an error, refresh health and retry the blocked operation. See [troubleshooting](troubleshooting) for common cases.

# test-explorer | Validation and testing | Test Explorer and unit tests
Project > Test Explorer discovers test files, runs them, and keeps results and report artifacts available for inspection.

## Prepare and choose tests
Open Test Explorer with your project active. Create test examples adds the app's starter test material; inspect it through Edit selected file. Refresh updates the file list after changes made in your editor.
Select a file to limit a run to that file. Clear selection runs all matching files for the chosen test type. Review the selection before launching a suite so you know which files will be included.

## Run unit tests
Click Run unit tests. These run without launching Project Zomboid. Wait for the status and result table; select a result to read its file, message, and recorded steps. The table includes status, name, endpoint, and duration.
Cancel run requests cancellation. Wait for the runner to finish cleanup before starting again. Open report folder becomes available after a report is produced and reveals the artifacts for that run.

## Interpret results
Check both failed tests and runner errors. A failure to discover, load, or execute a suite is different from an assertion failure inside a test. Unit tests use the app's test environment and do not prove live game behaviour.
Use [game tests](game-tests) for the app's automated game-session workflow. Use [Lua checks](lua-checks) when you only need syntax feedback on saved Lua files.

# game-tests | Validation and testing | Running game tests
Game tests use a configured playtest profile to exercise tests in an isolated game session and collect results.

## Before running
1. Configure and save the intended profile in Debug > Run Game Settings.
2. Confirm the game build, save mode, dependencies, and server/client settings as appropriate.
3. Run health checks and resolve errors.
4. Open Project > Test Explorer and select the intended game-test file, or Clear selection for all matching files.
The Playtest profiles button in Test Explorer opens profile setup. Test examples provide starting files; writing game test logic is outside the scope of this app manual.

## Run and inspect
Click Run game tests and follow the status. Game startup can take longer than a unit run. Review endpoint and test messages in the result table, then use Open report folder for collected artifacts.
If startup fails or times out, inspect the profile's selected executable, dependencies, and session output before changing the tests. A runner error may mean the game never reached the test stage.

## Cancel safely
Cancel run requests cancellation and stopping of owned test processes. Closing the test window during a run also waits for cancellation handling rather than abandoning the runner. Allow cleanup to finish.
For manual reproduction, run the same [Playtest Lab profile](playtest) and compare its [logs](logs).

# lua-checks | Validation and testing | Quick Lua checks and watermarks
The Debug menu includes quick saved-file checks. The File menu includes actions for applying the project's configured Lua watermark.

## Check Lua files
Debug > Test > Test This Lua File checks the currently opened saved Lua file. Select a Lua file first; other file types are rejected. Test All Lua Files checks Lua files across the project and reports through the app's output.
These quick checks are separate from Test Explorer suites. They are not a live game session. Save in your external editor before running them so that the checked disk contents include your changes.
For a project-wide readiness review including metadata and dependencies, use [Project Health](health).

## Apply a watermark
Set the Lua Watermark in File > Project Settings > Compatibility and Save. File > Lua Watermark > Apply Watermark To This File uses that watermark. Apply Watermark To All Files processes the project's Lua files. The app reports when no watermark is set.
Watermark actions write to files. Confirm the active project and selected file, and retain a backup before applying a project-wide change. Inspect the saved result afterwards. They do not upload the project or establish licensing.
See [file operations](files) for recovery boundaries and [storage](storage) for backups.

# deployment | Validation and testing | Deploy Project and stale-copy checks
Deployment copies the saved project to the local Mods destination used for testing. It is distinct from publishing to Steam Workshop.

## Deploy current work
1. Save your changes in the external editor and close or save any content-manager edits.
2. Run Project Health and fix blocking errors.
3. Click Deploy Project in the workspace.
4. Read the output for completion or a failure message before starting the game.
The project folder remains your working source. Editing only a deployed file does not update the project and can create a mismatch on the next deployment.

## Understand copy status
PZ Tools records deployment information and compares file hashes to detect missing, modified, and stale copies. A copy can become stale after an external edit, a file removal, or changes to the project after its last deployment.
If a run reports stale deployment, save the intended source files and deploy again. Review any health errors that prevent the refresh. Do not use a successful earlier deployment as evidence that today's files are already copied.

## Different destinations
Playtest Lab prepares isolated session content for its profiles. Workshop upload stages a separate publication package and requires review. Neither should be confused with modifying your managed project directly.
See [Playtest Lab](playtest), [Workshop upload](workshop-upload), and [logs](logs).

# playtest | Running the game | Playtest Lab profiles
Playtest Lab stores reusable launch settings so you can repeat a test with the intended build, content, and isolated game data.

## Create a profile
Open Debug > Run Game Settings, or Run / Playtest Lab on the Game tab. Choose an existing profile or New. Set Profile name, Mode, Game build, and any Launch arguments. Review the display choice and window size; the default window is 1280 × 720.
Choose the save mode, configure dependencies, and fill the Dedicated Server tab if using that mode. Click Save profile to retain the setup. Delete removes a profile from the list; choose carefully when you have several similar profiles.

## Launch and stop
Click Run profile and watch SESSION OUTPUT and the status. The app validates and prepares the profile before launch. A failure during preparation is not a successful game start; inspect the message and correct the profile.
Use Stop session to stop the session managed by this window. Wait for the status to settle before launching another run. Keep isolated session data after the test finishes controls whether test data is retained for later inspection.

## Run Game shortcut
Show this each time I press Run Game controls whether the workspace shortcut opens the settings window on each run. Keep it enabled while adjusting a profile if you want to review the settings before launch.
See [save modes](save-modes), [dependencies](dependencies), [multiplayer](multiplayer), and [game docking](game-panel).

# save-modes | Running the game | Isolated saves and session data
The profile's save mode determines how its isolated world is prepared. Choose it deliberately when comparing runs.

## Choose a save mode
- Fresh Clone copies the selected source save into the session workspace. Use Browse beside Source save folder to choose the source you intend to clone.
- Empty starts with a clean isolated world instead of copying a source save.
- Reuse keeps the profile's isolated world so that later runs can continue its state.
The source save and the isolated copy have different roles. Changes made while testing belong to the test-session copy; do not expect them to be written back into the source save.

## Keep useful evidence
Enable Keep isolated session data after the test finishes when you need to inspect the world or logs after stopping. If your workflow depends on persistent profile state, review this option alongside Reuse.
Keep a normal backup of an important source save before using it in a new test workflow. Profile isolation separates routine test data; it does not replace your backups.

## Reproduce a problem
For a repeatable comparison, use the same profile, game build, dependencies, and starting save. Empty helps check whether a problem depends on old state. Fresh Clone helps repeat a fixed starting condition. Reuse helps investigate a sequence across sessions.
Record the selected mode with your [test report](test-explorer) or [game log](logs).

# multiplayer | Running the game | Dedicated server and local clients
Dedicated-server profiles coordinate an isolated local server and optional clients through Playtest Lab.

## Configure the profile
Choose the dedicated-server Mode on the Profile tab. In Dedicated Server, browse for the Server install/launcher and set Server name, Port, Maximum players, Local clients, and Startup timeout. These fields describe the local session PZ Tools will try to start.
No-Steam local test selects the local testing mode. Auto-connect clients controls whether launched clients connect automatically. Additional non-secret server options accepts one Key=Value setting per line; use the dedicated fields for settings already represented by the form.
Review Dependencies & Load Order and ensure required content has valid source paths. Local copies are prepared into isolated caches; Workshop IDs are emitted for Steam-enabled servers.

## Start and observe
Save profile, then Run profile. Watch SESSION OUTPUT while the server starts and readiness is checked. Clients may start after server readiness; launching the server process alone is not proof that every endpoint is ready.
If startup times out, check the launcher, selected build, dependencies, and server messages. Increasing the timeout is useful only when startup is valid but slower than the configured allowance.

## End the session
Use Stop session and wait for cleanup. PZ Tools manages the processes belonging to its session. Review the output and retained artifacts when a client or server behaves differently.
For automated endpoint results, see [game tests](game-tests). For dependency cycles or missing content, see [dependencies](dependencies).

# game-panel | Running the game | Docking the game and releasing focus
The Game tab can host the window of a running windowed playtest so you can switch between app context and the game.

## Use the Game tab
Start a windowed playtest using Run / Playtest Lab. The Game tab shows its current status and enables Dock or Undock when a suitable managed game window is available. Dock embeds the window; Undock returns it to a separate desktop window.
The Code tab remains available for file inspection. Docking changes where the game window is displayed; it does not restart the world or deploy new project content.

## Release keyboard focus
Press Ctrl+Shift+F12 to release focus from the game back to PZ Tools. Use this when normal app shortcuts are being consumed by the game. Click inside the game again when you want to resume game input.

## If docking is unavailable
Check that the session is running and has reached a visible window. Review Game display and Window size in the profile; docking expects a suitable windowed game process. Startup failures belong in the session output rather than the docking controls.
Use Undock if a workflow needs a separate game window, and [Stop session](playtest) to end the playtest.

# logs | Validation and testing | Output, game logs, and reports
Use the right output source to distinguish an app operation failure from a game runtime problem.

## PZ Tools output
The main Output panel records app activity such as validation, copying, decompilation, and launch preparation. Follow output keeps new messages visible while you are at the bottom. Scroll up to inspect earlier messages, or disable Follow output to stop following.
Clear console clears the visible app console. Capture useful failure details before clearing it. A message that an operation started is different from its eventual completion message.

## Game and session output
View > Game Log and Open game log open the configured game user's console.txt. Isolated Playtest Lab sessions use their own data and SESSION OUTPUT; inspect those when the general game log does not describe the run you just made.
Project Health can correlate recent project-related runtime issues from the game log. Refresh it after reproduction and check the timestamp and affected file so an old error is not mistaken for a current one.

## Reports
Export report in Project Health saves the diagnostic report. Open report folder in Test Explorer reveals artifacts from the completed test run. When reporting a problem, include reproduction steps, the active profile/build, and the relevant error or report.
If there is no report because preparation failed, copy the visible runner or session error and diagnose that first. See [troubleshooting](troubleshooting).

# workshop-settings | Steam Workshop | Prepare Workshop settings
Workshop settings describe the publication package. They are saved with the project and are separate from Steam login.

## Fill the metadata
Open File > Project Settings > Steam Workshop. Review Title, Description, Tags, Visibility, Published File ID, Default change note, and Preview image. Save the settings before starting an upload.
Published File ID identifies the existing Workshop item to update. Confirm it carefully if you intend to update an existing listing; it is not the project's mod ID. An unset or new-item value follows the new-publication workflow.
Choose the intended preview image through the provided controls and inspect it. The project's in-game media and the Workshop preview serve different purposes; changing one should not be assumed to change the other.

## Storage and preparation
The app stores this configuration in .pztools/workshop-settings.json inside the project. Include the project configuration folder in backups so a restored workspace retains its publication identity and defaults.
Steam passwords and Steam Guard codes are entered through SteamCMD, not stored in this settings file.
Run Project Health before upload. Metadata or asset errors need fixing before the package can proceed. See [upload review](workshop-upload).

# workshop-upload | Steam Workshop | Review and upload a Workshop package
File > Upload To Steam Workshop validates the project and presents a package review before SteamCMD publication.

## Review before uploading
1. Save your source files and [Workshop settings](workshop-settings).
2. Choose File > Upload To Steam Workshop.
3. Resolve any blocking Project Health errors. The app opens the dashboard when health blocks publication.
4. Inspect the review dialog's target item, package details, metadata, preview, and change note. Cancel if the package is not the one you intend to publish.
5. Confirm the review, then complete Steam login and SteamCMD's prompts.
Upload stages a clean publication package. A local deployment or a successful health check alone does not publish anything.

## Confirm completion
Wait for the upload result. On success, the app can offer to open the published item for review. Check the listing to confirm its content and visibility. If Steam requests an agreement on the item page, complete that in Steam's interface.
If SteamCMD does not confirm success, PZ Tools reports that the upload was not confirmed. Review the app output and SteamCMD Workshop logs before retrying. Check the saved Published File ID before another attempt, especially after a first publication.

## Common blockers
Missing preview files, invalid metadata, health errors, login issues, or SteamCMD failures can stop the workflow at different stages. Fix the reported stage rather than repeatedly changing project content when authentication is the problem.
See [health](health), [logs](logs), and [troubleshooting](troubleshooting).

# game-source | Reference tools | Decompiled files and Game Code Knowledge Base
The app's source tools help you browse locally recovered game files. This article explains their controls, not the game code itself.

## Prepare the source index
Check the active game installation path in App Options. Choose File > Decompile Game Files and wait for progress to complete. Java must be available and CFR may need to download. Managed mode can process multiple installed build directories.
After decompilation, PZ Tools generates a searchable index beside the recovered source. View > Decompiled Game Files opens the source location. Project > Open Game Lua Source opens the installed game's Lua folder when available.

## Search the generated index
Choose View > Game Code Knowledge Base. Select a build, a symbol kind, and a search query. Search covers names, packages, signatures, parameters, and recovered documentation. Methods are selected by default.
Include bundled libraries broadens package coverage. Include internal / generated members reveals declarations hidden by the default filter. Select a result to inspect details and use its source-opening action to inspect the recovered file.
Use Rebuild index for a manual refresh when you need to regenerate cached results. Source changes can also trigger automatic index rebuilding.

## Which knowledgebase to use
Help > How to use opens this application manual without needing decompilation. View > Game Code Knowledge Base depends on locally recovered source. Use the PZ Modding Wiki for modding instructions and interpretation of game APIs.

# options | App settings | App Options and appearance
File > App Options contains application-wide preferences. Changes save automatically as you edit them.

## General and System
General controls App theme and Confirm on exit. Choose the theme that suits your display; this knowledgebase uses the same app theme resources.
System shows the app installation path, game management mode, game installation paths, and Current Stable Build. Check which game mode is active before correcting a path. These are app-wide values, unlike individual Playtest Lab profiles.

## Editor
Font size and Word wrap affect reading in the file preview. Default File Editor and its arguments configure external opening. PZ Tools text previews remain read-only. See [external editors](external-editor).
This knowledgebase's A− and A+ buttons change its own article reading size for the open window.

## Agent MCP and Updates
Agent MCP has the local server switch, capability checkboxes, and Configure Current Project for Codex action. See [Agent MCP](agent).
Updates controls startup checks and the selected release channel, with a Check now action. See [updates](updates).
For panel sizing and placement, resize the workspace dividers and use View > Save Window Layout. File > Project Settings is the separate dialog for metadata belonging to the active project.

# agent | App settings | Agent MCP controls in PZ Tools
Agent MCP lets a compatible connected agent use PZ Tools services for the currently open project, subject to the app's capability switches.

## Enable the integration
1. Open the intended project in PZ Tools.
2. Open File > App Options > Agent MCP.
3. Enable the PZ Tools MCP server and choose the capabilities you want available.
4. Select Configure Current Project for Codex and read the status message.
5. Reconnect or restart the agent in that same project so it loads the generated project configuration.
The setup action writes the app-managed pztools server block in the project's .codex/config.toml while preserving other settings. Keep the matching project open in PZ Tools while using the connection.

## Capability choices
- Inspect and control the live editor permits the supported editor operations.
- Create and update project files controls project writes.
- Run Lua tests and full Project Health checks controls testing operations.
- Deploy the active project to the local Mods folder controls deployment and is disabled by default.
- Start, debug, stop, and inspect Project Zomboid controls supported game-session operations.
These are independent app permissions. Enabling game control does not automatically enable deployment. A debug launch that needs a refreshed copy can remain blocked until deployment is permitted and health passes.

## Change or diagnose access
Requests are checked against the live settings. After changing capabilities, run Configure Current Project for Codex again so the generated configuration also reflects your choices. Disable the server when you want the app integration unavailable.
If disconnected, check the active project, server switch, generated configuration status, and that the packaged mcp companion folder is present. If a particular action is denied, check its capability rather than enabling unrelated permissions.
This article covers PZ Tools' controls. Agent-client installation, authentication, and general usage belong in that client's own documentation.

# updates | App settings | Updates and About
PZ Tools can check its release feed and open an available release page. It does not silently replace the running application.

## Check for a release
Open File > App Options > Updates. Enable Check on startup if desired, select the update channel, and use Check now for an immediate check. Review Last check and the status message.
When an available update is found, the app asks before opening the release page. Follow the distribution's installation process after downloading; the check itself has not installed a new version.

## Keep the app files together
When using a packaged build, keep its companion mcp folder with the app so [Agent MCP](agent) can start its separate process. Preserve your project folders and app configuration when moving or replacing an installation.
Use Help > About PZTools for the app's About information. Help > PZ Wiki opens the existing external wiki link. Help > How to use reopens or focuses this manual.

## If a check fails
Read the status and check connectivity before retrying. A failed release check does not prevent reading the offline knowledgebase. See [storage](storage) before changing installation folders and [troubleshooting](troubleshooting) for reporting app issues.

# storage | App settings | Project storage and backups
Keep your working project, app settings, deployed copies, and isolated sessions distinct when moving or backing up work.

## Locate the working project
Use Project > Open Project Folder from an open project, or Open folder in the picker. Inspector's Full Path shows exactly which copy a selection belongs to. Imported projects live in the managed workspace rather than modifying the import source in place.
Back up the whole project folder, including hidden folders. .pztools contains per-project tool data such as Workshop settings and profiles; .codex and .vscode contain their respective integration configuration when generated.

## What is a copy
A local deployment is a testing copy of saved project content. Workshop upload creates a publication staging package. Playtest Lab prepares isolated caches and worlds. Generated decompiled source and its indexes are also separate from your authored project.
Do not rely on one of these copies as your only source backup. Edit the project, then use the relevant app action to refresh the destination.

## Recover or move work
Close tools that are writing to the project before taking a consistent backup. Restore to a separate location first if you need to compare old and current files. Use the app's import workflow when bringing an external project into the managed workspace.
App undo history is not a durable backup. Content-manager backup files help with recent managed saves but do not replace a full project history.
See [projects](projects), [deployment](deployment), and [version sync](version-sync).

# shortcuts | Reference | Keyboard shortcuts and menu guide
Use these shortcuts in the relevant PZ Tools window. A focused game or external editor may handle its own keys instead.

## Workspace shortcuts
- Ctrl+N: open New PZ Mod File.
- Ctrl+Shift+F: open Find in Project.
- Ctrl+F: find text in the current file preview.
- F3 / Shift+F3: next / previous preview match.
- Escape: close the preview find bar when it is active.
- Ctrl+Shift+F12: release focus from the docked game.

## Knowledgebase shortcuts
- Ctrl+F: focus Search all articles and select its current query.
- Alt+Left / Alt+Right: previous / next article in reading history.
- Ctrl+C: copy selected article text.
- Escape: close the knowledgebase window.
Use Tab to move between search, category, results, toolbar actions, and article links. The results list supports standard keyboard selection.

## Menu guide
File contains Project Settings, Close Project, Decompile Game Files, Upload To Steam Workshop, App Options, and Exit. Edit exposes Undo and Redo. Project contains file creation, search, health, tests, editor integration, and folder shortcuts.
View contains game-source browsing, Game Log, and Save Window Layout. File contains Lua watermark actions. Debug contains Run Game Settings and quick Lua tests. Help contains this manual, the external PZ Wiki link, and About.
For a task-oriented entry point, return to [Welcome](welcome).

# troubleshooting | Reference | Troubleshooting common app workflows
Start with the active project, exact path, selected profile, and most recent status message. These identify which stage failed.

## Files or changes seem missing
Use Inspector or Project > Open Project Folder to confirm the working copy. Save in the external editor. Check the correct target and remember that the preview is read-only. Imported source folders and deployed copies are different paths. Search status reports skipped or capped results; see [search](search).

## Health or deployment is blocked
Open Project > Health Dashboard, refresh, and address error diagnostics at their reported paths. Save fixes and refresh again. For stale-copy reports, redeploy the saved project. Warnings and errors have different effects; see [health](health) and [deployment](deployment).

## A profile will not launch
Check App Options' active installation mode and path, then the profile's Game build. Confirm save-source and dependency paths. For dedicated-server mode, check the launcher, port, readiness timeout, and SESSION OUTPUT. See [Playtest Lab](playtest) and [multiplayer](multiplayer).

## Sync did not update another target
Confirm that the item or its parent folder is covered by a saved rule. Inspect read-only attributes and conflict messages. Independently changed content is retained for manual reconciliation; use [version sync](version-sync).

## Decompilation or source search fails
Confirm the game path and Java availability. CFR may require a download. Wait for decompilation completion before searching. Choose the intended indexed build and broaden the filters or Rebuild index if needed. See [game source tools](game-source).

## Workshop upload is not confirmed
Distinguish health, package-review, login, and SteamCMD errors. Review logs and the saved Published File ID before retrying a first upload. See [Workshop upload](workshop-upload).

## An agent operation is unavailable
Keep the matching project open, enable the server, check the particular capability, and regenerate the project's configuration after changing options. Confirm the mcp companion is present. See [Agent MCP](agent).

## Report an app issue
Record the steps, app version, selected game build/profile, expected result, and actual result. Include the relevant status message and an exported health or test report if available. Mention whether the failure occurs before game launch, during a session, or during an external operation; see [logs and reports](logs).
