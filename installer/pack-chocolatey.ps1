# Writes and packs the Chocolatey package for one release:
#
#   .\installer\pack-chocolatey.ps1                    # from the MSIs in Releases\ (release.ps1 calls it this way)
#   .\installer\pack-chocolatey.ps1 -FromGitHub        # from the MSIs of the published release, downloaded to check them
#   .\installer\pack-chocolatey.ps1 -Version 2.10.0 -FromGitHub
#
# installer\chocolatey holds the package with placeholders. A copy with the version, the download addresses and the
# SHA-256 checksums filled in goes to Releases\chocolatey, and choco packs it there. Nothing is built or uploaded.
# -FromGitHub is for a release that is already out: it takes the checksums from the files people will download,
# so they are right even when Releases\ has been rebuilt since.
param(
    [string]$Version,
    [switch]$FromGitHub
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$repo = "https://github.com/SpaceSharpApp/SpaceSharp"
if (-not $Version) {
    [xml]$project = Get-Content (Join-Path $root "SpaceSharp\SpaceSharp.csproj")
    $Version = "$($project.Project.PropertyGroup.Version)".Trim()
}
$download = "$repo/releases/download/$Version"
$out = Join-Path $root "Releases\chocolatey"
"Chocolatey package for SpaceSharp $Version"

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item $out -ItemType Directory -Force | Out-Null
Copy-Item (Join-Path $PSScriptRoot "chocolatey\*") $out -Recurse

# The SHA-256 of one arch's MSI, or $null when the release has none for it.
function Get-MsiHash([string]$arch) {
    $name = "SpaceSharp-win-$arch.msi"
    if ($FromGitHub) {
        $file = Join-Path ([IO.Path]::GetTempPath()) "choco-$Version-$name"
        try {
            Invoke-WebRequest "$download/$name" -OutFile $file -UseBasicParsing
        } catch {
            if ($arch -eq "x64") { throw "Could not download $download/$name. Is release $Version published?" }
            return $null
        }
    } else {
        $file = Join-Path $root "Releases\$name"
        if (-not (Test-Path $file)) {
            if ($arch -eq "x64") { throw "$file is missing. Run release.ps1 first, or pass -FromGitHub." }
            return $null
        }
    }
    $hash = (Get-FileHash $file -Algorithm SHA256).Hash.ToLower()
    if ($FromGitHub) { Remove-Item $file -ErrorAction SilentlyContinue }
    return $hash
}

$sha64 = Get-MsiHash "x64"
$sha32 = Get-MsiHash "x86"
$scriptPath = Join-Path $out "tools\chocolateyinstall.ps1"
$script = Get-Content $scriptPath -Raw
if ($sha32) {
    $script = $script.Replace("__URL32__", "$download/SpaceSharp-win-x86.msi").Replace("__SHA32__", $sha32)
} else {
    # An x64-only release: the package carries no 32-bit download.
    $script = ($script -split "`r?`n" | Where-Object { $_ -notmatch "__URL32__|__SHA32__|checksumType   =" }) -join "`r`n"
}
$script = $script.Replace("__URL64__", "$download/SpaceSharp-win-x64.msi").Replace("__SHA64__", $sha64)
Set-Content $scriptPath $script -Encoding utf8 -NoNewline

$nuspecPath = Join-Path $out "spacesharp.nuspec"
Set-Content $nuspecPath ((Get-Content $nuspecPath -Raw).Replace("__VERSION__", $Version)) -Encoding utf8 -NoNewline

"  x64  $sha64"
if ($sha32) { "  x86  $sha32" }
if (-not (Get-Command choco -ErrorAction SilentlyContinue)) {
    Write-Warning "choco is not installed, so the package was written to $out but not packed."
    return
}
choco pack $nuspecPath --outputdirectory $out
if ($LASTEXITCODE) { throw "choco pack failed" }
""
"Packed $out\spacesharp.$Version.nupkg"
"Try it (elevated, on a throwaway account or VM):  choco install spacesharp --source `"$out`" -y"
"Push it once the GitHub release is published:     choco push `"$out\spacesharp.$Version.nupkg`" --source https://push.chocolatey.org/"
