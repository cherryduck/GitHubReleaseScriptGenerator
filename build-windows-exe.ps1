# Run this on Windows with the .NET SDK installed.
dotnet publish .\GitHubReleaseScriptGenerator.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
Write-Host "EXE will be under: bin\Release\net8.0-windows\win-x64\publish"
