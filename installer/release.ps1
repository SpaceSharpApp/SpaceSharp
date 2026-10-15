# release.ps1
# Builds every release asset, on your PC or on the GitHub runner. Everything ends up in Releases\ under one naming
# scheme, SpaceSharp-win-<arch>-<kind>, because each build is its own Velopack channel ("win-x64" and "win-x86")
# and Velopack puts the channel name in every file:
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
# Releases are made here, on the PC with the code-signing certificate; GitHub Actions only runs the tests. The stages:
#   -Stage Build    tests, then publishes the four app builds (portable and Velopack folder, x64 and x86)
#                   into publish\portable*\ and publish\velopack*\
#   -Stage Pack     packs the installers, MSIs and update packages from those folders into Releases\
#   -Stage Upload   creates the GitHub release as a draft with every asset
# Without -Stage it runs Build and Pack in one go, which is how you make a build to try on another PC:
#
#   .\installer\release.ps1                        # build everything into Releases\
#   .\installer\release.ps1 -SkipDelta             # first release on a channel, or offline: no delta package
#   .\installer\release.ps1 -NoX86                 # x64 only
#   .\installer\release.ps1 -Upload -Force         # also upload, as a draft, from this PC (needs $env:GITHUB_TOKEN)
#
# Code signing happens on this PC, where the certificate lives (Certum SimplySign or a card; either shows up in the
# Windows certificate store once its desktop software is running). -Sign takes the certificate's SHA-1 thumbprint
# (certmgr.msc > Personal > Certificates > Details > Thumbprint) and signs everything in the right order: the app
# exes after Build, every file inside the Velopack packages plus Setup.exe and Update.exe during Pack (vpk's own
# --signParams), and the branded MSIs last. With -Sign, -Upload needs no -Force: a signed release from here is the
# real thing.
#
#   .\installer\release.ps1 -Sign 0123456789ABCDEF0123456789ABCDEF01234567 -Upload
#
# Needs the .NET 10 SDK, vpk (dotnet tool install -g vpk) and, for Upload, the GitHub CLI (gh).

param(
    [ValidateSet("All", "Build", "Pack", "Upload")]
    [string]$Stage = "All",
    [switch]$SkipDelta,
    [switch]$Upload,
    [switch]$Force,
    [string]$Sign = "",
    [switch]$NoX86
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$repoSlug = "SpaceSharpApp/SpaceSharp"
$repo = "https://github.com/$repoSlug"
$csproj = Get-Content .\SpaceSharp\SpaceSharp.csproj -Raw
if ($csproj -notmatch '<Version>([^<]+)</Version>') { throw "No <Version> in SpaceSharp.csproj" }
$version = $Matches[1]
"SpaceSharp $version ($Stage)"

$archs = if ($NoX86) { @("x64") } else { @("x64", "x86") }
$doBuild = $Stage -in @("All", "Build")
$doPack = $Stage -in @("All", "Pack")
$doUpload = $Stage -eq "Upload" -or ($Stage -eq "All" -and $Upload)

if ($Stage -eq "All" -and $Upload -and -not $Force -and -not $Sign) {
    throw "A release should be signed: pass -Sign <thumbprint> with SimplySign running, or -Force to upload an unsigned build anyway."
}
if (($doPack -or $doUpload) -and -not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    throw "vpk is not installed. Run: dotnet tool install -g vpk"
}

# ------------------------------------------------------------------ Signing
$signtool = $null
$signParams = ""
if ($Sign) {
    if ($Sign -notmatch '^[0-9A-Fa-f]{40}$') { throw "-Sign wants the certificate's SHA-1 thumbprint: 40 hex characters, no spaces." }
    $signtool = Get-Command signtool -ErrorAction SilentlyContinue | ForEach-Object Source
    if (-not $signtool) {
        $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
    }
    if (-not $signtool) { throw "signtool.exe was not found. Install the Windows SDK (Signing Tools) or put signtool on PATH." }
    $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object Thumbprint -eq $Sign.ToUpper()
    if (-not $cert) { throw "No certificate with thumbprint $Sign in the current user's store. Start SimplySign Desktop (or the card software) and sign in first." }
    "Signing as: $($cert.Subject)"
    # RFC 3161 timestamp from Certum, SHA-256 digests. The same string goes to vpk, which calls signtool itself.
    $signParams = "/sha1 $Sign /fd sha256 /tr http://time.certum.pl /td sha256"
}

function Sign-Files([string[]]$files) {
    if (-not $Sign) { return }
    foreach ($f in $files) {
        if (-not (Test-Path $f)) { throw "Cannot sign ${f}: it does not exist." }
        & $signtool sign /sha1 $Sign /fd sha256 /tr http://time.certum.pl /td sha256 /v $f
        if ($LASTEXITCODE) { throw "Signing $f failed" }
    }
}

# ------------------------------------------------------------------ Build
if ($doBuild) {
    "== Tests"
    dotnet test --configuration Release
    if ($LASTEXITCODE) { throw "Tests failed" }

    foreach ($arch in $archs) {
        $suffix = if ($arch -eq "x64") { "" } else { "-x86" }
        "== Portable exe ($arch)"
        dotnet publish .\SpaceSharp\SpaceSharp.csproj -p:PublishProfile=Portable$suffix
        if ($LASTEXITCODE) { throw "Portable publish ($arch) failed" }

        "== Velopack publish folder ($arch)"
        dotnet publish .\SpaceSharp\SpaceSharp.csproj -p:PublishProfile=Velopack$suffix
        if ($LASTEXITCODE) { throw "Velopack publish ($arch) failed" }
    }
    $appExes = @()
    foreach ($arch in $archs) {
        $p = if ($arch -eq "x64") { "portable" } else { "portable-x86" }
        $v = if ($arch -eq "x64") { "velopack" } else { "velopack-x86" }
        $appExes += ".\publish\$p\SpaceSharp.exe", ".\publish\$v\SpaceSharp.exe"
    }
    if ($Sign) {
        "== Signing the app exes"
        Sign-Files $appExes
    } else {
        ""
        "Built:"
        $appExes | ForEach-Object { "  $_" }
    }
}

# ------------------------------------------------------------------ Pack
if ($doPack) {
    New-Item -ItemType Directory -Force .\Releases | Out-Null
    # The portable exes join the other assets in Releases\ under the release names.
    foreach ($arch in $archs) {
        $src = if ($arch -eq "x64") { ".\publish\portable\SpaceSharp.exe" } else { ".\publish\portable-x86\SpaceSharp.exe" }
        if (-not (Test-Path $src)) { throw "$src is missing; run -Stage Build first." }
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
    # can show it without a network call. The heading must be exactly "## <version>": take "(unreleased)" off first.
    $lines = Get-Content .\CHANGELOG.md
    $start = ($lines | Select-String -Pattern "^## $([regex]::Escape($version))\s*$" | Select-Object -First 1).LineNumber
    if (-not $start) { throw "CHANGELOG.md has no '## $version' section (an '(unreleased)' marker still on it counts as missing); fix it before releasing." }
    $notes = @()
    for ($i = $start; $i -lt $lines.Count; $i++) { if ($lines[$i] -match '^## ') { break }; $notes += $lines[$i] }
    $notes -join "`n" | Set-Content -Path .\Releases\notes.md -Encoding utf8

    foreach ($arch in $archs) {
        $channel = "win-$arch"
        $packDir = if ($arch -eq "x64") { ".\publish\velopack" } else { ".\publish\velopack-x86" }
        if (-not (Test-Path "$packDir\SpaceSharp.exe")) { throw "$packDir\SpaceSharp.exe is missing; run -Stage Build first." }
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
            --releaseNotes .\Releases\notes.md `
            @(if ($signParams) { "--signParams", $signParams })
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
        if ($Sign) {
            "== Signing the MSI ($arch)"
            Sign-Files @(".\Releases\$wanted")      # after branding, which rewrites the file
        }
    }
    Remove-Item .\Releases\notes.md -ErrorAction SilentlyContinue

    "== Bridge for 1.x installs"
    # 1.x installs are on channel "win" and look for releases.win.json: give them the x64 manifest under that name.
    # RELEASES is the legacy manifest, copied too so nothing that still reads it is left behind. This runs after
    # every vpk pack, since each pack tidies the folder and would remove the copies.
    Copy-Item .\Releases\releases.win-x64.json .\Releases\releases.win.json -Force
    if (Test-Path .\Releases\RELEASES-win-x64) { Copy-Item .\Releases\RELEASES-win-x64 .\Releases\RELEASES -Force }

    "== SHA256SUMS.txt"
    # One line per download, in the sha256sum format, so a download can be checked with Get-FileHash or sha256sum.
    Get-ChildItem .\Releases\*.exe, .\Releases\*.msi | Sort-Object Name | ForEach-Object {
        "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower())  $($_.Name)"
    } | Set-Content .\Releases\SHA256SUMS.txt -Encoding ascii

    if ($Sign) {
        "== Checking signatures"
        Get-ChildItem .\Releases\*.exe, .\Releases\*.msi | Get-AuthenticodeSignature | ForEach-Object {
            "  $($_.Status)  $(Split-Path $_.Path -Leaf)"
            if ($_.Status -ne "Valid") { throw "$(Split-Path $_.Path -Leaf) is not validly signed" }
        }
    } else {
        ""
        "Packed. Files to sign before uploading:"
        foreach ($arch in $archs) { "  Releases\SpaceSharp-win-$arch-Setup.exe"; "  Releases\SpaceSharp-win-$arch.msi" }
    }
}

# ------------------------------------------------------------------ Upload
if ($doUpload) {
    if (-not $env:GITHUB_TOKEN) { throw "Set `$env:GITHUB_TOKEN to a token with repo access before uploading." }
    "== Creating the GitHub release (draft)"
    # No --publish: the release stays a draft until someone reads it and presses Publish. Installed copies only
    # see published releases, so a broken run never reaches anyone.
    vpk upload github --repoUrl $repo --token $env:GITHUB_TOKEN --releaseName "SpaceSharp $version" --channel win-x64
    if ($LASTEXITCODE) { throw "Upload (win-x64) failed" }
    if (-not $NoX86) {
        vpk upload github --repoUrl $repo --token $env:GITHUB_TOKEN --releaseName "SpaceSharp $version" --channel win-x86 --merge
        if ($LASTEXITCODE) { throw "Upload (win-x86) failed" }
    }
    # vpk only uploads what it packed; the portable exes and the 1.x bridge manifests go up with the GitHub CLI.
    $extra = @($archs | ForEach-Object { ".\Releases\SpaceSharp-win-$_.exe" }) + @(".\Releases\releases.win.json", ".\Releases\SHA256SUMS.txt")
    if (Test-Path .\Releases\RELEASES) { $extra += ".\Releases\RELEASES" }
    if (Get-Command gh -ErrorAction SilentlyContinue) {
        "== Uploading the portable exes and the 1.x manifests"
        gh release upload $version @extra --repo $repoSlug --clobber
        if ($LASTEXITCODE) { throw "Upload (extra assets) failed" }
    } else {
        Write-Warning "gh is not installed, so these were not uploaded: $($extra -join ', '). Attach them to the release by hand."
    }
}

""
"Release assets:"
Get-ChildItem .\Releases -ErrorAction SilentlyContinue | ForEach-Object { "  Releases\$($_.Name)" }
if ($doUpload) { "Draft release created: $repo/releases. Read it, then press Publish." }
