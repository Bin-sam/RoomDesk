param([Parameter(Mandatory=$true)][string]$Installer)
$ErrorActionPreference = 'Stop'
$target = Join-Path $env:RUNNER_TEMP 'RoomDesk-installed'
$data = Join-Path $env:LOCALAPPDATA 'RoomDeskPrototype\rooms-v1.db'
if (Test-Path $data) { throw 'Refusing to test with an existing user database.' }
$log = Join-Path $env:RUNNER_TEMP 'roomdesk-install.log'
function Wait-InstallerProcess($process, [string]$phase, [string]$logPath) {
    # Wait for the installer itself with a deadline, not an unbounded process-tree wait.
    for($attempt=0;$attempt -lt 12;$attempt++){
        if($process.WaitForExit(10000)){
            if($process.ExitCode -ne 0){throw "$phase failed: $($process.ExitCode)"}
            Write-Output "PASS: $phase exited successfully."
            return
        }
        Write-Output "$phase still running ($((1+$attempt)*10) seconds)."
        if(Test-Path $logPath){Get-Content $logPath -Tail 8 | Write-Output}
    }
    try { $process.Kill($true); $process.WaitForExit() } catch {}
    throw "$phase timed out after 120 seconds."
}
function Install-RoomDesk {
    Write-Output 'Starting silent installation.'
    $p = Start-Process -FilePath $Installer -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',"/DIR=`"$target`"","/LOG=`"$log`"") -PassThru
    Wait-InstallerProcess $p 'Installer' $log
    if (!(Test-Path "$target\RoomDesk.exe")) { throw 'Installed EXE is missing.' }
}
Install-RoomDesk
$p = Start-Process "$target\RoomDesk.exe" -PassThru
try {
    $ready = $false
    for ($i=0; $i -lt 60; $i++) {
        Start-Sleep -Milliseconds 500
        $p.Refresh()
        if ($p.HasExited) { throw "Installed app exited early: $($p.ExitCode)" }
        if ($p.MainWindowHandle -ne 0 -and (Test-Path $data)) { $ready = $true; break }
    }
    if (!$ready) { throw 'App did not create a window and database within 30 seconds.' }
    # A window/file can appear before async initialization completes; wait for all seed rows.
    python "$PSScriptRoot/wait-initialized.py" $data
    if ($LASTEXITCODE -ne 0) { throw 'Installed application database initialization did not complete.' }
    Write-Output 'PASS: installed EXE starts and initializes 24 rooms with a WPF window on Windows runner.'
} finally {
    if (!$p.HasExited) {
        $null = $p.CloseMainWindow()
        if (!$p.WaitForExit(10000)) { $p.Kill(); $p.WaitForExit() }
    }
}
$hash = (Get-FileHash $data -Algorithm SHA256).Hash
Install-RoomDesk
if ((Get-FileHash $data -Algorithm SHA256).Hash -ne $hash) { throw 'Reinstall modified the user database.' }
Write-Output 'PASS: reinstall preserves user database.'
$uninstallLog=Join-Path $env:RUNNER_TEMP 'roomdesk-uninstall.log'
$p = Start-Process "$target\unins000.exe" -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/LOG=`"$uninstallLog`"") -PassThru
Wait-InstallerProcess $p 'Uninstaller' $uninstallLog
if (Test-Path "$target\RoomDesk.exe") { throw 'Uninstall left installed EXE behind.' }
if (!(Test-Path $data) -or (Get-FileHash $data -Algorithm SHA256).Hash -ne $hash) { throw 'Uninstall did not preserve user database.' }
Write-Output 'PASS: uninstall removes app and preserves user database.'
