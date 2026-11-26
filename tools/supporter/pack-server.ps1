<#
.SYNOPSIS
Puts together the folder to upload to the web host: the server code plus the private folder with the signing key.

.DESCRIPTION
Writes <Folder>\upload\supporter, ready to upload as /supporter/ on the site:

  hook.php, thanks.php, lib.php, where.php, .htaccess    the code from tools/supporter/server
  private-<random>\key.php                               the private key, as a file a web server answers with nothing
  private-<random>\config.php                            the settings; fill in webhook_secret and product_id
  private-<random>\.htaccess and web.config              deny all, for Apache and for IIS

The output is outside the repository on purpose: it holds the private key. Run it again after the server code changes; the
private folder keeps its name and config.php is left as you filled it in.

.PARAMETER Folder
Where supporter-private.pem is, and where upload\ is written. Default: SpaceSharp-supporter in your user folder.
#>
param(
    [string]$Folder = (Join-Path $HOME 'SpaceSharp-supporter')
)

$ErrorActionPreference = 'Stop'
$plain = New-Object Text.UTF8Encoding $false

$privateKey = Join-Path $Folder 'supporter-private.pem'
if (-not (Test-Path $privateKey)) {
    throw "There is no private key at $privateKey. Run new-keys.ps1 first, or restore the key from your password manager."
}

$server = Join-Path $PSScriptRoot 'server'
$upload = Join-Path $Folder 'upload\supporter'
New-Item -ItemType Directory -Force -Path $upload | Out-Null

foreach ($name in 'hook.php', 'thanks.php', 'lib.php', 'where.php', '.htaccess') {
    Copy-Item (Join-Path $server $name) (Join-Path $upload $name) -Force
}

# The private folder keeps its name between runs, so the orders on the server stay where the code looks for them.
$private = Get-ChildItem $upload -Directory -Filter 'private-*' | Select-Object -First 1 -ExpandProperty FullName
if (-not $private) {
    $bytes = New-Object byte[] 12
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    $private = Join-Path $upload ('private-' + (($bytes | ForEach-Object { $_.ToString('x2') }) -join ''))
    New-Item -ItemType Directory -Force -Path $private | Out-Null
}

# A web server asked for key.php runs its first line and sends back nothing.
$guard = "<?php exit; ?>`n"
[IO.File]::WriteAllText((Join-Path $private 'key.php'), $guard + [IO.File]::ReadAllText($privateKey), $plain)

$config = Join-Path $private 'config.php'
if (-not (Test-Path $config)) {
    Copy-Item (Join-Path $server 'config.sample.php') $config
}

[IO.File]::WriteAllText((Join-Path $private '.htaccess'), "Require all denied`n", $plain)
[IO.File]::WriteAllText((Join-Path $private 'web.config'), @'
<?xml version="1.0" encoding="UTF-8"?>
<configuration>
  <system.webServer>
    <security>
      <authorization>
        <remove users="*" roles="" verbs="" />
        <add accessType="Deny" users="*" />
      </authorization>
    </security>
  </system.webServer>
</configuration>
'@, $plain)

Write-Host "Upload folder: $upload"
Write-Host "Settings:      $config"
Write-Host '               Fill in webhook_secret and product_id there, then upload the supporter folder to the site.'
Write-Host 'Upload only; never synchronize with deleting, or the orders on the server are removed.'
