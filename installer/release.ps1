# release.ps1
# Builds every release asset on your own PC, the same way the GitHub release workflow does:
#   publish\portable\SpaceSharp.exe        one-file portable exe
#   Releases\SpaceSharp-win-Setup.exe      Velopack installer
#   Releases\SpaceSharp-win-x64.msi        Windows Installer package, branded
#   Releases\SpaceSharp-win-Portable.zip   auto-updating portable
#   Releases\*.nupkg, releases.win.json    update packages
#
# Needs the .NET 10 SDK and vpk (dotnet tool install -g vpk). Run from anywhere:
#
#   .\installer\release.ps1              # build everything
#   .\installer\release.ps1 -SkipDelta   # first release, or offline: no delta package
#   .\installer\release.ps1 -Upload      # also upload to a GitHub release (needs $env:GITHUB_TOKEN)

param(
    [switch]$SkipDelta,
    [switch]$Upload
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$repo = "https://github.com/ClearanceClarence/SpaceSharp"
$csproj = Get-Content .\SpaceSharp\SpaceSharp.csproj -Raw
if ($csproj -notmatch '<Version>([^<]+)</Version>') { throw "No <Version> in SpaceSharp.csproj" }
$version = $Matches[1]
"SpaceSharp $version"

if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    throw "vpk is not installed. Run: dotnet tool install -g vpk"
}

"== Tests"
dotnet test --configuration Release
if ($LASTEXITCODE) { throw "Tests failed" }

"== Portable exe"
dotnet publish .\SpaceSharp\SpaceSharp.csproj -p:PublishProfile=Portable
if ($LASTEXITCODE) { throw "Portable publish failed" }

"== Velopack publish folder"
dotnet publish .\SpaceSharp\SpaceSharp.csproj -p:PublishProfile=Velopack
if ($LASTEXITCODE) { throw "Velopack publish failed" }

if (-not $SkipDelta) {
    "== Previous release (for the delta package)"
    vpk download github --repoUrl $repo
    if ($LASTEXITCODE) { Write-Warning "Could not download the previous release; building without a delta package." }
}

"== Release notes"
# The version's CHANGELOG section travels inside the Velopack package, so the update prompt's "What's new"
# can show it without a network call.
$lines = Get-Content .\CHANGELOG.md
$start = ($lines | Select-String -Pattern "^## $version\b" | Select-Object -First 1).LineNumber
if (-not $start) { throw "CHANGELOG.md has no '## $version' section; add one before releasing." }
$notes = @()
for ($i = $start; $i -lt $lines.Count; $i++) { if ($lines[$i] -match '^## ') { break }; $notes += $lines[$i] }
New-Item -ItemType Directory -Force .\Releases | Out-Null
$notes -join "`n" | Set-Content -Path .\Releases\notes.md -Encoding utf8

"== vpk pack"
vpk pack `
    --packId SpaceSharp `
    --packVersion $version `
    --packDir .\publish\velopack `
    --mainExe SpaceSharp.exe `
    --packTitle SpaceSharp `
    --packAuthors ClearanceClarence `
    --icon .\SpaceSharp\Assets\SpaceSharp.ico `
    --splashImage .\installer\splash.gif `
    --msi `
    --instLocation Either `
    --instWelcome .\installer\welcome.rtf `
    --instLicense .\installer\license.txt `
    --instConclusion .\installer\conclusion.rtf `
    --releaseNotes .\Releases\notes.md
if ($LASTEXITCODE) { throw "vpk pack failed" }
Remove-Item .\Releases\notes.md -ErrorAction SilentlyContinue

"== Naming the MSI"
# vpk names the MSI after its own rules (they have changed between versions); the release, the website and
# the winget manifest all expect exactly SpaceSharp-win-x64.msi, so rename whatever came out.
$msi = Get-ChildItem .\Releases\*.msi | Select-Object -First 1
if (-not $msi) { throw "vpk pack produced no .msi" }
if ($msi.Name -ne "SpaceSharp-win-x64.msi") {
    Remove-Item .\Releases\SpaceSharp-win-x64.msi -ErrorAction SilentlyContinue
    Rename-Item $msi.FullName "SpaceSharp-win-x64.msi"
    "Renamed $($msi.Name) -> SpaceSharp-win-x64.msi"
}

"== Branding the MSI"
& (Join-Path $PSScriptRoot "brand-msi.ps1")

if ($Upload) {
    if (-not $env:GITHUB_TOKEN) { throw "Set `$env:GITHUB_TOKEN to a token with repo access before using -Upload." }
    "== Uploading to GitHub"
    vpk upload github --repoUrl $repo --token $env:GITHUB_TOKEN --publish --releaseName "SpaceSharp $version"
    if ($LASTEXITCODE) { throw "Upload failed" }
}

""
"Release assets:"
Get-ChildItem .\Releases | ForEach-Object { "  Releases\$($_.Name)" }
"  publish\portable\SpaceSharp.exe"
if (-not $Upload) { "Attach all of them to the GitHub release, or rerun with -Upload." }
