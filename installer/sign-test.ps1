# sign-test.ps1
# Signs one exe with the Certum certificate and checks the result, before trusting release.ps1 -Sign with a whole
# release. Run it with SimplySign Desktop (or the card software) running and signed in, so the certificate is in the
# Windows certificate store:
#
#   .\installer\sign-test.ps1 -Sign 0123456789ABCDEF0123456789ABCDEF01234567
#   .\installer\sign-test.ps1 -Sign <thumbprint> -File .\SpaceSharp\bin\Release\net10.0-windows\SpaceSharp.exe
#
# Without -File it publishes a portable SpaceSharp.exe into publish\sign-test\ and signs a copy of it, so nothing
# you keep gets touched. It signs with SHA-256 and an RFC 3161 timestamp from Certum, the same parameters release.ps1
# uses, then verifies with signtool and with PowerShell and prints what Windows will show in the file's properties.

param(
    [Parameter(Mandatory = $true)] [string]$Sign,
    [string]$File = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

if ($Sign -notmatch '^[0-9A-Fa-f]{40}$') { throw "-Sign wants the certificate's SHA-1 thumbprint: 40 hex characters, no spaces (certmgr.msc > Personal > Certificates > Details > Thumbprint)." }
$Sign = $Sign.ToUpper()

# ---- the certificate must be in the store, with a private key reachable through its provider
$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object Thumbprint -eq $Sign
if (-not $cert) {
    $all = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.EnhancedKeyUsageList.FriendlyName -contains "Code Signing" }
    "No certificate with thumbprint $Sign in Current User > Personal."
    if ($all) { "Code-signing certificates that are there:"; $all | ForEach-Object { "  $($_.Thumbprint)  $($_.Subject)  (until $($_.NotAfter.ToString('yyyy-MM-dd')))" } }
    else { "No code-signing certificate is in the store at all: start SimplySign Desktop and sign in, then run this again." }
    exit 1
}
"Certificate: $($cert.Subject)"
"Issuer:      $($cert.Issuer)"
"Valid:       $($cert.NotBefore.ToString('yyyy-MM-dd')) to $($cert.NotAfter.ToString('yyyy-MM-dd'))"
"Key:         $($cert.PublicKey.Oid.FriendlyName) $($cert.PublicKey.Key.KeySize) bits"
if (-not $cert.HasPrivateKey) { throw "The certificate has no private key behind it. Importing the PEM or DER file gives only the public half; the key lives in SimplySign (or on the card), which must be running and signed in." }

# ---- signtool from the Windows SDK
$signtool = Get-Command signtool -ErrorAction SilentlyContinue | ForEach-Object Source
if (-not $signtool) {
    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $signtool) { throw "signtool.exe was not found. Install the Windows SDK (Signing Tools for Desktop Apps) or put signtool on PATH." }
"signtool:    $signtool"

# ---- the file to sign: a fresh portable build unless one was given
if (-not $File) {
    "== Publishing a portable SpaceSharp.exe to sign"
    dotnet publish .\SpaceSharp\SpaceSharp.csproj -p:PublishProfile=Portable -o .\publish\sign-test\src | Out-Host
    if ($LASTEXITCODE) { throw "Publish failed" }
    $File = ".\publish\sign-test\src\SpaceSharp.exe"
}
if (-not (Test-Path $File)) { throw "$File does not exist." }
New-Item -ItemType Directory -Force .\publish\sign-test | Out-Null
$target = Join-Path (Resolve-Path .\publish\sign-test) ("signed-" + (Split-Path $File -Leaf))
Copy-Item $File $target -Force
"Signing:     $target"

# ---- sign: SHA-256 digest, RFC 3161 timestamp from Certum with SHA-256, verbose so the chain is printed
"== signtool sign"
& $signtool sign /sha1 $Sign /fd sha256 /tr http://time.certum.pl /td sha256 /v $target
if ($LASTEXITCODE) { throw "Signing failed (exit $LASTEXITCODE). A timestamp failure is usually the network; a 'private key' failure is SimplySign not running or not signed in." }

# ---- verify as Windows does (the /pa policy is what SmartScreen and the properties dialog use)
"== signtool verify"
& $signtool verify /pa /v $target
if ($LASTEXITCODE) { throw "Verification failed (exit $LASTEXITCODE)." }

"== PowerShell's view"
$sig = Get-AuthenticodeSignature $target
"Status:      $($sig.Status) ($($sig.StatusMessage))"
"Signer:      $($sig.SignerCertificate.Subject)"
"Timestamp:   $(if ($sig.TimeStamperCertificate) { $sig.TimeStamperCertificate.Subject } else { 'none (the signature expires with the certificate)' })"
if ($sig.Status -ne "Valid") { throw "The signature is $($sig.Status)." }

""
"Signed and verified. Right-click the file > Properties > Digital Signatures to see what users see:"
"  $target"
"If that looks right, the real thing is:  .\installer\release.ps1 -Sign $Sign"
