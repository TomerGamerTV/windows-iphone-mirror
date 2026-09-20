param(
    [string]$OutputRoot = "artifacts/runtime",
    [string]$CacheRoot = "artifacts/cache"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$outputRoot = Join-Path $repoRoot $OutputRoot
$cacheRoot = Join-Path $repoRoot $CacheRoot
$pythonRoot = Join-Path $outputRoot "python"
$mpvRoot = Join-Path $outputRoot "mpv"

$pythonVersion = "3.14.0"
$pythonArchiveName = "python-$pythonVersion-embed-amd64.zip"
$pythonUri = "https://www.python.org/ftp/python/$pythonVersion/$pythonArchiveName"
$pythonSha256 = "8D4D3590C10449D78AA4375F534E6D5F3027D67FDC362DD1A882279DB6F90FDF"

$mpvTag = "20260903"
$mpvArchiveName = "mpv-x86_64-20260903-git-69e63f425a.7z"
$mpvUri = "https://github.com/shinchiro/mpv-winbuild-cmake/releases/download/$mpvTag/$mpvArchiveName"
$mpvSha256 = "418DBFB5FEB851CBED33D6C05D8481BA71802621BFD6EFE8974522B28D42AC97"

function Get-VerifiedArtifact {
    param(
        [string]$Uri,
        [string]$Destination,
        [string]$Sha256
    )

    if (-not (Test-Path $Destination)) {
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Destination) | Out-Null
        Invoke-WebRequest -UseBasicParsing -Uri $Uri -OutFile $Destination
    }

    $actual = (Get-FileHash -Algorithm SHA256 -Path $Destination).Hash
    if ($actual -ne $Sha256) {
        throw "SHA-256 mismatch for $(Split-Path -Leaf $Destination). Expected $Sha256, got $actual."
    }
}

$pythonArchive = Join-Path $cacheRoot $pythonArchiveName
$mpvArchive = Join-Path $cacheRoot $mpvArchiveName
Get-VerifiedArtifact -Uri $pythonUri -Destination $pythonArchive -Sha256 $pythonSha256
Get-VerifiedArtifact -Uri $mpvUri -Destination $mpvArchive -Sha256 $mpvSha256

if (Test-Path $pythonRoot) { Remove-Item -Recurse -Force $pythonRoot }
if (Test-Path $mpvRoot) { Remove-Item -Recurse -Force $mpvRoot }
New-Item -ItemType Directory -Force -Path $pythonRoot,$mpvRoot | Out-Null

Expand-Archive -Path $pythonArchive -DestinationPath $pythonRoot -Force
$pthFile = Join-Path $pythonRoot "python314._pth"
$pth = Get-Content $pthFile
$pth = $pth | ForEach-Object {
    if ($_ -eq "#import site") { "import site" } else { $_ }
}
if ($pth -notcontains "Lib\site-packages") {
    $pth += "Lib\site-packages"
}
Set-Content -Path $pthFile -Value $pth -Encoding ascii

$sitePackages = Join-Path $pythonRoot "Lib\site-packages"
New-Item -ItemType Directory -Force -Path $sitePackages | Out-Null
$devPython = Join-Path $repoRoot "worker\.venv\Scripts\python.exe"
if (-not (Test-Path $devPython)) {
    throw "worker/.venv is missing. Create it with Python 3.14 and install worker/requirements.txt first."
}

& $devPython -m pip install --disable-pip-version-check --no-warn-script-location --upgrade `
    --target $sitePackages -r (Join-Path $repoRoot "worker\requirements.txt")
if ($LASTEXITCODE -ne 0) { throw "Installing worker dependencies into the private runtime failed." }

$sevenZip = Get-Command 7z.exe -ErrorAction SilentlyContinue
if (-not $sevenZip) { $sevenZip = Get-Command 7z -ErrorAction SilentlyContinue }
if (-not $sevenZip) { throw "7-Zip is required only on the build machine to unpack the pinned MPV archive." }
& $sevenZip.Source x -y "-o$mpvRoot" $mpvArchive | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Extracting MPV failed." }

foreach ($unneeded in @("installer", "doc", "mpv.com", "mpv-register.bat", "mpv-unregister.bat", "updater.bat")) {
    $path = Join-Path $mpvRoot $unneeded
    if (Test-Path $path) { Remove-Item -Recurse -Force $path }
}

$embeddedPython = Join-Path $pythonRoot "python.exe"
& $embeddedPython -c "import pymobiledevice3, importlib.metadata; assert importlib.metadata.version('pymobiledevice3') == '11.13.1'; print('private-python-ok')"
if ($LASTEXITCODE -ne 0) { throw "The private Python runtime could not import pymobiledevice3==11.13.1." }

$mpvExe = Join-Path $mpvRoot "mpv.exe"
if (-not (Test-Path $mpvExe)) { throw "The pinned MPV archive did not contain mpv.exe." }
& $mpvExe --no-config --no-terminal --version | Select-Object -First 1
if ($LASTEXITCODE -ne 0) { throw "The bundled MPV executable did not start." }

Copy-Item -Force (Join-Path $pythonRoot "LICENSE.txt") (Join-Path $outputRoot "PYTHON_LICENSE.txt")
@"
MPV runtime build: $mpvArchiveName
Source: https://github.com/shinchiro/mpv-winbuild-cmake/releases/tag/$mpvTag
MPV is licensed under GPL-2.0-or-later by default; bundled components retain their own licenses.
See https://github.com/mpv-player/mpv/blob/master/Copyright for upstream copyright and license details.
"@ | Set-Content -Path (Join-Path $outputRoot "MPV_NOTICE.txt") -Encoding utf8

Write-Output "Prepared private Python $pythonVersion and MPV $mpvTag under $outputRoot"
