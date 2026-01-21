# PZTools
**Project Zomboid Tools – Modding, Build Management, and Developer Utilities**
<br>PZTools is a desktop utility designed to streamline Project Zomboid modding, development, and build management workflows. It provides a unified interface for managing multiple game installations, decompiling game source files, validating Lua scripts, and launching specific builds with optional debug or runtime arguments.
<br><br>This tool is primarily aimed at modders, reverse engineers, and developers who need tighter control over Project Zomboid environments than the base Steam installation provides.


## Key Features
- Manage multiple Project Zomboid builds (Steam-managed or custom)
- Launch specific game versions with custom JVM or debug arguments
- Automatic Java decompilation using the CFR decompiler
- Integrated Lua syntax testing
- Centralised project and configuration management
- Extensible WPF architecture with undoable command support
- Designed for long-term modding and tooling workflows

---
## Prerequisites
Before you start, ensure the following are installed:
- **Visual Studio 2026 Insiders**  
    - Make sure the **.NET desktop development** workload is installed.
- **.NET 10 SDK** (or latest compatible with Visual Studio 2026)
- **Git** for cloning the repository
- **Java (JDK)** if you intend to decompile Project Zomboid JARs
- Internet connection (for downloading CFR and SteamCMD)
---

## Cloning the Repository
```bash
git clone https://github.com/yourusername/PZTools.git
cd PZTools
```
---

## Opening the Project
1. Open Visual Studio 2026 Insiders.
2. Click File → Open → Project/Solution.
3. Navigate to the repository folder and select PZTools.sln.
4. Allow Visual Studio to restore NuGet packages and resolve dependencies.
---

## Project Structure
```
PZTools
├── Resources
│   └── Editor syntax highlighting definitions, icons, and embedded assets
│
├── Core
│   ├── Windows
│   │   ├── WPF windows and UI entry points
│   │   └── Dialogs
│   │       └── Project-specific dialogs
│   │
│   ├── Models
│   │   ├── Commands     (Undoable command models)
│   │   ├── Menu         (Menu and toolbar models)
│   │   ├── InputDialog  (Custom Input Dialog models)
│   │   ├── Test         (Testing-related models)
│   │   └── View         (View and layout models)
│   │
│   └── Functions
│       ├── Config       (Configuration handling)
│       ├── Decompile    (Java decompilation logic)
│       ├── Logger       (Centralised logging)
│       ├── InputDialog  (Functions for Custom Input Dialog)
│       ├── Menu         (Main menu behaviour)
│       ├── Projects     (Project and workspace management)
│       ├── Tester       (Lua syntax testing)
│       └── Zomboid      (Game installation and launch logic)
│       └── Undo         (Undoable command logic)
```
---

## Configuration
### Java Path
The application tries to detect java.exe automatically. You can also set the JAVA_HOME environment variable if needed.
### Project Zomboid Path
For an existing installation, point the app to your Zomboid folder.
For managed installations, PZTools will handle downloading and setting up multiple builds via SteamCMD.
### CFR Decompiler
CFR JAR is downloaded automatically to tools/cfr.jar if missing.
### Running PZTools
- Simply run and setup a release build .exe!
---

## Notes
- Managed installations require Steam credentials. SteamCMD will prompt for login in a console window.
- Decompiling large builds may take several minutes. The progress overlay will display the status.
- Always back up your save files when testing new builds.
---

## Troubleshooting
- Missing Java: Ensure java.exe is in your PATH or JAVA_HOME is set.
- SteamCMD errors: Make sure SteamCMD is installed and updated.
- Decompile fails: Check internet connectivity; CFR will be downloaded automatically.
- UI issues: Restart Visual Studio Insiders after first clone to restore all XAML designer features.
---

## Contributing
1. Fork the repository
2. Create a new branch
3. Make your changes
5. Open a Pull Request
---

## License
This project is licensed under the MIT License.