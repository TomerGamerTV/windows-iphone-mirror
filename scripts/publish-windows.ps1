param(
    [string]$Configuration = "Release",
    [string]$Output = "artifacts/publish/win-x64",
    [switch]$SkipRuntimePreparation
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $SkipRuntimePreparation) {
    & (Join-Path $PSScriptRoot "prepare-runtime.ps1")
    if ($LASTEXITCODE -ne 0) { throw "Runtime preparation failed." }
}

$preparedPython = Join-Path $repoRoot "artifacts\runtime\python\python.exe"
$preparedMpv = Join-Path $repoRoot "artifacts\runtime\mpv\mpv.exe"
if (-not (Test-Path $preparedPython) -or -not (Test-Path $preparedMpv)) {
    throw "Prepared runtime is missing. Run scripts/prepare-runtime.ps1 first."
}

$publishDir = Join-Path $repoRoot $Output
if (Test-Path -LiteralPath $publishDir) {
    $removed = $false
    for ($attempt = 1; $attempt -le 8; $attempt++) {
        try {
            Remove-Item -LiteralPath $publishDir -Recurse -Force -ErrorAction Stop
            $removed = $true
            break
        }
        catch {
            if ($attempt -eq 8) { throw }
            Start-Sleep -Milliseconds (250 * $attempt)
        }
    }
    if (-not $removed -or (Test-Path -LiteralPath $publishDir)) {
        throw "Could not clear publish directory after retrying: $publishDir"
    }
}

dotnet publish (Join-Path $repoRoot "src\iPhoneMirror.App\iPhoneMirror.App.csproj") `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    --output $publishDir `
    -p:PublishSingleFile=false `
    -p:DebugType=None `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

dotnet publish (Join-Path $repoRoot "src\iPhoneMirror.Cli\iPhoneMirror.Cli.csproj") `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    --output $publishDir `
    -p:PublishSingleFile=false `
    -p:DebugType=None `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "CLI publish failed." }

Copy-Item -Force (Join-Path $repoRoot "LICENSE") (Join-Path $publishDir "LICENSE.txt")
Copy-Item -Force (Join-Path $repoRoot "THIRD_PARTY_NOTICES.md") (Join-Path $publishDir "THIRD_PARTY_NOTICES.md")
Copy-Item -Force (Join-Path $repoRoot "artifacts\runtime\PYTHON_LICENSE.txt") (Join-Path $publishDir "PYTHON_LICENSE.txt")
Copy-Item -Force (Join-Path $repoRoot "artifacts\runtime\MPV_NOTICE.txt") (Join-Path $publishDir "MPV_NOTICE.txt")

$privatePython = Join-Path $publishDir "runtime\python\python.exe"
& $privatePython -c "import pymobiledevice3, importlib.metadata; assert importlib.metadata.version('pymobiledevice3') == '11.13.1'"
if ($LASTEXITCODE -ne 0) { throw "Published private worker runtime validation failed." }

$required = @(
    "iPhoneMirror.exe",
    "iphone-mirror.exe",
    "worker\worker.py",
    "runtime\python\python.exe",
    "runtime\mpv\mpv.exe",
    "LICENSE.txt",
    "THIRD_PARTY_NOTICES.md"
)
foreach ($relative in $required) {
    if (-not (Test-Path (Join-Path $publishDir $relative))) {
        throw "Published output is missing $relative"
    }
}

Write-Output "Published self-contained Windows app to $publishDir"
