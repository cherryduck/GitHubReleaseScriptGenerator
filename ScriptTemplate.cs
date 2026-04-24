using System.Text;

namespace GitHubReleaseScriptGenerator;

public static class ScriptTemplate
{
    public static string Build(string targetDirectory, string owner, string repo, string shortcutName, string shortcutRelativeFolder, bool includePrereleases)
    {
        static string Ps(string value) => value.Replace("`", "``").Replace("$", "`$").Replace("\"", "`\"");
        var include = includePrereleases ? "$true" : "$false";

        return $$$"""
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

# -------------------------------
# Config (filled in by generator)
# -------------------------------
$targetDirectory       = "{{{Ps(targetDirectory)}}}"
$owner                 = "{{{Ps(owner)}}}"
$repo                  = "{{{Ps(repo)}}}"
$shortcutName          = "{{{Ps(shortcutName)}}}"
$includePrereleases    = {{{include}}}
$shortcutRelativeFolder = "{{{Ps(shortcutRelativeFolder)}}}" # Relative to Desktop. Blank means Desktop itself.

Set-Location -LiteralPath $targetDirectory

# State files
$versionFilePath        = "current-version.txt"
$assetPrefFilePath      = "asset-preference.txt"
$launcherPrefFilePath   = "launcher-preference.txt"

$desktopRoot = [Environment]::GetFolderPath("Desktop")
if ([string]::IsNullOrWhiteSpace($shortcutRelativeFolder)) {
    $shortcutRoot = $desktopRoot
} else {
    $cleanShortcutRelativeFolder = $shortcutRelativeFolder.Trim().TrimStart("\", "/")
    $shortcutRoot = Join-Path $desktopRoot $cleanShortcutRelativeFolder
}

# Icon override state
$iconPrefFilePath        = "icon-preference.txt"
$iconOverrideUrlFilePath = "icon-url.txt"
$iconLocalFileBase       = "shortcut-icon"

function Log {
    param([Parameter(Mandatory)][string]$Message)
    $ts = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
    Write-Host "[$ts] $Message"
}

function Parse-ToUtcDateTime {
    param([Parameter(Mandatory)]$Value)

    if ($Value -is [DateTime]) {
        if ($Value.Kind -eq [DateTimeKind]::Unspecified) {
            return [DateTime]::SpecifyKind($Value, [DateTimeKind]::Utc)
        }
        return $Value.ToUniversalTime()
    }

    $s = [string]$Value
    if ([string]::IsNullOrWhiteSpace($s)) { throw "Empty datetime value." }

    $styles = [System.Globalization.DateTimeStyles]::AssumeUniversal `
            -bor [System.Globalization.DateTimeStyles]::AdjustToUniversal

    $cultInv = [System.Globalization.CultureInfo]::InvariantCulture
    $cultCur = [System.Globalization.CultureInfo]::CurrentCulture

    try { return [DateTime]::ParseExact($s, "o", $cultInv, $styles) } catch {}
    try { return [DateTime]::Parse($s, $cultInv, $styles) } catch {}
    try { return [DateTime]::Parse($s, $cultCur, $styles) } catch {}

    throw "Could not parse datetime: '$s'"
}

# -------------------------------
# Shortcut Helper
# -------------------------------
function Test-ShortcutIsValid {
    param(
        [Parameter(Mandatory)][string]$ShortcutPath,
        [Parameter(Mandatory)][string]$ExpectedTargetPath,
        [Parameter(Mandatory)][string]$ExpectedArguments,
        [Parameter(Mandatory)][string]$ExpectedWorkingDirectory
    )

    if (-not (Test-Path -LiteralPath $ShortcutPath)) { return $false }

    try {
        $wsh = New-Object -ComObject WScript.Shell
        $s = $wsh.CreateShortcut($ShortcutPath)

        $targetMatches = ([string]$s.TargetPath).Trim() -ieq $ExpectedTargetPath
        $argsMatches = ([string]$s.Arguments).Trim() -eq $ExpectedArguments
        $workingDirMatches = ([string]$s.WorkingDirectory).TrimEnd([char[]]@('\', '/')) -ieq $ExpectedWorkingDirectory.TrimEnd([char[]]@('\', '/'))

        return ($targetMatches -and $argsMatches -and $workingDirMatches)
    }
    catch {
        Log "Existing shortcut could not be inspected: $($_.Exception.Message)"
        return $false
    }
}

function Ensure-Shortcut {
    param([Parameter(Mandatory)][string]$IconPath)

    $powershellExe = "C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe"
    $args = "-ExecutionPolicy Bypass -File `"$targetDirectory\update.ps1`""

    if (-not (Test-Path -LiteralPath $shortcutRoot)) {
        Log "Shortcut root does not exist; creating: $shortcutRoot"
        New-Item -ItemType Directory -Path $shortcutRoot | Out-Null
    }

    $lnkName = "$shortcutName.lnk"
    $matches = @(Get-ChildItem -LiteralPath $shortcutRoot -Recurse -File -Filter $lnkName -ErrorAction SilentlyContinue)

    $shortcutPath = $null
    if ($matches.Count -eq 1) {
        $shortcutPath = $matches[0].FullName
        Log "Found existing shortcut: $shortcutPath"
    } elseif ($matches.Count -gt 1) {
        Log "Multiple shortcuts named '$lnkName' found under $shortcutRoot; using the first valid match if possible."

        foreach ($match in $matches) {
            if (Test-ShortcutIsValid -ShortcutPath $match.FullName -ExpectedTargetPath $powershellExe -ExpectedArguments $args -ExpectedWorkingDirectory $targetDirectory) {
                Log "Found valid existing shortcut: $($match.FullName)"
                Log "Shortcut already valid; leaving it unchanged."
                return
            }
        }

        $shortcutPath = $matches[0].FullName
        Log "No valid shortcut found among duplicates; chosen for update: $shortcutPath"
    }

    if ($shortcutPath) {
        if (Test-ShortcutIsValid -ShortcutPath $shortcutPath -ExpectedTargetPath $powershellExe -ExpectedArguments $args -ExpectedWorkingDirectory $targetDirectory) {
            Log "Shortcut already valid; leaving it unchanged: $shortcutPath"
            return
        }

        Log "Existing shortcut is missing or does not point to this updater; updating: $shortcutPath"
    }
    else {
        $shortcutPath = Join-Path $shortcutRoot $lnkName
        Log "No existing shortcut found under $shortcutRoot."
        Log "Creating new shortcut at: $shortcutPath"
    }

    $wsh = New-Object -ComObject WScript.Shell
    $s = $wsh.CreateShortcut($shortcutPath)
    $s.TargetPath = $powershellExe
    $s.Arguments  = $args
    $s.WorkingDirectory = $targetDirectory
    $s.IconLocation = "$IconPath,0"
    $s.Save()

    Log "Shortcut created/updated: $shortcutPath"
    Log "Shortcut icon set to: $IconPath (index 0)"
}

# -------------------------------
# Release fetching, with optional prereleases
# -------------------------------
function Get-LatestReleaseRobust {
    param(
        [Parameter(Mandatory)][string]$Owner,
        [Parameter(Mandatory)][string]$Repo,
        [Parameter(Mandatory)]$Headers,
        [Parameter(Mandatory)][bool]$IncludePrereleases
    )

    if ($IncludePrereleases) {
        $listUrl = "https://api.github.com/repos/$Owner/$Repo/releases?per_page=30"
        Log "API (list, prereleases included): $listUrl"
        $releases = Invoke-RestMethod -Uri $listUrl -Headers $Headers -Method Get
        $candidates = @($releases | Where-Object { -not $_.draft })
        if ($candidates.Count -eq 0) { throw "No non-draft releases found for $Owner/$Repo." }

        return $candidates | Sort-Object `
            @{ Expression = { Parse-ToUtcDateTime $_.published_at }; Descending = $true }, `
            @{ Expression = { Parse-ToUtcDateTime $_.created_at   }; Descending = $true } |
            Select-Object -First 1
    }

    $latestUrl = "https://api.github.com/repos/$Owner/$Repo/releases/latest"
    Log "API (latest stable): $latestUrl"

    try {
        return Invoke-RestMethod -Uri $latestUrl -Headers $Headers -Method Get
    }
    catch {
        $resp = $_.Exception.Response
        if ($resp -and $resp.StatusCode.value__ -eq 404) {
            Log "Latest release endpoint returned 404. Falling back to listing stable releases..."
            $listUrl = "https://api.github.com/repos/$Owner/$Repo/releases?per_page=30"
            Log "API (list):   $listUrl"
            $releases = Invoke-RestMethod -Uri $listUrl -Headers $Headers -Method Get
            $candidates = @($releases | Where-Object { -not $_.draft -and -not $_.prerelease })
            if ($candidates.Count -eq 0) { throw "No stable non-draft releases found for $Owner/$Repo." }

            return $candidates | Sort-Object `
                @{ Expression = { Parse-ToUtcDateTime $_.published_at }; Descending = $true }, `
                @{ Expression = { Parse-ToUtcDateTime $_.created_at   }; Descending = $true } |
                Select-Object -First 1
        }
        throw
    }
}

# -------------------------------
# Asset Selection (Windows archives)
# -------------------------------
function Select-WindowsAsset {
    param([Parameter(Mandatory)]$release)

    $assets = @($release.assets | Where-Object {
        $_.name -match '(?i)\.(zip|7z)$' -and
        $_.name -notmatch '(?i)source code'
    })

    if ($assets.Count -eq 0) {
        throw "No downloadable .zip/.7z assets found on the selected release."
    }

    if (Test-Path -LiteralPath $assetPrefFilePath) {
        $pref = Get-Content -LiteralPath $assetPrefFilePath -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($pref) {
            $match = $assets | Where-Object { $_.name -eq $pref } | Select-Object -First 1
            if ($match) {
                Log "Using remembered asset: $($match.name)"
                return $match
            }
        }
    }

    $windowsAssets = @($assets | Where-Object { $_.name -match '(?i)(win|windows|x64|x86)' })

    if ($windowsAssets.Count -eq 1) {
        Log "Auto-selected Windows asset: $($windowsAssets[0].name)"
        return $windowsAssets[0]
    }

    if ($windowsAssets.Count -gt 1) {
        Log "Multiple Windows assets found:"
        for ($i=0; $i -lt $windowsAssets.Count; $i++) {
            Write-Host "[$i] $($windowsAssets[$i].name)"
        }

        do {
            $choice = Read-Host "Choose one (0-$($windowsAssets.Count-1))"
        } while ($choice -notmatch '^\d+$' -or [int]$choice -lt 0 -or [int]$choice -ge $windowsAssets.Count)

        $selected = $windowsAssets[[int]$choice]
        Set-Content -LiteralPath $assetPrefFilePath -Value $selected.name
        Log "Selected asset saved to ${assetPrefFilePath}: $($selected.name)"
        return $selected
    }

    Log "No clear Windows asset; using first archive asset: $($assets[0].name)"
    return $assets[0]
}

# -------------------------------
# Launcher selection (EXE/BAT/CMD) with memory
# -------------------------------
function Select-Launcher {
    param([Parameter(Mandatory)][string]$Root)

    if (Test-Path -LiteralPath $launcherPrefFilePath) {
        $pref = Get-Content -LiteralPath $launcherPrefFilePath -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($pref -and (Test-Path -LiteralPath $pref)) {
            Log "Using remembered launcher: $pref"
            return Get-Item -LiteralPath $pref
        }
    }

    $candidates = @(Get-ChildItem -LiteralPath $Root -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -match '^(?i)\.(exe|bat|cmd)$' })

    if ($candidates.Count -eq 0) { return $null }

    if ($candidates.Count -eq 1) {
        Set-Content -LiteralPath $launcherPrefFilePath -Value $candidates[0].FullName
        Log "Only one launcher found; saved: $($candidates[0].FullName)"
        return $candidates[0]
    }

    Log "Multiple launch candidates found:"
    for ($i=0; $i -lt $candidates.Count; $i++) {
        Write-Host "[$i] $($candidates[$i].FullName)"
    }

    do {
        $choice = Read-Host "Choose launcher (0-$($candidates.Count-1))"
    } while ($choice -notmatch '^\d+$' -or [int]$choice -lt 0 -or [int]$choice -ge $candidates.Count)

    $selected = $candidates[[int]$choice]
    Set-Content -LiteralPath $launcherPrefFilePath -Value $selected.FullName
    Log "Launcher saved to ${launcherPrefFilePath}: $($selected.FullName)"
    return $selected
}

function Start-Launcher {
    param([Parameter(Mandatory)]$LauncherItem)

    $path = $LauncherItem.FullName
    $wd   = Split-Path -Parent $path
    $ext  = $LauncherItem.Extension.ToLowerInvariant()

    if ($ext -eq ".exe") {
        Log "Starting EXE: $path"
        Start-Process -FilePath $path -WorkingDirectory $wd
        return
    }

    if ($ext -eq ".bat" -or $ext -eq ".cmd") {
        Log "Starting script via cmd.exe: $path"
        Start-Process -FilePath "$env:WINDIR\System32\cmd.exe" -ArgumentList "/c `"$path`"" -WorkingDirectory $wd
        return
    }

    throw "Unsupported launcher extension: $ext"
}

# -------------------------------
# Icon selection: propose + confirm; allow URL override
# -------------------------------
function Download-IconFromUrl {
    param([Parameter(Mandatory)][string]$Url)

    $uri = [Uri]$Url
    $ext = [IO.Path]::GetExtension($uri.AbsolutePath).ToLowerInvariant()
    if ($ext -notin @(".ico", ".png", ".jpg", ".jpeg")) {
        throw "Icon URL must end with .ico, .png, .jpg, or .jpeg (got '$ext')."
    }

    $dest = Join-Path $targetDirectory ($iconLocalFileBase + $ext)
    Log "Downloading icon to: $dest"
    Invoke-WebRequest -Uri $Url -OutFile $dest -Headers @{ "User-Agent" = "UpdaterScript" }
    return $dest
}

function Convert-ImageToIco {
    param([Parameter(Mandatory)][string]$ImagePath)

    $ext = [IO.Path]::GetExtension($ImagePath).ToLowerInvariant()
    if ($ext -eq ".ico") { return $ImagePath }

    $icoPath = Join-Path $targetDirectory ($iconLocalFileBase + ".ico")

    try { Add-Type -AssemblyName System.Drawing } catch {
        Log "System.Drawing not available; cannot convert $ext to .ico. Provide an .ico URL instead."
        return $null
    }

    try {
        $bmp = [System.Drawing.Bitmap]::FromFile($ImagePath)
        $size = New-Object System.Drawing.Size(256, 256)
        $resized = New-Object System.Drawing.Bitmap($bmp, $size)

        $icon = [System.Drawing.Icon]::FromHandle($resized.GetHicon())
        $fs = New-Object System.IO.FileStream($icoPath, [System.IO.FileMode]::Create)
        try { $icon.Save($fs) } finally { $fs.Close() }

        Log "Converted image to icon: $icoPath"
        return $icoPath
    } catch {
        Log "Failed to convert image to .ico: $($_.Exception.Message)"
        return $null
    } finally {
        if ($bmp) { $bmp.Dispose() }
        if ($resized) { $resized.Dispose() }
    }
}

function Get-IconCandidates {
    $excludePathRegex = '(?i)\\(jdk|jre|runtime|bin|tools|tool|sdk|redist)\\'

    $top = @(Get-ChildItem -LiteralPath $targetDirectory -Filter *.exe -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch $excludePathRegex } |
        Sort-Object Length -Descending)

    $rec = @(Get-ChildItem -LiteralPath $targetDirectory -Filter *.exe -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch $excludePathRegex } |
        Sort-Object Length -Descending)

    $recAny = @(Get-ChildItem -LiteralPath $targetDirectory -Filter *.exe -File -Recurse -ErrorAction SilentlyContinue |
        Sort-Object Length -Descending)

    $localIco  = Join-Path $targetDirectory ($iconLocalFileBase + ".ico")
    $localPng  = Join-Path $targetDirectory ($iconLocalFileBase + ".png")
    $localJpg  = Join-Path $targetDirectory ($iconLocalFileBase + ".jpg")
    $localJpeg = Join-Path $targetDirectory ($iconLocalFileBase + ".jpeg")

    $icons = @()
    if (Test-Path -LiteralPath $localIco)  { $icons += (Get-Item -LiteralPath $localIco) }
    if (Test-Path -LiteralPath $localPng)  { $icons += (Get-Item -LiteralPath $localPng) }
    if (Test-Path -LiteralPath $localJpg)  { $icons += (Get-Item -LiteralPath $localJpg) }
    if (Test-Path -LiteralPath $localJpeg) { $icons += (Get-Item -LiteralPath $localJpeg) }

    $seen = @{}
    $candidates = @()
    foreach ($item in @($top + $rec + $icons + $recAny)) {
        if ($item -and -not $seen.ContainsKey($item.FullName)) {
            $seen[$item.FullName] = $true
            $candidates += $item
        }
    }

    return $candidates
}

function Resolve-IconPathFromItem {
    param([Parameter(Mandatory)]$Item)

    $ext = $Item.Extension.ToLowerInvariant()

    if ($ext -eq ".ico") { return $Item.FullName }
    if ($ext -in @(".png", ".jpg", ".jpeg")) { return Convert-ImageToIco -ImagePath $Item.FullName }
    if ($ext -eq ".exe") { return $Item.FullName }

    return $null
}

function Choose-IconInteractively {
    param([Parameter(Mandatory)]$LauncherItem)

    if (Test-Path -LiteralPath $iconPrefFilePath) {
        $pref = Get-Content -LiteralPath $iconPrefFilePath -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($pref -and (Test-Path -LiteralPath $pref)) {
            Log "Using remembered icon: $pref"
            return $pref
        }
    }

    $candidates = @(Get-IconCandidates)
    $proposed = $null

    foreach ($cand in $candidates) {
        $resolved = Resolve-IconPathFromItem -Item $cand
        if ($resolved) { $proposed = $resolved; break }
    }

    if ($proposed) {
        Log "Proposed icon source: $proposed"
        $ans = Read-Host "Use this icon? (Y/N)"
        if ($ans -match '^(Y|y)$') {
            Set-Content -LiteralPath $iconPrefFilePath -Value $proposed
            return $proposed
        }
        Log "Declined proposed icon."
    } else {
        Log "No local icon candidates found."
    }

    $iconUrl = $null
    if (Test-Path -LiteralPath $iconOverrideUrlFilePath) {
        $iconUrl = Get-Content -LiteralPath $iconOverrideUrlFilePath -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not [string]::IsNullOrWhiteSpace($iconUrl)) {
            Log "Using icon URL from ${iconOverrideUrlFilePath}: $iconUrl"
        }
    }

    if ([string]::IsNullOrWhiteSpace($iconUrl)) {
        $iconUrl = Read-Host "Enter icon URL (.ico/.png/.jpg/.jpeg) or press Enter to use cmd.exe icon"
        if (-not [string]::IsNullOrWhiteSpace($iconUrl)) {
            Set-Content -LiteralPath $iconOverrideUrlFilePath -Value $iconUrl
            Log "Saved icon URL to ${iconOverrideUrlFilePath}"
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($iconUrl)) {
        $downloaded = Download-IconFromUrl -Url $iconUrl
        $icoPath = Convert-ImageToIco -ImagePath $downloaded
        if (-not $icoPath -and ([IO.Path]::GetExtension($downloaded).ToLowerInvariant() -eq ".ico")) {
            $icoPath = $downloaded
        }

        if ($icoPath) {
            Set-Content -LiteralPath $iconPrefFilePath -Value $icoPath
            Log "Icon chosen from URL and saved: $icoPath"
            return $icoPath
        }

        Log "Could not use downloaded icon; falling back."
    }

    Log "No usable icon selected; falling back to cmd.exe"
    return "$env:WINDIR\System32\cmd.exe"
}

# -------------------------------
# Extraction Helper
# -------------------------------
function Extract-ReleaseArchive {
    param(
        [Parameter(Mandatory)][string]$ArchivePath,
        [Parameter(Mandatory)][string]$DestinationDir
    )

    $tempExtract = Join-Path $DestinationDir (".extract-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
    New-Item -ItemType Directory -Path $tempExtract | Out-Null

    $ext = [IO.Path]::GetExtension($ArchivePath).ToLowerInvariant()
    Log "Extracting to temp folder: $tempExtract"

    try {
        if ($ext -eq ".zip") {
            Expand-Archive -LiteralPath $ArchivePath -DestinationPath $tempExtract -Force
        } else {
            $sevenZip = Join-Path $env:ProgramFiles "7-Zip\7z.exe"
            if (-not (Test-Path -LiteralPath $sevenZip)) {
                throw "Archive is $ext but 7-Zip not found at $sevenZip. Install 7-Zip or choose a .zip asset."
            }
            & $sevenZip x $ArchivePath "-o$tempExtract" -y | Out-Null
        }

        $top = @(Get-ChildItem -LiteralPath $tempExtract -Force)
        if ($top.Count -eq 1 -and $top[0].PSIsContainer) {
            $mergeRoot = $top[0].FullName
            Log "Archive has a single top-level folder '$($top[0].Name)'; flattening during merge."
        } else {
            $mergeRoot = $tempExtract
            Log "Archive has multiple top-level items; merging as-is."
        }

        $items = @(Get-ChildItem -LiteralPath $mergeRoot -Force)
        foreach ($item in $items) {
            $destPath = Join-Path $DestinationDir $item.Name

            if (Test-Path -LiteralPath $destPath) {
                Remove-Item -LiteralPath $destPath -Recurse -Force -ErrorAction SilentlyContinue
            }

            Move-Item -LiteralPath $item.FullName -Destination $destPath -Force
        }

        Log "Merge into install directory complete."
    }
    finally {
        Remove-Item -LiteralPath $tempExtract -Recurse -Force -ErrorAction SilentlyContinue
        Log "Temp extract folder removed."
    }
}

# -------------------------------
# Version Tracking + Update
# -------------------------------
if (-not (Test-Path -LiteralPath $versionFilePath)) {
    Log "Version file missing; creating: $versionFilePath"
    Set-Content -LiteralPath $versionFilePath -Value "1970-01-01T00:00:00Z"
}

Log "Checking latest release..."
Log "Prereleases included: $includePrereleases"

$headers = @{
    "User-Agent" = "UpdaterScript"
    "Accept"     = "application/vnd.github+json"
}

$latestRelease = Get-LatestReleaseRobust -Owner $owner -Repo $repo -Headers $headers -IncludePrereleases $includePrereleases

$currentVersionRaw = Get-Content -LiteralPath $versionFilePath
$current = Parse-ToUtcDateTime $currentVersionRaw
$latest  = Parse-ToUtcDateTime $latestRelease.published_at

Log "Installed: $($current.ToString("o"))"
Log "Latest:    $($latest.ToString("o"))"
Log "Release:   $($latestRelease.name) / $($latestRelease.tag_name)"

if ($current -lt $latest) {

    Log "Update required."

    $asset = Select-WindowsAsset -release $latestRelease
    $url   = $asset.browser_download_url

    Log "Selected asset: $($asset.name)"
    Log "Downloading from: $url"

    $ext = [IO.Path]::GetExtension($asset.name).ToLowerInvariant()
    $archivePath = Join-Path $targetDirectory ("latest_release" + $ext)

    Log "Saving to: $archivePath"
    Invoke-WebRequest -Uri $url -OutFile $archivePath -Headers @{ "User-Agent" = "UpdaterScript" }

    Log "Extracting..."
    Extract-ReleaseArchive -ArchivePath $archivePath -DestinationDir $targetDirectory

    Log "Extraction complete."
    Remove-Item -LiteralPath $archivePath -Force -ErrorAction SilentlyContinue
    Log "Cleaned up archive."

    Set-Content -LiteralPath $versionFilePath -Value ($latest.ToString("o"))
    Log "Updated version file: $versionFilePath"
} else {
    Log "Already up to date."
}

# -------------------------------
# Choose launcher + icon + update shortcut + launch
# -------------------------------
$launcher = Select-Launcher -Root $targetDirectory

if ($launcher) {
    Log "Launching: $($launcher.Name)"

    $iconPath = Choose-IconInteractively -LauncherItem $launcher
    Log "Icon chosen: $iconPath"

    Ensure-Shortcut -IconPath $iconPath

    Start-Launcher -LauncherItem $launcher
} else {
    Log "No launcher found (.exe/.bat/.cmd) under $targetDirectory"
    Ensure-Shortcut -IconPath "$env:WINDIR\System32\cmd.exe"
}

Log "Done."
exit
""";
    }
}
