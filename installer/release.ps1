# release.ps1
# Builds every release asset on your own PC, the same way the GitHub release workflow does. Everything ends up in
# Releases\ under one naming scheme, SpaceSharp-win-<arch>-<kind>, because each build is its own Velopack channel
# ("win-x64" and "win-x86") and Velopack puts the channel name in every file:
#   SpaceSharp-win-x64.exe                 one-file portable exe (no updater)
#   SpaceSharp-win-x64-Setup.exe           Velopack installer
#   SpaceSharp-win-x64.msi                 Windows Installer package, branded
#   SpaceSharp-win-x64-Portable.zip        auto-updating portable
#   SpaceSharp-<version>-win-x64-full.nupkg, releases.win-x64.json, RELEASES-win-x64   update packages and manifests
#   SpaceSharp-win-x86-*, releases.win-x86.json, ...                                   the same for 32-bit Windows
#
# An installed copy updates from the channel it was installed with, so an x86 install never pulls an x64 package.
# 1.x installs are on the old channel "win" and look for releases.win.json; the script writes that file as a copy
# of releases.win-x64.json so they find 2.0 (a full package, no delta), after which they are on "win-x64" like
# everyone else. Keep uploading it until nobody is left on 1.x.
#
# Needs the .NET 10 SDK, vpk (dotnet tool install -g vpk) and, for -Upload, the GitHub CLI (gh). Run from anywhere:
#
#   .\installer\release.ps1              # build everything
#   .\installer\release.ps1 -SkipDelta   # first release, or offline: no delta package
#   .\installer\release.ps1 -Upload      # also upload to a GitHub release (needs $env:GITHUB_TOKEN)
#   .\installer\release.ps1 -NoX86       # x64 only

param(
    [switch]$SkipDelta,
    [switch]$Upload,
    [switch]$NoX86
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

$archs = if ($NoX86) { @("x64") } else { @("x64", "x86") }

foreach ($arch in $archs) {
    $suffix = if ($arch -eq "x64") { "" } else { "-x86" }
    "== Portable exe ($arch)"
    dotnet publish .\SpaceSharp\SpaceSharp.csproj -p:PublishProfile=Portable$suffix
    if ($LASTEXITCODE) { throw "Portable publish ($arch) failed" }

    "== Velopack publish folder ($arch)"
    dotnet publish .\SpaceSharp\SpaceSharp.csproj -p:PublishProfile=Velopack$suffix
    if ($LASTEXITCODE) { throw "Velopack publish ($arch) failed" }
}
New-Item -ItemType Directory -Force .\Releases | Out-Null
# The portable exes join the other assets in Releases\ under the release names.
foreach ($arch in $archs) {
    $src = if ($arch -eq "x64") { ".\publish\portable\SpaceSharp.exe" } else { ".\publish\portable-x86\SpaceSharp.exe" }
    Copy-Item $src ".\Releases\SpaceSharp-win-$arch.exe" -Force
}

if (-not $SkipDelta) {
    "== Previous release (for the delta packages)"
    foreach ($arch in $archs) {
        $channel = "win-$arch"
        vpk download github --repoUrl $repo --channel $channel
        if ($LASTEXITCODE) { Write-Warning "No previous $channel release to download (the first release on a channel has none); building without a delta package." }
    }
}

"== Release notes"
# The version's CHANGELOG section travels inside the Velopack package, so the update prompt's "What's new"
# can show it without a network call.
$lines = Get-Content .\CHANGELOG.md
$start = ($lines | Select-String -Pattern "^## $version\b" | Select-Object -First 1).LineNumber
if (-not $start) { throw "CHANGELOG.md has no '## $version' section; add one before releasing." }
$notes = @()
for ($i = $start; $i -lt $lines.Count; $i++) { if ($lines[$i] -match '^## ') { break }; $notes += $lines[$i] }
$notes -join "`n" | Set-Content -Path .\Releases\notes.md -Encoding utf8

foreach ($arch in $archs) {
    $channel = "win-$arch"
    $packDir = if ($arch -eq "x64") { ".\publish\velopack" } else { ".\publish\velopack-x86" }
    "== vpk pack ($channel)"
    vpk pack `
        --packId SpaceSharp `
        --packVersion $version `
        --packDir $packDir `
        --channel $channel `
        --mainExe SpaceSharp.exe `
        --packTitle SpaceSharp `
        --packAuthors ClearanceClarence `
        --icon .\SpaceSharp\Assets\SpaceSharp.ico `
        --splashImage .\installer\splash.gif `
        --msi `
        --instLocation Either `
        --instWelcome .\installer\welcome.txt `
        --instLicense .\installer\license.txt `
        --instConclusion .\installer\conclusion.txt `
        --releaseNotes .\Releases\notes.md
    if ($LASTEXITCODE) { throw "vpk pack ($channel) failed" }

    "== Naming the MSI ($arch)"
    # vpk names the MSI after its own rules (they have changed between versions); the release, the website and
    # the winget manifest all expect exactly SpaceSharp-win-x64.msi / SpaceSharp-win-x86.msi, so rename whatever came out.
    $wanted = "SpaceSharp-win-$arch.msi"
    $other = if ($arch -eq "x64") { "SpaceSharp-win-x86.msi" } else { "SpaceSharp-win-x64.msi" }
    $msi = Get-ChildItem .\Releases\*.msi | Where-Object { $_.Name -ne $other } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $msi) { throw "vpk pack ($channel) produced no .msi" }
    if ($msi.Name -ne $wanted) {
        Remove-Item ".\Releases\$wanted" -ErrorAction SilentlyContinue
        Rename-Item $msi.FullName $wanted
        "Renamed $($msi.Name) -> $wanted"
    }

    "== Branding the MSI ($arch)"
    & (Join-Path $PSScriptRoot "brand-msi.ps1") -Msi (Join-Path $root "Releases\$wanted")
}
Remove-Item .\Releases\notes.md -ErrorAction SilentlyContinue

"== Bridge for 1.x installs"
# 1.x installs are on channel "win" and look for releases.win.json: give them the x64 manifest under that name.
# RELEASES is the legacy manifest, copied too so nothing that still reads it is left behind. This runs after
# every vpk pack, since each pack tidies the folder and would remove the copies.
Copy-Item .\Releases\releases.win-x64.json .\Releases\releases.win.json -Force
if (Test-Path .\Releases\RELEASES-win-x64) { Copy-Item .\Releases\RELEASES-win-x64 .\Releases\RELEASES -Force }

if ($Upload) {
    if (-not $env:GITHUB_TOKEN) { throw "Set `$env:GITHUB_TOKEN to a token with repo access before using -Upload." }
    "== Uploading to GitHub"
    vpk upload github --repoUrl $repo --token $env:GITHUB_TOKEN --publish --releaseName "SpaceSharp $version" --channel win-x64
    if ($LASTEXITCODE) { throw "Upload (win-x64) failed" }
    if (-not $NoX86) {
        vpk upload github --repoUrl $repo --token $env:GITHUB_TOKEN --publish --releaseName "SpaceSharp $version" --channel win-x86 --merge
        if ($LASTEXITCODE) { throw "Upload (win-x86) failed" }
    }
    # vpk only uploads what it packed; the portable exes and the 1.x bridge manifests go up with the GitHub CLI.
    $extra = @($archs | ForEach-Object { ".\Releases\SpaceSharp-win-$_.exe" }) + @(".\Releases\releases.win.json")
    if (Test-Path .\Releases\RELEASES) { $extra += ".\Releases\RELEASES" }
    if (Get-Command gh -ErrorAction SilentlyContinue) {
        "== Uploading the portable exes and the 1.x manifests"
        gh release upload $version @extra --repo ClearanceClarence/SpaceSharp --clobber
        if ($LASTEXITCODE) { throw "Upload (extra assets) failed" }
    } else {
        Write-Warning "gh is not installed, so these were not uploaded: $($extra -join ', '). Attach them to the release by hand."
    }
}

""
"Release assets:"
Get-ChildItem .\Releases | ForEach-Object { "  Releases\$($_.Name)" }
if (-not $Upload) { "Attach all of them to the GitHub release, or rerun with -Upload." }
