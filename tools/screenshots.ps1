# Renders the README screenshots with the app itself: every scene is the real window, posed on the pretend PC
# (SpaceSharp.Core/Services/MockSystem.cs), captured off the screen with its title bar, dialog and menu.
#
#   .\tools\screenshots.ps1                 all scenes into docs\
#   .\tools\screenshots.ps1 start,changes   just those
#   .\tools\screenshots.ps1 -Size 1600x900  the window's client size (the default)
#
# Windows only (the capture is GDI). Run it on a monitor with room for a 1600 x 900 window; the screen's scaling
# decides the pixel size of the PNGs, 125% or 150% gives the crisp ones GitHub shows well. Afterwards run
# tools/make-mobile-shots.py for the small WebP copies the website shows on phones.
param(
    [string[]] $Scenes = @("none", "drawer", "insights", "changes", "inspect", "settings", "menu", "start", "about", "update", "light", "duplicates"),
    [string] $Size = "1600x900",
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root "SpaceSharp\SpaceSharp.csproj"
$docs = Join-Path $root "docs"

# the file each scene becomes, as the README references them
$files = @{
    none = "screenshot.png"; drawer = "shot-filter.png"; insights = "shot-insights.png"; changes = "shot-changes.png"
    inspect = "shot-inspect.png"; settings = "shot-settings.png"; menu = "shot-menu.png"; start = "shot-start.png"
    about = "shot-about.png"; update = "shot-update.png"; light = "shot-light.png"; duplicates = "shot-duplicates.png"
}

Write-Host "Building $Configuration..."
dotnet build $project -c $Configuration --nologo -v quiet | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Build failed." }
$exe = Get-ChildItem (Join-Path $root "SpaceSharp\bin\$Configuration") -Recurse -Filter "SpaceSharp.exe" | Where-Object { $_.FullName -notmatch "\\publish\\" } | Select-Object -First 1
if (-not $exe) { throw "SpaceSharp.exe not found under SpaceSharp\bin\$Configuration." }

foreach ($scene in $Scenes) {
    if (-not $files.ContainsKey($scene)) { Write-Warning "No such scene: $scene"; continue }
    $out = Join-Path $docs $files[$scene]
    Write-Host "  $scene -> docs\$($files[$scene])"
    # a running SpaceSharp would take the arguments instead of letting a second window open, so the scene waits for it
    while (Get-Process SpaceSharp -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 300 }
    & $exe.FullName --scene $scene --shot $out --size $Size | Out-Null
    $proc = Get-Process SpaceSharp -ErrorAction SilentlyContinue
    if ($proc) { $proc | Wait-Process -Timeout 60 }
    if (-not (Test-Path $out)) { Write-Warning "  nothing written for $scene" }
}
Write-Host "Done. Run python tools/make-mobile-shots.py for the phone copies."
