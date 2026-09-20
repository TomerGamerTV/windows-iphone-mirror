param([string]$Configuration = "Release")

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
& (Join-Path $repoRoot "scripts\publish-windows.ps1") -Configuration $Configuration -SkipRuntimePreparation
if ($LASTEXITCODE -ne 0) { throw "Publishing failed." }

$iscc = Get-Command ISCC.exe -ErrorAction SilentlyContinue
if (-not $iscc) {
    $candidate = Join-Path ${env:LOCALAPPDATA} "Programs\Inno Setup 6\ISCC.exe"
    if (Test-Path $candidate) { $iscc = Get-Item $candidate }
}
if (-not $iscc) {
    $candidate = Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"
    if (Test-Path $candidate) { $iscc = Get-Item $candidate }
}
if (-not $iscc) {
    throw "Inno Setup 6 compiler (ISCC.exe) is required on the build machine. End users do not need it."
}

New-Item -ItemType Directory -Force -Path (Join-Path $repoRoot "artifacts\installer") | Out-Null
& $iscc.FullName (Join-Path $PSScriptRoot "iPhoneMirror.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed." }

$installer = Join-Path $repoRoot "artifacts\installer\iPhoneMirror-Setup-x64.exe"
if (-not (Test-Path $installer)) { throw "Installer output was not created." }
Write-Output "Built installer: $installer"
