# build.ps1 — publish self-contained single-file binaries for win-x64 and win-arm64.
# Run ON WINDOWS in PowerShell from the windows_build/ directory:
#     powershell -ExecutionPolicy Bypass -File .\build.ps1
# Requires the .NET 8 SDK (https://dotnet.microsoft.com/download/dotnet/8.0).
#
# Output lands in dist/<rid>/.

param(
    [string]$Configuration = "Release",
    # Pass -Rids "win-x64" to build only one. Default builds both.
    [string[]]$Rids = @("win-x64", "win-arm64")
)

$ErrorActionPreference = "Stop"

$proj = "src/VibeXASR.Windows/VibeXASR.Windows.csproj"
$distRoot = Join-Path $PSScriptRoot "dist"

Write-Host "Restoring..." -ForegroundColor Cyan
dotnet restore $proj

foreach ($rid in $Rids) {
    $out = Join-Path $distRoot $rid
    Write-Host "Publishing $rid -> $out" -ForegroundColor Cyan

    # Notes:
    #  --self-contained: bundles the .NET runtime so end users need no SDK/runtime install.
    #  PublishSingleFile: one .exe. Native sherpa-onnx / ONNX Runtime DLLs are bundled and
    #    extracted at runtime (IncludeNativeLibrariesForSelfExtract).
    #  TODO(win): if startup extraction is slow, drop PublishSingleFile and ship a folder,
    #    or set <IncludeAllContentForSelfExtract>true</IncludeAllContentForSelfExtract>.
    dotnet publish $proj `
        -c $Configuration `
        -r $rid `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -o $out
}

Write-Host "Done. Binaries in $distRoot" -ForegroundColor Green
