param(
    [string]$Publish = "artifacts/publish/win-x64",
    [switch]$RequireInstaller
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$publishDir = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $Publish))
if (-not (Test-Path -LiteralPath $publishDir -PathType Container)) {
    throw "Published directory is missing: $publishDir"
}

$required = @(
    "iPhoneMirror.exe",
    "iphone-mirror.exe",
    "worker\worker.py",
    "runtime\python\python.exe",
    "runtime\mpv\mpv.exe",
    "LICENSE.txt",
    "THIRD_PARTY_NOTICES.md",
    "PYTHON_LICENSE.txt",
    "MPV_NOTICE.txt"
)
foreach ($relative in $required) {
    $path = Join-Path $publishDir $relative
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Published artifact is missing: $relative"
    }
    if ((Get-Item -LiteralPath $path).Length -le 0) {
        throw "Published artifact is empty: $relative"
    }
}

$python = Join-Path $publishDir "runtime\python\python.exe"
$version = (& $python -c "import importlib.metadata; print(importlib.metadata.version('pymobiledevice3'))").Trim()
if ($LASTEXITCODE -ne 0 -or $version -ne "11.13.1") {
    throw "Bundled pymobiledevice3 version is '$version', expected 11.13.1."
}

$mpv = Join-Path $publishDir "runtime\mpv\mpv.exe"
$mpvVersion = (& $mpv --version | Select-Object -First 1).Trim()
if ($LASTEXITCODE -ne 0 -or $mpvVersion -notmatch "^mpv v") {
    throw "Bundled MPV did not report a version."
}

if ($RequireInstaller) {
    $installer = Join-Path $repoRoot "artifacts\installer\iPhoneMirror-Setup-x64.exe"
    if (-not (Test-Path -LiteralPath $installer -PathType Leaf) -or (Get-Item -LiteralPath $installer).Length -le 0) {
        throw "Installer artifact is missing or empty."
    }
}

Write-Output "Verified self-contained publish: $publishDir"
Write-Output "pymobiledevice3: $version"
Write-Output $mpvVersion
