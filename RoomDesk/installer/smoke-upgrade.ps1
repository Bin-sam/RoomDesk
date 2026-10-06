param([Parameter(Mandatory=$true)][string]$Installer)
$ErrorActionPreference='Stop'
$root=Join-Path $env:RUNNER_TEMP ('RoomDesk-upgrade-'+[guid]::NewGuid())
New-Item -ItemType Directory $root|Out-Null
$old=Join-Path $root 'RoomDesk-Setup-0.7.0-win-x64.exe'
$target=Join-Path $root '安装目录 with spaces';$database=Join-Path $root '住客记录.db';$snapshot=Join-Path $root 'before.json';$backup=Join-Path $root 'before.db'
$p=$null;$helper=$null
try {
    gh release download roomdesk-v0.7.0-preview.1 --repo Bin-sam/RoomDesk --pattern RoomDesk-Setup-0.7.0-win-x64.exe --dir $root
    if($LASTEXITCODE -ne 0){throw 'Previous release download failed'}
    if((Get-FileHash $old -Algorithm SHA256).Hash.ToLower() -ne 'aab143e0ffd1dc5aa8d9999ef2ebecff37e9e0990da521f4791063d76cda2636'){throw 'Previous release checksum mismatch'}
    $setup=Start-Process $old -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',('/DIR="'+$target+'"')) -PassThru
    if(!$setup.WaitForExit(120000) -or $setup.ExitCode -ne 0){throw 'Previous release installation failed'}
    $application=Join-Path $target 'RoomDesk.exe'
    $p=Start-Process $application -ArgumentList @('--data',('"'+$database+'"')) -PassThru
    python "$PSScriptRoot/wait-initialized.py" $database
    if($LASTEXITCODE -ne 0){throw 'Old app initialization failed'}
    $p.Refresh();$p.CloseMainWindow()|Out-Null;if(!$p.WaitForExit(10000)){throw 'Old app failed to close'}
    python "$PSScriptRoot/upgrade-fixture.py" prepare $database $snapshot $backup
    if($LASTEXITCODE -ne 0){throw 'Fixture failed'}
    $p=Start-Process $application -ArgumentList @('--data',('"'+$database+'"')) -PassThru
    $deadline=(Get-Date).AddSeconds(30)
    do{Start-Sleep -Milliseconds 100;$p.Refresh()}while($p.MainWindowHandle -eq 0 -and !$p.HasExited -and (Get-Date) -lt $deadline)
    if($p.HasExited -or $p.MainWindowHandle -eq 0){throw 'Old-version window missing'}
    $plan=Join-Path $root 'plan.json'
    @{ProcessId=$p.Id;Application=$application;Database=$database;Installer=$Installer;InstallDirectory=$target;Backup=$backup;Sha256=(Get-FileHash $Installer -Algorithm SHA256).Hash;Version='roomdesk-v0.8.0-preview.1'}|ConvertTo-Json|Set-Content $plan -Encoding UTF8
    # Exact helper source embedded in the new app; run with the same Windows PowerShell used by the UI.
    $script=Join-Path $PSScriptRoot '../Windows/Resources/update.ps1'
    $powershell=Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
    $helper=Start-Process $powershell -ArgumentList @('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',('"'+$script+'"'),'-PlanPath',('"'+$plan+'"'),'-NoErrorDialog') -PassThru
    $deadline=(Get-Date).AddSeconds(20)
    while(!(Test-Path (Join-Path $root 'ready'))){if($helper.HasExited -or (Get-Date) -gt $deadline){throw 'Update helper handshake failed'};Start-Sleep -Milliseconds 100}
    $p.CloseMainWindow()|Out-Null;if(!$p.WaitForExit(10000)){throw 'Old app did not exit for upgrade'}
    if(!$helper.WaitForExit(210000) -or $helper.ExitCode -ne 0){if(Test-Path (Join-Path $root 'result.json')){Get-Content (Join-Path $root 'result.json')};throw 'Update helper failed'}
    $result=Get-Content (Join-Path $root 'result.json') -Raw|ConvertFrom-Json
    if(!$result.ok){throw 'Update reported failure'}
    $deadline=(Get-Date).AddSeconds(40);$newApp=$null
    do{Start-Sleep -Milliseconds 200;$newApp=Get-Process RoomDesk -ErrorAction SilentlyContinue|Where-Object{$_.Path -eq $application -and $_.MainWindowHandle -ne 0}|Select-Object -First 1}while($null -eq $newApp -and (Get-Date) -lt $deadline)
    if($null -eq $newApp){throw 'Updated application did not restart with a window'}
    $p=$newApp
    if(!([Diagnostics.FileVersionInfo]::GetVersionInfo($application).ProductVersion.StartsWith('0.8.0'))){throw 'Wrong installed version'}
    python "$PSScriptRoot/upgrade-fixture.py" verify $database $snapshot $backup
    if($LASTEXITCODE -ne 0){throw 'Upgrade altered hotel data'}
    Write-Output 'PASS: update helper installs 0.8.0 over 0.7.0, automatically restarts, and preserves data.'
} finally {
    if($null -ne $helper -and !$helper.HasExited){$helper.Kill($true)}
    if($null -ne $p -and !$p.HasExited){$p.CloseMainWindow()|Out-Null;if(!$p.WaitForExit(5000)){$p.Kill()}}
    $reports=Join-Path $PSScriptRoot '../artifacts/upgrade-validation';New-Item -ItemType Directory -Force $reports|Out-Null
    foreach($name in @('result.json','install.log')){if(Test-Path (Join-Path $root $name)){Copy-Item (Join-Path $root $name) $reports}}
}
