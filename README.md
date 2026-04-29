# GitHub Release Script Generator

A small Windows Forms app that generates `update.ps1` scripts for GitHub release based applications.

## Requirements

### Running the EXE

- Windows 10 / 11
- .NET 8 Desktop Runtime (x64)

Download:
https://dotnet.microsoft.com/download/dotnet/8.0

> The EXE will not start if the runtime is not installed.

### Building from source

- .NET 8 SDK

Verify:

```powershell
dotnet --version
```

## Fields

- **GitHub URL**: e.g. `https://github.com/cherryduck/GitHubReleaseScriptGenerator`
- **Install directory**: where the release asset will be installed and where `update.ps1` should live. Use the **Browse...** button to pick this folder.
- **Shortcut name**: the `.lnk` name.
- **Shortcut folder**: optional folder under Desktop. Leave blank for Desktop itself; use `Games` etc for custom folders.
- **Include pre-releases**: when checked, the generated script selects the newest non-draft release, including prereleases. When unchecked, it prefers GitHub's latest stable release endpoint and falls back to non-prerelease releases.
- **Run generated script after creation**: when checked, the generator starts the newly-created script with `powershell.exe -ExecutionPolicy Bypass -File`.

## Generated script asset behaviour

The generated script supports these GitHub release asset types:

- `.zip`
- `.7z`
- `.exe`

Archive assets are extracted into the install directory. If the archive contains a single top-level folder, that folder is flattened during installation.

Portable `.exe` assets are downloaded directly into the install directory using the release asset filename. The script then stores that EXE as the remembered launcher so future runs can start it directly.

Installer EXEs are not treated specially. This is intended for portable EXE release assets where the downloaded EXE is the final application.

## Generated script requirements

The generated `update.ps1` script requires:

- Windows PowerShell 5.1, built into Windows
- Internet access for GitHub API and asset downloads
- Optional: 7-Zip, only required for `.7z` archives

Expected path for 7-Zip:

```text
C:\Program Files\7-Zip\7z.exe
```

## Generated script shortcut behaviour

The generated script checks whether an existing shortcut already points to the generated updater script with the expected working directory. If it is valid, the script leaves the shortcut unchanged. It only creates or updates the shortcut when it is missing or invalid.

## Build the EXE

On Windows with the .NET SDK installed:

```powershell
.\build-windows-exe.ps1
```

The executable will be generated in:

```text
dist\
```
