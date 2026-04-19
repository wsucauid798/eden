#!/usr/bin/env pwsh
# Eden build script (Windows / cross-platform PowerShell)
# Produces runnable binaries under ../build/.
#
# Usage:
#   scripts/build.ps1                     # current OS, x64
#   scripts/build.ps1 -Rid linux-x64      # cross-compile target
#   scripts/build.ps1 -Clean              # wipe build/ first

param(
    [string]$Rid    = $(if ($IsWindows -or $null -eq $IsWindows) { "win-x64" }
                       elseif ($IsLinux)   { "linux-x64" }
                       elseif ($IsMacOS)   { "osx-arm64" }
                       else                { "win-x64" }),
    [switch]$Clean
)

$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..")
$buildDir = Join-Path $repo "build"
$serverOut = Join-Path $buildDir "eden-server-$Rid"

if ($Clean -and (Test-Path $buildDir)) {
    Write-Host "cleaning $buildDir"
    Remove-Item -Recurse -Force $buildDir
}
New-Item -ItemType Directory -Force -Path $buildDir | Out-Null

Write-Host "publishing Eden.Server (CLI) for $Rid → $serverOut"
dotnet publish `
    (Join-Path $repo "src/Eden.Server.Cli/Eden.Server.Cli.csproj") `
    --configuration Release `
    --runtime $Rid `
    --self-contained `
    --output $serverOut `
    --nologo

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "server binaries:"
Get-ChildItem -Recurse -File -Path $serverOut | Where-Object {
    $_.Extension -in '.exe','' -and $_.Length -gt 1MB
} | ForEach-Object {
    $sizeMb = [math]::Round($_.Length / 1MB, 1)
    Write-Host "  $($_.FullName) ($sizeMb MB)"
}

# ------------------------------------------------------------------
# Viewer export (Godot)
# ------------------------------------------------------------------

# Find Godot. Honors $env:GODOT_BIN first (explicit override), then
# tries common command names on PATH, then the standard Windows install.
$godot = $null
if ($env:GODOT_BIN -and (Test-Path $env:GODOT_BIN)) {
    $godot = $env:GODOT_BIN
}
if (-not $godot) {
    foreach ($name in @('godot', 'godot4', 'Godot', 'Godot_mono',
                        'Godot_v4.6.2-stable_mono_win64',
                        'Godot_v4.6.1-stable_mono_win64')) {
        $cmd = Get-Command $name -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($cmd) { $godot = $cmd.Source; break }
    }
}
if (-not $godot) {
    $fallbacks = @(
        'C:\Program Files\Godot\Godot_v4.6.2-stable_mono_win64.exe',
        'C:\Program Files\Godot\Godot_v4.6.1-stable_mono_win64.exe'
    )
    foreach ($p in $fallbacks) { if (Test-Path $p) { $godot = $p; break } }
}

if (-not $godot) {
    Write-Host ""
    Write-Host "godot not found. skipping viewer export."
    Write-Host "set `$env:GODOT_BIN to the Godot executable path, or put godot on PATH."
    exit 0
}
$viewerProject = Join-Path $repo "src/Eden.Viewer"
$viewerOut = Join-Path $buildDir "eden-viewer-$Rid"
$viewerExe = Join-Path $viewerOut "Eden.Viewer.exe"
New-Item -ItemType Directory -Force -Path $viewerOut | Out-Null

# Preset name matches the [preset.0] block in src/Eden.Viewer/export_presets.cfg.
$preset = switch -Wildcard ($Rid) {
    'win-*'   { 'Windows Desktop' }
    'linux-*' { 'Linux/X11' }
    'osx-*'   { 'macOS' }
    default   { 'Windows Desktop' }
}

Write-Host ""
Write-Host "exporting Eden.Viewer ($preset) via $godot → $viewerExe"
& "$godot" --headless --path $viewerProject --export-release $preset $viewerExe
$exit = $LASTEXITCODE

if ($exit -ne 0) {
    Write-Host ""
    Write-Host "viewer export failed (exit $exit). common causes:"
    Write-Host "  - export templates for Godot 4.6.1 not installed"
    Write-Host "    (Godot editor → Editor → Manage Export Templates)"
    Write-Host "  - export_presets.cfg is stale — open the project in Godot,"
    Write-Host "    Project → Export, save, commit export_presets.cfg"
    exit $exit
}

Write-Host ""
Write-Host "viewer binary: $viewerExe"
