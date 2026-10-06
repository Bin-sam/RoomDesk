param([Parameter(Mandatory=$true)][string]$PlanPath,[switch]$NoErrorDialog)
$ErrorActionPreference='Stop'
# A parent PowerShell 7 session can pass incompatible module paths to Windows PowerShell 5.
# Use this interpreter's built-in modules for hashing and process/file commands.
$env:PSModulePath=Join-Path $PSHOME 'Modules'
Import-Module Microsoft.PowerShell.Utility -ErrorAction Stop
Import-Module Microsoft.PowerShell.Management -ErrorAction Stop
$plan=Get-Content -LiteralPath $PlanPath -Raw -Encoding UTF8 | ConvertFrom-Json
$status=Join-Path (Split-Path $PlanPath) 'result.json'
function Start-RoomDesk {Start-Process -FilePath $plan.Application -ArgumentList @('--data',('"'+$plan.Database+'"')) | Out-Null}
try {
    if((Get-FileHash -LiteralPath $plan.Installer -Algorithm SHA256).Hash -ne $plan.Sha256){throw 'Installer checksum mismatch'}
    if(!(Test-Path -LiteralPath $plan.Backup)){throw 'Database backup missing'}
    # The application remains running until the helper has verified the persisted plan and installer.
    New-Item -ItemType File -Path (Join-Path (Split-Path $PlanPath) 'ready') -Force | Out-Null
    $deadline=(Get-Date).AddSeconds(90)
    while(Get-Process -Id $plan.ProcessId -ErrorAction SilentlyContinue){if((Get-Date) -gt $deadline){throw 'Application did not close. Update cancelled.'};Start-Sleep -Milliseconds 250}
    $log=Join-Path (Split-Path $PlanPath) 'install.log'
    $install=Start-Process -FilePath $plan.Installer -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',('/DIR="'+$plan.InstallDirectory+'"'),('/LOG="'+$log+'"')) -PassThru
    if(!$install.WaitForExit(180000)){throw 'Installer is still running. Please inspect install.log before retrying.'}
    if($install.ExitCode -ne 0){throw ('Installer exit code '+$install.ExitCode)}
    @{ok=$true;version=$plan.Version;backup=$plan.Backup} | ConvertTo-Json | Set-Content -LiteralPath $status -Encoding UTF8
    Start-RoomDesk
    Remove-Item -LiteralPath $plan.Installer -ErrorAction SilentlyContinue
} catch {
    @{ok=$false;error=$_.Exception.Message;backup=$plan.Backup} | ConvertTo-Json | Set-Content -LiteralPath $status -Encoding UTF8
    if(!$NoErrorDialog){Add-Type -AssemblyName PresentationFramework
    [System.Windows.MessageBox]::Show(('更新未完成，请查看：'+$status+"`n数据备份："+$plan.Backup),'RoomDesk 更新') | Out-Null}
    # Never launch the app while an installer is still modifying its files.
    if(($null -eq $install -or $install.HasExited) -and !(Get-Process -Id $plan.ProcessId -ErrorAction SilentlyContinue) -and (Test-Path -LiteralPath $plan.Application)){Start-RoomDesk}
    exit 1
}
