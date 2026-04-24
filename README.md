# GitHub Release Script Generator

A small Windows Forms app that generates `update.ps1` scripts for GitHub release based applications.

## Fields

- **GitHub URL**: e.g. `https://github.com/cherryduck/GitHubReleaseScriptGenerator`
- **Install directory**: where the release archive will be extracted and where `update.ps1` should live. Use the **Browse...** button to pick this folder.
- **Shortcut name**: the `.lnk` name.
- **Shortcut folder**: optional folder under Desktop. Leave blank for Desktop itself; use `Games` or `Games\Emulators` for custom folders.
- **Include pre-releases**: when checked, the generated script selects the newest non-draft release, including prereleases. When unchecked, it prefers GitHub's latest stable release endpoint and falls back to non-prerelease releases.
- **Run generated script after creation**: when checked, the generator starts the newly-created script with `powershell.exe -ExecutionPolicy Bypass -File`.

## Generated script shortcut behaviour

The generated script now checks whether an existing shortcut already points to the generated updater script with the expected working directory. If it is valid, the script leaves the shortcut unchanged. It only creates or updates the shortcut when it is missing or invalid.

## Build the EXE

On Windows with the .NET SDK installed:

```powershell
.\build-windows-exe.ps1
```

The executable will be generated in:

```text
bin\Release\net8.0-windows\win-x64\publish
```
