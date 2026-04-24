# Run this on Windows with the .NET SDK installed.

$publishDir = ".\dist"

Remove-Item .\bin, .\obj, $publishDir -Recurse -Force -ErrorAction SilentlyContinue

dotnet publish .\GitHubReleaseScriptGenerator.csproj `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:SelfContained=false `
    -p:PublishSelfContained=false `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $publishDir

Write-Host "EXE will be under: $publishDir"