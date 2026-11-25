<#
.SYNOPSIS
Makes the key pair that supporter keys are signed with, or puts the public half back into the source. Run on your own PC.

.DESCRIPTION
The first run makes the pair: the private key goes to a folder outside the repository (never commit it, never send it to
anyone) and the public half into SpaceSharp.Core/Services/SupporterKey.cs, which is what the app checks keys against. The
private key then goes to the server's private folder; tools/supporter/README.md has the steps.

A later run never makes a new pair, since that would make every key already sold unreadable. It reads the public half from
the private key that is there and writes it into SupporterKey.cs when the line is empty, which is the repair after a copy of
the source without the key was unpacked over the folder.

.PARAMETER Folder
Where the private key is kept. Default: a SpaceSharp-supporter folder in your user folder.
#>
param(
    [string]$Folder = (Join-Path $HOME 'SpaceSharp-supporter')
)

$ErrorActionPreference = 'Stop'

# Git for Windows ships openssl; it is used when openssl is not on the PATH.
$openssl = (Get-Command openssl -ErrorAction SilentlyContinue).Source
if (-not $openssl) {
    $candidates = @(
        (Join-Path $env:ProgramFiles 'Git\usr\bin\openssl.exe'),
        (Join-Path $env:ProgramFiles 'Git\mingw64\bin\openssl.exe')
    )
    $openssl = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $openssl) {
    throw 'openssl was not found. It comes with Git for Windows; install that or put openssl on the PATH.'
}

$privateKey = Join-Path $Folder 'supporter-private.pem'
$made = -not (Test-Path $privateKey)
if ($made) {
    New-Item -ItemType Directory -Force -Path $Folder | Out-Null
    & $openssl ecparam -name prime256v1 -genkey -noout -out $privateKey
    if ($LASTEXITCODE -ne 0) { throw 'openssl could not make the private key.' }
}

$publicDer = Join-Path $Folder 'supporter-public.der'
& $openssl pkey -in $privateKey -pubout -outform DER -out $publicDer
if ($LASTEXITCODE -ne 0) { throw 'openssl could not read the public key from the private key.' }
$public = [Convert]::ToBase64String([IO.File]::ReadAllBytes($publicDer))
Remove-Item $publicDer

$source = Join-Path $PSScriptRoot '..\..\SpaceSharp.Core\Services\SupporterKey.cs'
$empty = 'internal const string PublicKey = "";'
$filled = "internal const string PublicKey = `"$public`";"
$code = [IO.File]::ReadAllText($source)

if ($made) {
    Write-Host "Private key: $privateKey"
    Write-Host '             Keep a copy somewhere safe (a password manager). Never commit it.'
} else {
    Write-Host "Private key: $privateKey (the one already there; no new key was made)"
}

if ($code.Contains($filled)) {
    Write-Host 'Public key:  already in SpaceSharp.Core/Services/SupporterKey.cs.'
} elseif ($code.Contains($empty)) {
    [IO.File]::WriteAllText($source, $code.Replace($empty, $filled), (New-Object Text.UTF8Encoding $false))
    Write-Host 'Public key:  written into SpaceSharp.Core/Services/SupporterKey.cs; commit that change.'
} else {
    throw 'SupporterKey.cs holds a public key that does not belong to this private key. Nothing was changed; find out which private key is the real one before going on.'
}
