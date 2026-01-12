# PZTools
## **Project Zomboid Tools – Modding and Game Utilities**
### This repository contains the PZTools application, a utility for managing Project Zomboid installations, builds, decompiling source files, and launching specific game versions with optional debug arguments.
---
## Prerequisites
Before you start, ensure the following are installed:
- **Visual Studio 2026 Insiders**  
  Make sure the **.NET desktop development** workload is installed.
- **.NET 8 SDK** (or latest compatible with Visual Studio 2026)
- **Git** for cloning the repository
- **Java (JDK)** if you intend to decompile Project Zomboid JARs
- **SteamCMD** (for managed game installations)
- Internet connection (for downloading CFR and SteamCMD)
---

## Cloning the Repository
```bash
git clone https://github.com/yourusername/PZTools.git
cd PZTools
```
---

## Opening the Project in Visual Studio Insiders 2026
1. Open Visual Studio 2026 Insiders.
2. Click File → Open → Project/Solution.
3. Navigate to the repository folder and select PZTools.sln.
4. Allow Visual Studio to restore NuGet packages and resolve dependencies.
---

## Project Structure
- **Resources** – Embedded resources like icons and images, or in our case the syntax highlighting definitions for the code editor.
- **Core** – Main application logic.
	- **Windows** – WPF UI components and dialogs.
		- **Dialogs** – WPF UI dialogs.
			- **Project** – WPF UI Project spesific dialogs.
	- **Models** – Application logic models.
		- **Commands** – Commands logic models.
		- **Menu** – Menu logic models.
		- **Test** – Test logic models.
		- **View** – View logic models.
	- **Functions** – Application logic functions.
		- **Config** – Configuration functions.
		- **Decompile** – Java decompilation functions.
		- **Logger** – Application logging functions.
		- **Menu** – Main window toolbar menu functions.
		- **Projects** – Project Zomboid Mod/Project management functions.
		- **Tester** – Lua testing functions.
		- **Zomboid** – Project Zomboid management functions.
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
- Launch the application via Visual Studio (F5 for Debug or Ctrl+F5 for Release).
- Select installation options for PZTools and Project Zomboid.
- Use the RunProject window to launch specific builds, set optional arguments, or debug.
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