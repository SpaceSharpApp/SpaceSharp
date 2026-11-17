$ErrorActionPreference = 'Stop'

$packageArgs = @{
  packageName    = $env:ChocolateyPackageName
  fileType       = 'msi'
  softwareName   = 'SpaceSharp*'
  url            = '__URL32__'
  checksum       = '__SHA32__'
  checksumType   = 'sha256'
  url64bit       = '__URL64__'
  checksum64     = '__SHA64__'
  checksumType64 = 'sha256'
  silentArgs     = '/qn /norestart'
  validExitCodes = @(0, 1641, 3010)
}

Install-ChocolateyPackage @packageArgs
