# Crewmate Manager

A Windows and Linux desktop manager for Among Us plugin files, built with **C#**, **.NET 8**, and **Avalonia UI**.

Browse user-added GitHub and GitLab release sources, choose a direct DLL asset, and install it into an existing `BepInEx/plugins` folder—with explicit confirmation.

> **Independent third-party project.** Not affiliated with or endorsed by Innersloth, Among Us, the listed mods, or their authors. Use only in private lobbies with every participant's informed consent. Do not use plugins to gain unfair advantages in public games.

![Crewmate Manager gallery](docs/gallery.png)

## Features

### Downloads

- Card-based menu library with search and user-added sources.
- Public GitHub and GitLab release API support.
- Separate release details, DLL selection, and release notes.
- Repository URLs follow latest-release rules; explicit release URLs stay pinned to their tag.
- Download progress, cancellation, and an installation confirmation prompt.

### Current Mod Menus

- Cards for local enabled and disabled plugin files.
- Enable or disable a plugin by renaming its file.
- Uninstall with a local backup and restore a selected backup.
- File inspection without loading or executing the DLL.
- Comparison of a tracked plugin's recorded release tag with its source.

### Advanced tools

- Capture, preview, apply, import, and export plugin state profiles.
- Compare local SHA-256 hashes with recorded installation metadata.
- Export an inventory, activity journal, source collection, or backup catalog.
- Basic workspace diagnostics.
- Advanced tools stay in collapsible sections instead of filling the main screen.

## Requirements

| Platform | Requirements |
| --- | --- |
| Windows | x64 Windows supported by .NET 8 and Avalonia; existing Among Us and BepInEx installation |
| Linux | x64 graphical desktop, Avalonia system dependencies, and an existing working Among Us/BepInEx installation under Wine or Proton |
| Building from source | [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and internet access for NuGet restore |

The manager **does not install BepInEx or configure Wine/Proton**. Windows plugin DLLs are not native Linux plugins. The Linux application manages files in your existing game installation.

Self-contained published packages do not require a separate .NET installation. Linux still needs the appropriate native desktop libraries, such as X11/fontconfig dependencies.

## Getting started

1. Close Among Us.
2. Open **Settings**.
3. Choose the existing **`BepInEx/plugins`** folder, then click **Save & validate**.
4. Open **Downloads** and choose a menu card.
5. Click **View & install** to fetch its release information.
6. Select one DLL asset, then click **Install selected DLL**.
7. Review the source, destination, and warnings before confirming.
8. Use **Current Mod Menus** to manage installed files.

The manager copies a selected file into the plugin directory. It does **not** inject code into a running process, bypass anti-cheat, or execute downloaded DLLs itself. BepInEx may load enabled plugins when you next start the game.

## Build from source

From the directory containing `CrewmateManager.csproj`:

```sh
dotnet restore
dotnet build -c Release
dotnet run --project CrewmateManager.csproj
```

### Publish a Windows x64 executable

```sh
dotnet publish CrewmateManager.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o publish/windows
```

Output: `publish/windows/CrewmateManager.exe`.

### Publish a Linux x64 executable

```sh
dotnet publish CrewmateManager.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o publish/linux
chmod +x publish/linux/CrewmateManager
./publish/linux/CrewmateManager
```

Windows and Linux use separate binaries. A single-file publish may extract bundled native libraries into a temporary directory at runtime.

The application is unsigned. An unsigned Windows executable may trigger reputation warnings. Do not disable security software to run it; review and build the source if you are unsure.

### GitHub Actions

A build workflow is included in `.github/workflows/build.yml`.

1. Upload the repository contents, including the `.github` directory.
2. Open **Actions → Build desktop apps → Run workflow**.
3. Download the Windows or Linux artifact from the completed run.

The workflow also runs when a tag matching `v*` is pushed. It uploads build artifacts; it does not automatically create a GitHub Release or sign executables. The workflow is provided as configuration and has not been run on GitHub during preparation of this source package.

## Project layout

```text
.
├── Program.cs                    # All application code; no separate XAML
├── CrewmateManager.csproj         # .NET and NuGet configuration
├── README.md
├── TEST-RESULTS.txt               # Recorded v0.4 render-check output
├── .gitignore
├── .github/
│   └── workflows/
│       └── build.yml
└── docs/
    └── gallery.png               # Actual headless-rendered application screenshot
```

All application code lives in one `Program.cs` file, including the UI, release API access, file operations, profiles, inspection, and journal. The project file is still required to build it. No external fonts, logos, or image assets are required by the application; card icons are monograms, not official mod logos.

## Release source behavior

- **GitHub repository URL:** latest stable release by default. Enable pre-releases in Settings to use the first published entry returned by the releases endpoint, which requests up to 30 entries.
- **GitLab repository URL:** most recently released entry by publication date.
- **Explicit release URL:** the specified tag, not the latest release.
- Only **direct DLL release assets** are supported. ZIP archives, CI artifacts, installers, and multi-file packages are not automatically installed.
- GitHub anonymous API rate limits apply. Authentication tokens and private repositories are not supported.

Ten user-supplied sources are preloaded. Their inclusion is not an endorsement. Availability, authorship, plugin safety, and game compatibility have not been verified.

The Community Collection source is pinned to:

```text
https://github.com/bilalmuslu321-stack/another-among-us-mod-menus/releases/tag/A
```

Its asset list is fetched at runtime; assets are selected individually, not installed as a bundle. To follow that repository's latest release instead, remove the pinned source and add its repository URL without the release path.

## Storage and backups

Settings, saved profiles, installation metadata, and `activity.json` are stored under the .NET `ApplicationData` location in a folder named `CrewmateManagerAvalonia`.

- On Windows, this is normally `%APPDATA%\CrewmateManagerAvalonia`.
- On Linux, the exact location follows the runtime's `ApplicationData` resolution.
- Plugin backups are stored in `BepInEx/plugins/.crewmate-backups`.
- Backups are not automatically deleted.
- The activity journal retains up to 1,000 entries.
- Exported reports may contain local paths. Review them before sharing.

Profiles contain file names, enabled/disabled states, and captured hashes—not plugin binaries. Applying a profile checks for missing or changed files before renaming them. Unlisted plugins remain unchanged. If a rename fails, completed renames are rolled back where possible; an interrupted operation or failed rollback may require manual inspection.

## Security and limitations

**Downloaded plugins execute code inside the game. Only install files from sources you independently trust.** Hosting on GitHub or GitLab does not establish safety.

The download path includes HTTPS, a 100 MB limit, temporary files, a basic MZ-header check, SHA-256 recording, and comparison with an API-provided SHA-256 digest when available. The inspector can also read PE headers, machine type, and the DLL flag without loading the file.

These checks are **not malware detection, signature verification, publisher authentication, or a compatibility assessment**. Matching a recorded hash only shows that the bytes match the recorded value.

Other limitations:

- The game is not automatically detected or stopped. Close it before changing files.
- Multiple menus can conflict; the manager does not resolve mod dependencies or conflicts.
- Local scanning covers the top level of `plugins`, not nested plugin directories.
- Updating a differently named asset does not automatically remove an older file.
- Tracked release comparisons use exact tag equality, not semantic version ordering.
- Profile hashing has a 512 MB per-file limit. Synchronous preview hashing can briefly block the UI on large collections.
- Profile capture and preview do not use the cancellable download/integrity-check queue.
- Symbolic-link plugin files are unsupported; profile operations also reject linked folder paths.

## Validation status

For v0.4:

- Release build completed with **0 errors and 0 warnings**.
- Windows x64 and Linux x64 self-contained publishes were produced in a Linux environment.
- The Downloads gallery, empty collection page, and Settings page were rendered with **Avalonia.Headless + Skia**. All three render checks passed.
- The screenshot in this README is an actual 1240 × 860 application render, not an HTML mockup. Page transition animations were disabled for capture.

The preceding v0.3 implementation passed 27 checks covering profile/hash/journal behavior and page construction. That entire suite was **not rerun for v0.4**. `TEST-RESULTS.txt` contains the recorded v0.4 render-check output; the test harness itself is not included in this repository package.

Windows execution, native file pickers, visible desktop animations, release-detail dialogs, and real-game download/install behavior have **not** been tested end to end. This is an initial, unsigned implementation, not a security-audited release. Back up your plugin directory before testing.

## License

No license has been selected for this source package. Public availability is not a grant of reuse rights. Before publishing, choose a license appropriate to the rights you hold and add its text in a `LICENSE` file. Do not assume this project grants rights to any third-party mods or downloaded plugin binaries.
