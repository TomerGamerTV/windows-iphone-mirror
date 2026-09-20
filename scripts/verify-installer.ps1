param(
    [string]$Installer = "artifacts\installer\iPhoneMirror-Setup-x64.exe",
    [string]$Root = "artifacts\.installer-check"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$installerPath = [IO.Path]::GetFullPath((Join-Path $repoRoot $Installer))
$checkRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot $Root))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts"))

if (-not $checkRoot.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Installer check root must stay under artifacts."
}
if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "Installer was not found: $installerPath"
}
if (Get-Process -Name iPhoneMirror -ErrorAction SilentlyContinue) {
    throw "Stop every iPhoneMirror.exe process before running isolated installer verification."
}

function Stop-InstallerTestProcess([Diagnostics.Process]$Process) {
    if ($Process.HasExited) { return }
    & taskkill.exe /PID ([string]$Process.Id) /T /F 2>$null | Out-Null
    $Process.WaitForExit()
}

$installDir = Join-Path $checkRoot "install"
$logPath = Join-Path $checkRoot "uninstall.log"

try {
    if (Test-Path -LiteralPath $checkRoot) {
        Remove-Item -LiteralPath $checkRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Path $checkRoot | Out-Null

    $installerProcess = Start-Process -FilePath $installerPath -ArgumentList @(
        "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/DIR=$installDir"
    ) -Wait -PassThru
    if ($installerProcess.ExitCode -ne 0) {
        throw "Installer exited with $($installerProcess.ExitCode)."
    }

    # Exercise the in-place upgrade path against the same installation
    # directory. This catches installer metadata, locked-file, and shortcut
    # replacement regressions that a first-install check cannot see.
    $updateProcess = Start-Process -FilePath $installerPath -ArgumentList @(
        "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/DIR=$installDir"
    ) -Wait -PassThru
    if ($updateProcess.ExitCode -ne 0) {
        throw "In-place update exited with $($updateProcess.ExitCode)."
    }

    $guiPath = Join-Path $installDir "iPhoneMirror.exe"
    $cliPath = Join-Path $installDir "iphone-mirror.exe"
    $uninstallerPath = Join-Path $installDir "unins000.exe"
    foreach ($path in @($guiPath, $cliPath, $uninstallerPath)) {
        if (-not (Test-Path -LiteralPath $path)) {
            throw "Installed artifact is missing: $path"
        }
    }

    $cliInfo = [Diagnostics.ProcessStartInfo]::new()
    $cliInfo.FileName = $cliPath
    $cliInfo.Arguments = "status"
    $cliInfo.UseShellExecute = $false
    $cliInfo.CreateNoWindow = $true
    $cliInfo.RedirectStandardOutput = $true
    $cliInfo.RedirectStandardError = $true
    $cliInfo.Environment["Path"] = "C:\Windows\System32"
    $cliProcess = [Diagnostics.Process]::new()
    $cliProcess.StartInfo = $cliInfo
    [void]$cliProcess.Start()
    if (-not $cliProcess.WaitForExit(15000)) {
        Stop-InstallerTestProcess $cliProcess
        throw "Installed CLI did not exit within 15 seconds."
    }
    $cliOutput = $cliProcess.StandardOutput.ReadToEnd().Trim()
    if ($cliProcess.ExitCode -ne 0 -or $cliOutput -notmatch '"running"\s*:') {
        throw "Installed CLI validation failed: $cliOutput"
    }

    $uninstallerProcess = Start-Process -FilePath $uninstallerPath -ArgumentList @(
        "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/LOG=$logPath"
    ) -Wait -PassThru
    if ($uninstallerProcess.ExitCode -ne 0) {
        throw "Uninstaller exited with $($uninstallerProcess.ExitCode)."
    }
    Start-Sleep -Seconds 2
    if (Test-Path -LiteralPath $installDir) {
        throw "Uninstaller left the install directory behind."
    }

    [pscustomobject]@{
        Installer = $installerPath
        InstalledCliStatus = $cliOutput
        InstallerExit = $installerProcess.ExitCode
        UpdateInstallerExit = $updateProcess.ExitCode
        UninstallerExit = $uninstallerProcess.ExitCode
        InstallDirectoryRemoved = $true
    } | ConvertTo-Json -Compress
}
finally {
    if (Test-Path -LiteralPath $checkRoot) {
        Remove-Item -LiteralPath $checkRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
