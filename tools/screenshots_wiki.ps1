# Renders the wiki's pictures with the app itself, the way tools/screenshots.ps1 renders the README's: every scene is the
# real window posed on the pretend PC (SpaceSharp.Core/Services/MockSystem.cs), captured off the screen. The scenes are
# the WikiScenes list in SpaceSharp/MainWindow.Showcase.cs; the README's own scenes are rendered too, since the wiki
# shows those pictures as well.
#
#   .\tools\screenshots_wiki.ps1                          everything into ..\SpaceSharp.wiki\images (the wiki clone beside this repo)
#   .\tools\screenshots_wiki.ps1 -Out C:\path\to\images   somewhere else
#   .\tools\screenshots_wiki.ps1 wiki-palettes,changes    just those
#
# Windows only (the capture is GDI). Run it on a monitor with room for a 1600 x 900 window; the screen's scaling decides
# the pixel size of the PNGs. Afterwards commit and push the wiki repository.
param(
    [string[]] $Scenes = @(),
    [string] $Out = "",
    [string] $Size = "1600x900",
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root "SpaceSharp\SpaceSharp.csproj"
if ($Out -eq "") { $Out = Join-Path (Split-Path $root -Parent) "SpaceSharp.wiki\images" }
if (-not (Test-Path $Out)) { New-Item -ItemType Directory -Path $Out | Out-Null }

# the file each scene becomes, as the wiki pages reference them (README scenes keep the README's names)
$files = [ordered]@{
    none = "screenshot.png"; start = "shot-start.png"; drawer = "shot-filter.png"; insights = "shot-insights.png"; changes = "shot-changes.png"
    inspect = "shot-inspect.png"; menu = "shot-menu.png"; settings = "shot-settings.png"; about = "shot-about.png"; update = "shot-update.png"; light = "shot-light.png"
    duplicates = "shot-duplicates.png"
    "wiki-colorby-top" = "colorby-top.png"; "wiki-colorby-type" = "colorby-type.png"; "wiki-shading" = "shading.png"; "wiki-zoomed" = "zoomed.png"
    "wiki-rail" = "filter-rail.png"; "wiki-filtered" = "filtered.png"; "wiki-palette-picker" = "palette-picker.png"; "wiki-shortcuts" = "shortcuts.png"
    "wiki-changes-everything" = "changes-everything.png"; "wiki-inspect-selection" = "inspect-selection.png"
    "wiki-palettes" = "palettes.png"; "wiki-palette-editor" = "palette-editor.png"
    "wiki-settings-general" = "settings-general.png"; "wiki-settings-behavior" = "settings-behavior.png"; "wiki-settings-appearance" = "settings-appearance.png"
    "wiki-settings-text" = "settings-text.png"; "wiki-settings-map" = "settings-map.png"; "wiki-settings-scanning" = "settings-scanning.png"
    "wiki-rtl" = "rtl.png"
    "wiki-achievements" = "achievements.png"; "wiki-achievement-toast" = "achievement-toast.png"
    "wiki-empty-folders" = "empty-folders.png"; "wiki-duplicates" = "duplicates.png"
}
if ($Scenes.Count -eq 0) { $Scenes = @($files.Keys) }

Write-Host "Building $Configuration..."
dotnet build $project -c $Configuration --nologo -v quiet | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Build failed." }
$exe = Get-ChildItem (Join-Path $root "SpaceSharp\bin\$Configuration") -Recurse -Filter "SpaceSharp.exe" | Where-Object { $_.FullName -notmatch "\\publish\\" } | Select-Object -First 1
if (-not $exe) { throw "SpaceSharp.exe not found under SpaceSharp\bin\$Configuration." }

foreach ($scene in $Scenes) {
    if (-not $files.Contains($scene)) { Write-Warning "No such scene: $scene"; continue }
    $target = Join-Path $Out $files[$scene]
    Write-Host "  $scene -> $target"
    # a running SpaceSharp would take the arguments instead of letting a second window open, so the scene waits for it
    while (Get-Process SpaceSharp -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 300 }
    & $exe.FullName --scene $scene --shot $target --size $Size | Out-Null
    $proc = Get-Process SpaceSharp -ErrorAction SilentlyContinue
    if ($proc) { $proc | Wait-Process -Timeout 60 }
    if (-not (Test-Path $target)) { Write-Warning "  nothing written for $scene" }
}
Write-Host "Done. Commit and push the wiki repository."
