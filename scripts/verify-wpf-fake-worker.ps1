param(
    [string]$Gui = "artifacts\publish\win-x64\iPhoneMirror.exe",
    [string]$Cli = "artifacts\publish\win-x64\iphone-mirror.exe",
    [string]$Python = "artifacts\publish\win-x64\runtime\python\python.exe",
    [string]$Worker = "scripts\fake-worker-wpf-smoke.py",
    [ValidateSet("normal", "error", "crash", "reconnect", "missing-stack", "locked", "untrusted", "developer-mode")]
    [string]$Mode = "normal"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$guiPath = [IO.Path]::GetFullPath((Join-Path $repoRoot $Gui))
$cliPath = [IO.Path]::GetFullPath((Join-Path $repoRoot $Cli))
$pythonPath = [IO.Path]::GetFullPath((Join-Path $repoRoot $Python))
$workerPath = [IO.Path]::GetFullPath((Join-Path $repoRoot $Worker))

foreach ($path in @($guiPath, $cliPath, $pythonPath, $workerPath)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Smoke-test input is missing: $path" }
}
if (Get-Process -Name iPhoneMirror -ErrorAction SilentlyContinue) {
    throw "Stop every iPhoneMirror.exe process before running the fake-worker WPF smoke test."
}

$guiInfo = [Diagnostics.ProcessStartInfo]::new()
$guiInfo.FileName = $guiPath
$guiInfo.WorkingDirectory = Split-Path -Parent $guiPath
$guiInfo.UseShellExecute = $false
$guiInfo.CreateNoWindow = $true
$guiInfo.ArgumentList.Add("start")
$guiInfo.ArgumentList.Add("--connection")
$guiInfo.ArgumentList.Add("usb")
$guiInfo.ArgumentList.Add("--serial")
$guiInfo.ArgumentList.Add("fake-wpf-device")
$guiInfo.Environment["IPHONE_MIRROR_TEST_MODE"] = "1"
$guiInfo.Environment["IPHONE_MIRROR_TEST_PYTHON"] = $pythonPath
$guiInfo.Environment["IPHONE_MIRROR_TEST_WORKER"] = $workerPath
$guiInfo.Environment["IPHONE_MIRROR_FAKE_MODE"] = $Mode

$guiProcess = New-Object System.Diagnostics.Process
$guiProcess.StartInfo = $guiInfo
[void]$guiProcess.Start()

try {
    $status = $null
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        Start-Sleep -Milliseconds 250
        try {
            $status = & $cliPath status 2>$null | ConvertFrom-Json
            if ($status.state -eq "running") { break }
        } catch { }
    }
    if ($null -eq $status -or $status.state -ne "running" -or $status.serial -ne "fake-wpf-device") {
        throw "Fake-worker GUI did not reach running state."
    }

    $faultState = $null
    if ($Mode -ne "normal") {
        for ($attempt = 0; $attempt -lt 40; $attempt++) {
            Start-Sleep -Milliseconds 250
            try {
                $faultState = & $cliPath status 2>$null | ConvertFrom-Json
                if ($faultState.state -eq "stopped") { break }
            } catch { }
        }
        if ($null -eq $faultState -or $faultState.state -ne "stopped") {
            throw "Fake-worker $Mode scenario did not leave the GUI stopped."
        }
        $expectedFaultCode = @{
            "missing-stack" = "apple_device_support_missing"
            "locked" = "iphone_locked"
            "untrusted" = "usb_trust_required"
            "developer-mode" = "developer_mode_required"
        }[$Mode]
        if ($null -ne $expectedFaultCode -and $faultState.error_code -ne $expectedFaultCode) {
            throw "$Mode scenario returned the wrong error code: $($faultState.error_code)"
        }
    }

    $reconnectState = $null
    $reconnectExit = 0
    if ($Mode -eq "reconnect") {
        & $cliPath start --connection usb --serial fake-wpf-device 2>$null
        $reconnectExit = $LASTEXITCODE
        if ($reconnectExit -ne 0) { throw "Reconnect start command exited with $reconnectExit." }
        for ($attempt = 0; $attempt -lt 40; $attempt++) {
            Start-Sleep -Milliseconds 250
            try {
                $reconnectState = & $cliPath status 2>$null | ConvertFrom-Json
                if ($reconnectState.state -eq "running") { break }
            } catch { }
        }
        if ($null -eq $reconnectState -or $reconnectState.state -ne "running") {
            throw "Reconnect did not return the fake-worker GUI to running state."
        }
        $reconnectProcessCount = @(Get-Process -Name iPhoneMirror -ErrorAction SilentlyContinue).Count
        if ($reconnectProcessCount -ne 1) { throw "Reconnect created an unexpected process count: $reconnectProcessCount" }
    }

    $restartExit = 0
    if ($Mode -eq "normal") {
        & $cliPath restart --connection usb --serial fake-wpf-device 2>$null
        $restartExit = $LASTEXITCODE
        if ($restartExit -ne 0) { throw "Secondary restart command exited with $restartExit." }
        $restartState = $null
        for ($attempt = 0; $attempt -lt 40; $attempt++) {
            Start-Sleep -Milliseconds 250
            try {
                $restartState = & $cliPath status 2>$null | ConvertFrom-Json
                if ($restartState.state -eq "running") { break }
            } catch { }
        }
        if ($null -eq $restartState -or $restartState.state -ne "running") {
            $restartStateText = if ($null -eq $restartState) { "<no-status>" } else { $restartState | ConvertTo-Json -Compress }
            throw "Restart did not return the fake-worker GUI to running state: $restartStateText"
        }

        & $cliPath start --connection usb --serial fake-wpf-device 2>$null
        $secondStartExit = $LASTEXITCODE
        if ($secondStartExit -ne 0) { throw "Secondary start command exited with $secondStartExit." }
        $mirrorProcessCount = @(Get-Process -Name iPhoneMirror -ErrorAction SilentlyContinue).Count
        if ($mirrorProcessCount -ne 1) { throw "Secondary start created an unexpected process count: $mirrorProcessCount" }
    }
    if ($restartExit -ne 0) { throw "Secondary restart command exited with $restartExit." }

    & $cliPath stop 2>$null
    $stopExit = $LASTEXITCODE
    if ($stopExit -ne 0) { throw "Secondary stop command exited with $stopExit." }
    if (-not $guiProcess.WaitForExit(15000)) {
        $guiProcess.Kill($true)
        throw "WPF process did not exit after stop command."
    }

    $faultStateText = "not-applicable"
    $faultErrorCode = "not-applicable"
    if ($null -ne $faultState) { $faultStateText = $faultState.state }
    if ($null -ne $faultState -and $null -ne $faultState.error_code) { $faultErrorCode = $faultState.error_code }
    $processCountText = "not-applicable"
    if ($Mode -eq "normal") { $processCountText = $mirrorProcessCount }
    [pscustomobject]@{
        InitialState = $status.state
        Mode = $Mode
        FaultState = $faultStateText
        FaultErrorCode = $faultErrorCode
        Serial = $status.serial
        SecondStartProcessCount = $processCountText
        RestartExit = $restartExit
        ReconnectExit = $reconnectExit
        ReconnectState = if ($null -eq $reconnectState) { "not-applicable" } else { $reconnectState.state }
        StopExit = $stopExit
        GuiExit = $guiProcess.ExitCode
    } | ConvertTo-Json -Compress
}
finally {
    if (-not $guiProcess.HasExited) {
        $guiProcess.Kill($true)
        $guiProcess.WaitForExit()
    }
}
