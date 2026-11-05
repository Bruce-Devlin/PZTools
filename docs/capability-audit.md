# PZTools capability audit

This pass reviews PZTools as a mod-development workspace. A claim that it covers every modder's needs would exceed the implemented and verified feature set.

## Session and evidence improvements — 5 November 2026

The implementation and workflows are described in [Mod tests and isolated playtests](testing.md). This update supersedes the older session, deployment and test-evidence descriptions below.

| Requested improvement | Implementation and evidence |
| --- | --- |
| Protect manual worlds | Retained by default, including old disposable profiles; process ownership checked before launch; a real crashed-process regression preserves the workspace. |
| Persistent history | Shared durable registry for manual, debug and automated sessions; process start times, operation/report IDs and artifact paths; interrupted owners reconciled without replay. |
| Unified evidence | Captured source used for execution, changed-file comparison, profile and endpoints, conditions, separate verdict/operation/errors and checkpointed reports in Test Explorer. |
| Observable fixtures | Living-player, loaded-square, server-confirmed co-location and native-walk prerequisites; Setup/Exercise/Cleanup reports; native unloaded-square failure prevents exercise. |
| Launch preflight | Exact installation paths, Steam builds, selected profile and source world; discovered replacements and installation settings route; fixture file and profile validation. |
| Save/restart | Separate termination and native save/shutdown actions; save receipts and clean exits; persisted marker asserted after a new client process starts. |
| Failure diagnostics | Assertion, phase, endpoint, log excerpt and owned-window image; incremental per-endpoint MCP logs; startup diagnostics when no case begins. |
| Reference provenance | Binary fingerprint and originating build; visible unknown/mismatched origins; public Java declarations distinguished from matching native probe observations. |
| Native Lua validation | Fast MoonSharp checks retained; installed JVM/Kahlua compilation with build and scope labels; native API probe scaffold. |
| Deployment recovery | Private staging beside scanned mod directories; copy/verify/replace/cleanup receipts, retained failure artifacts and possible lock owners; dedicated-server load/save/exit acceptance passed. |
| Source storage | Durable user-selected project location, build-output and Git coverage warnings, reviewed source copy preserving the original, explicit Git initialization. |

Native acceptance uses cloned fixtures and isolated caches. The single-player acceptance covers a real save, clean shutdown, new process, persisted marker, API observation and a deliberate setup failure with its diagnostic screenshot. The dedicated-server acceptance covers deployed payload verification, native mod loading, save completion and clean exit. These results apply to the installed Build 42.21.0 / Steam build 25485521 used on this date.

Two-client acceptance loaded both cloned worlds after fixing long cache paths and preserving the server ResetID, but Windows denied foreground focus at Click to Start. The readiness barrier stayed closed and the run retained its artifacts. Server-confirmed co-location and multiplayer save/restart are therefore not yet live-accepted. Dark/light WPF renders establish layout; the unavailable desktop helper prevents claiming a complete manual mouse/keyboard walkthrough.

The opt-in tests are `LiveServerAcceptance`, `LivePersistenceAcceptance` and `LiveMultiplayerAcceptance` in `Tests/PZTools.Testing.Tests`. Set `PZTOOLS_LIVE_GAME_ROOT` and the applicable `PZTOOLS_LIVE_SOURCE_SAVE` or `PZTOOLS_LIVE_SOURCE_SESSION` to run them. They are explicitly skipped in the normal regression suite. Native artifacts remain under `%TEMP%/PZTools*Acceptance`, linked from durable session history; new worlds remain under `%LOCALAPPDATA%/PZTools/worlds`.

Verification: solution build passed with no warnings or errors; 91 regression tests passed and three opt-in live tests skipped; Runner Setup UI, Playtest and Knowledge smoke checks passed. The native persistence check passed in 2m34s and the final dedicated-server check passed in 44s. The failed two-client acceptance remains a failed result, separate from those passes. Captures are retained under `.verify/runner-setup` and each native report's artifact directory.

| Workflow | Current implementation | Remaining boundary |
| --- | --- | --- |
| Create/import and target builds | Build targets, shared content, scaffolds, metadata and version sync | Test each target against its matching installed game |
| Code authoring | External editor integration, generated test/deploy tasks, read-only previews and search | No built-in editable multi-document IDE, language server, breakpoint debugger or refactoring engine |
| Navigate tools | Searchable command palette, project explorer, offline manual | Existing commands retain their individual windows and lifecycle |
| Define content | Professions, traits, sandbox options, localization, Lua/item/recipe scaffolds | Visual editors do not cover every game script schema or mod API |
| Inspect vanilla content | Installed-build content references and recovered Java knowledge base | Source visibility does not prove Lua exposure; content schemas vary by build |
| Validate and test | Health diagnostics with filters, Lua/unit/game-test tooling, reports | Static checks and unit tests do not prove game registration or runtime behavior |
| Deploy | Staged replacement, manifests, overlap/link rejection, internal-folder exclusion | Cross-process writers are not coordinated; external file changes can race inspection |
| Single-player / multiplayer | Isolated caches, saved profiles, dependencies, load order and parity checks | Real client/server runs and game-renderer docking still need live acceptance |
| Publish | Workshop package review and SteamCMD upload | Authenticated upload/retry acceptance requires Steam |
| Maps and tiles | File management and installed map references | No embedded WorldEd, TileZed or BuildingEd authoring pipeline |
| Models, animation, textures and audio | File organization, supported image previews, installed-content references | No DCC replacement, asset conversion pipeline, mesh/animation editor, sound-bank editor or texture-pack compiler |
| Collaboration | External editor/Git-friendly project files and Agent MCP | No built-in Git UI or merge tooling |

## Changes in this pass

- Ctrl+Shift+P command palette built from the existing menu registry, with keyboard navigation and empty results feedback.
- Health filters, keyboard navigation to diagnostic files, and protection against a cancelled refresh replacing a newer report.
- Game content reference browser supporting separate managed build folders; the previous Lua-source action incorrectly assumed the managed installation root directly contained media.
- Deployment overlap and link checks, unique temporary directories, serialization of in-process deployments, failed staging cleanup, and exclusion of agent configuration.
- Manifest validation for malformed entries and detection of unexpected deployed payload files. Source verification prunes excluded cache folders before descending into them.

## Acceptance

Automated tests use disposable projects and test payloads. Desktop captures use WPF test windows. They do not establish live Project Zomboid, native game rendering, authenticated Workshop publication, or correctness of every game API. Keep those acceptance results separate from build and smoke results.

Results for this pass:

- Solution build: passed, zero warnings and errors.
- xUnit: 27 passed, including deployment regression tests and installed-build discovery.
- Core smoke: passed (project/import, content, validation, deployment, editor/MCP configuration, version sync and source indexing).
- Playtest smoke: passed (dependency order, isolated caches, server configuration and payload parity).
- Desktop smoke: passed in both themes, including 900×640 workspace layout, command search, health filters and content-reference availability. Native window docking uses a test window, not the game renderer.
- Help smoke: 38 articles passed catalog, navigation and rendering checks.
- Changed solution C# files passed the whitespace formatter check; Git diff whitespace check passed.
- Visual captures: `.verify/astra-pass/` (local, ignored by Git).
- Live game sessions and Steam upload: not performed.

## Next substantial product work

1. Validate the complete create → edit → health → deploy → launch → diagnose loop against representative installed builds, then run the dedicated-server variant with two clients.
2. Add a configured external-tool workflow for mapping and asset tools, with inputs/outputs and build compatibility recorded in the project.
3. Decide whether PZTools remains an external-editor companion or becomes an editable IDE before adding document editing, language services and debugging.
4. Expand content editors based on actual vanilla schemas and representative mods, with per-build runtime fixtures.

The Indie Stone describes the versioned mod architecture and specialist modding tools in [Tidy Up Time](https://projectzomboid.com/blog/news/2024/08/tidy-up-time/). That is architectural context, not certification of current game compatibility.
