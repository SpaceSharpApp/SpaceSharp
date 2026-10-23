# Runs the layout audit: the app poses every screen and dialog on the pretend PC, left-to-right and mirrored, at the default
# and the minimum window size, and lists every label that is wider than the room it was given. One report per language.
#
#   .\tools\layout-audit.ps1                      every language the app ships (the Strings.*.resx files, plus English)
#   .\tools\layout-audit.ps1 -Languages de,pl     just those
#   .\tools\layout-audit.ps1 -Resume             skips languages whose report is already in the output folder
#   .\tools\layout-audit.ps1 -Out C:\temp\audit   reports elsewhere (default: audit\ under the repository, ignored by git)
#
# Exit code: the number of distinct clipped labels across all languages (0 is clean), so it can gate a script.
# Windows only, since the app has to open its windows; it does not need the screen captured, so it can run on a second monitor.
param(
    [string[]] $Languages = @(),
    [string] $Out = "",
    [switch] $Resume,
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root "SpaceSharp\SpaceSharp.csproj"
if ($Out -eq "") { $Out = Join-Path $root "audit" }
if ($Languages.Count -eq 0) {
    # English first, then every translation file: Strings.de.resx -> de, Strings.zh-Hans.resx -> zh-Hans
    $Languages = @("en") + (Get-ChildItem (Join-Path $root "SpaceSharp.Core\Resources") -Filter "Strings.*.resx" | ForEach-Object { $_.BaseName.Substring("Strings.".Length) } | Sort-Object)
}
if (-not (Test-Path $Out)) { New-Item -ItemType Directory -Path $Out | Out-Null }

Write-Host "Building $Configuration..."
dotnet build $project -c $Configuration --nologo -v quiet | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Build failed." }
$exe = Get-ChildItem (Join-Path $root "SpaceSharp\bin\$Configuration") -Recurse -Filter "SpaceSharp.exe" | Where-Object { $_.FullName -notmatch "\\publish\\" } | Select-Object -First 1
if (-not $exe) { throw "SpaceSharp.exe not found under SpaceSharp\bin\$Configuration." }

$total = 0
foreach ($lang in $Languages) {
    $report = Join-Path $Out "layout-audit-$lang.txt"
    if ($Resume -and (Test-Path $report)) {
        $first = Get-Content $report -TotalCount 1
        Write-Host "  $lang skipped, report exists: $first"
        if ($first -match '(\d+) distinct') { $total += [int]$Matches[1] }
        continue
    }
    Write-Host "  $lang -> $report"
    while (Get-Process SpaceSharp -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 300 }
    & $exe.FullName --audit $report --lang $lang | Out-Null
    $proc = Get-Process SpaceSharp -ErrorAction SilentlyContinue
    if ($proc) { $proc | Wait-Process -Timeout 300 }
    if (Test-Path $report) {
        $first = Get-Content $report -TotalCount 1
        Write-Host "    $first"
        if ($first -match '(\d+) distinct') { $total += [int]$Matches[1] }
    } else { Write-Warning "    no report written for $lang" }
}
Write-Host "Done: $total distinct clipped label(s) across $($Languages.Count) language(s). Reports in $Out."
exit $total
