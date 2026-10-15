# sign-draft.ps1
# The last step of a release, on the PC with the code-signing certificate. The release workflow has built the
# version on GitHub and left it as a draft with unsigned assets; this script pulls the files Windows judges by
# their signature (the portable exes, the Setup.exe installers and the MSIs), signs them with the Certum
# certificate, uploads them back over the unsigned ones and rewrites SHA256SUMS.txt to match.
#
# The update packages (the .nupkg files and the Portable zip) are left as the workflow made them: Velopack's
# manifests carry their checksums, so they cannot be changed after packing. The app exe inside them is unsigned,
# which only shows in the properties of an installed copy; downloads and SmartScreen see the signed outer files.
#
#   .\installer\sign-draft.ps1 -Sign 0123456789ABCDEF0123456789ABCDEF01234567           # the current <Version>
#   .\installer\sign-draft.ps1 -Sign <thumbprint> -Version 2.3.0                        # a named draft
#
# Needs the GitHub CLI (gh, signed in to the repo), signtool (Windows SDK) and SimplySign Desktop running and
# signed in, so the certificate is in the current user's store. -Sign is the certificate's SHA-1 thumbprint.

param(
    [Parameter(Mandatory = $true)]
    [string]$Sign,
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
$repoSlug = "SpaceSharpApp/SpaceSharp"

if (-not $Version) {
    $csproj = Get-Content .\SpaceSharp\SpaceSharp.csproj -Raw
    if ($csproj -notmatch '<Version>([^<]+)</Version>') { throw "No <Version> in SpaceSharp.csproj" }
    $Version = $Matches[1]
}
if ($Sign -notmatch '^[0-9A-Fa-f]{40}$') { throw "-Sign wants the certificate's SHA-1 thumbprint: 40 hex characters, no spaces." }
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw "The GitHub CLI (gh) is not installed." }

$signtool = Get-Command signtool -ErrorAction SilentlyContinue | ForEach-Object Source
if (-not $signtool) {
    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $signtool) { throw "signtool.exe was not found. Install the Windows SDK (Signing Tools) or put signtool on PATH." }
$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object Thumbprint -eq $Sign.ToUpper()
if (-not $cert) { throw "No certificate with thumbprint $Sign in the current user's store. Start SimplySign Desktop and sign in first." }
"Signing SpaceSharp $Version as: $($cert.Subject)"

# ------------------------------------------------------------------ Download
$dir = Join-Path $root "Releases\draft-$Version"
if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
New-Item $dir -ItemType Directory | Out-Null
"== Downloading the exes and MSIs of the draft"
gh release download $Version --repo $repoSlug --dir $dir --pattern "*.exe" --pattern "*.msi"
if ($LASTEXITCODE) { throw "Download failed. Is there a draft release tagged $Version?" }
$files = Get-ChildItem $dir\*.exe, $dir\*.msi
if (-not $files) { throw "The draft has no .exe or .msi assets." }

# ------------------------------------------------------------------ Sign
"== Signing"
foreach ($f in $files) {
    & $signtool sign /sha1 $Sign /fd sha256 /tr http://time.certum.pl /td sha256 /v $f.FullName
    if ($LASTEXITCODE) { throw "Signing $($f.Name) failed" }
}
"== Checking signatures"
$files | Get-AuthenticodeSignature | ForEach-Object {
    "  $($_.Status)  $(Split-Path $_.Path -Leaf)"
    if ($_.Status -ne "Valid") { throw "$(Split-Path $_.Path -Leaf) is not validly signed" }
}

# ------------------------------------------------------------------ Checksums
"== SHA256SUMS.txt"
$files | Sort-Object Name | ForEach-Object {
    "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower())  $($_.Name)"
} | Set-Content (Join-Path $dir "SHA256SUMS.txt") -Encoding ascii

# ------------------------------------------------------------------ Upload
"== Uploading the signed files over the unsigned ones"
$upload = @($files | ForEach-Object FullName) + @((Join-Path $dir "SHA256SUMS.txt"))
gh release upload $Version @upload --repo $repoSlug --clobber
if ($LASTEXITCODE) { throw "Upload failed" }

""
"Signed and uploaded. Read the draft at https://github.com/$repoSlug/releases, then press Publish."
