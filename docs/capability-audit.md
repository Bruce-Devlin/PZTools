# PZTools capability audit

This pass reviews PZTools as a mod-development workspace. A claim that it covers every modder's needs would exceed the implemented and verified feature set.

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
