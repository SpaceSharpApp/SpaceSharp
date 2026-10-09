# brand-msi.ps1
# Finishes the MSI that "vpk pack" produced in .\Releases:
#   1. writes the branded dialog images (installer\banner.bmp, installer\logo.bmp)
#   2. fixes the desktop shortcut description, which vpk 1.2.158 leaves as a placeholder
#   3. registers the app icon so Apps & features and the installer show the mark instead of the generic MSI icon
# Works from any folder; the release script runs it once per MSI:  .\installer\brand-msi.ps1 -Msi .\Releases\SpaceSharp-win-x64.msi
# Without -Msi it brands every .msi in Releases (x64 and x86). Running it twice on the same file is harmless.

param([string]$Msi)

$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot -Parent
if ($Msi) { $targets = @((Resolve-Path $Msi).Path) }
else {
    $targets = @(Get-Item (Join-Path $root "Releases\*.msi") -ErrorAction SilentlyContinue | ForEach-Object FullName)
    if ($targets.Count -eq 0) { throw "No .msi found in $root\Releases. Run vpk pack first." }
}

$banner      = (Resolve-Path (Join-Path $PSScriptRoot "banner.bmp")).Path
$logo        = (Resolve-Path (Join-Path $PSScriptRoot "logo.bmp")).Path
$icon        = (Resolve-Path (Join-Path $root "SpaceSharp\Assets\SpaceSharp.ico")).Path
$description = "See where your disk space went"

# Windows Installer's COM object has no type info in PowerShell 7, so call it through reflection.
function Call($obj, $method, $params) {
    $obj.GetType().InvokeMember($method, [System.Reflection.BindingFlags]::InvokeMethod, $null, $obj, [object[]]$params)
}
function Prop($obj, $name, $params) {
    $obj.GetType().InvokeMember($name, [System.Reflection.BindingFlags]::GetProperty, $null, $obj, [object[]]$params)
}
function Run-Sql($db, $sql, $record = $null) {
    $view = Call $db "OpenView" @($sql)
    Call $view "Execute" @($record) | Out-Null
    Call $view "Close" @() | Out-Null
}
function Query-Column($db, $sql) {
    $view = Call $db "OpenView" @($sql)
    Call $view "Execute" @($null) | Out-Null
    $values = @()
    while (($rec = Call $view "Fetch" @()) -ne $null) { $values += (Prop $rec "StringData" @(1)) }
    Call $view "Close" @() | Out-Null
    return $values
}

$installer = New-Object -ComObject WindowsInstaller.Installer

foreach ($msi in $targets) {
    "== $([IO.Path]::GetFileName($msi))"
    $db = Call $installer "OpenDatabase" @($msi, 1)   # 1 = transact

    # ---- 1. dialog images -------------------------------------------------------
    $images = @{
        "WixUI_Bmp_Banner" = $banner; "WixUI_Bmp_Dialog" = $logo   # WiX 4/5 names
        "WixUIBannerBmp"   = $banner; "WixUIDialogBmp"   = $logo   # WiX 3 names
    }
    $binaries = Query-Column $db "SELECT Name FROM Binary"
    $replaced = 0
    foreach ($name in $binaries) {
        if (-not $images.ContainsKey($name)) { continue }
        $record = Call $installer "CreateRecord" @(1)
        Call $record "SetStream" @(1, $images[$name]) | Out-Null
        Run-Sql $db "UPDATE Binary SET Data = ? WHERE Name = '$name'" $record
        "Replaced image $name"
        $replaced++
    }
    if ($replaced -eq 0) {
        "No dialog images found. Binary table contains: $($binaries -join ', ')"
    }

    # ---- 2. desktop shortcut description -----------------------------------------
    $hasProperty = (Query-Column $db "SELECT Property FROM Property WHERE Property = 'MsiDesktopShortcutDescription'").Count -gt 0
    if ($hasProperty) {
        Run-Sql $db "UPDATE Property SET Value = '$description' WHERE Property = 'MsiDesktopShortcutDescription'"
    } else {
        Run-Sql $db "INSERT INTO Property (Property, Value) VALUES ('MsiDesktopShortcutDescription', '$description')"
    }
    # In case the placeholder text is stored directly in the shortcut instead of through the property.
    Run-Sql $db "UPDATE Shortcut SET Description = '$description' WHERE Description = '[MsiDesktopShortcutDescription]'"
    "Set desktop shortcut description"

    # ---- 3. product icon --------------------------------------------------------
    $iconName = "SpaceSharp.ico"
    $hasIcon = (Query-Column $db "SELECT Name FROM Icon WHERE Name = '$iconName'").Count -gt 0
    $record = Call $installer "CreateRecord" @(1)
    Call $record "SetStream" @(1, $icon) | Out-Null
    if ($hasIcon) { Run-Sql $db "UPDATE Icon SET Data = ? WHERE Name = '$iconName'" $record }
    else          { Run-Sql $db "INSERT INTO Icon (Name, Data) VALUES ('$iconName', ?)" $record }
    $hasArp = (Query-Column $db "SELECT Property FROM Property WHERE Property = 'ARPPRODUCTICON'").Count -gt 0
    if ($hasArp) { Run-Sql $db "UPDATE Property SET Value = '$iconName' WHERE Property = 'ARPPRODUCTICON'" }
    else         { Run-Sql $db "INSERT INTO Property (Property, Value) VALUES ('ARPPRODUCTICON', '$iconName')" }
    "Set product icon"

    Call $db "Commit" @() | Out-Null

    $record = $null; $view = $null; $db = $null
    "Done: $msi"
}

# Release the files so Windows Installer can open them afterwards.
$installer = $null
[GC]::Collect(); [GC]::WaitForPendingFinalizers()
