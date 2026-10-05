param([Parameter(Mandatory=$true)][string]$Application,[int]$Rows=1000000)
$ErrorActionPreference='Stop'
$taskRoot=Join-Path $env:TEMP ('RoomDesk-Perf-'+[guid]::NewGuid())
New-Item -ItemType Directory $taskRoot | Out-Null
$taskDb=Join-Path $taskRoot 'performance.db'
$reportDir=Join-Path $PSScriptRoot '../artifacts/validation'
New-Item -ItemType Directory -Force $reportDir | Out-Null
$native=$null
try {
    dotnet build "$PSScriptRoot/../Benchmarks/Benchmarks.csproj" -c Release
    if($LASTEXITCODE -ne 0){throw 'Benchmark build failed'}
    $bench=Join-Path $PSScriptRoot '../Benchmarks/bin/Release/net8.0/Benchmarks.dll'
    dotnet $bench $taskDb --prepare $Rows
    if($LASTEXITCODE -ne 0){throw 'Fixture generation failed'}
    dotnet $bench $taskDb
    $benchmarkExit=$LASTEXITCODE
    Copy-Item "$taskDb.benchmark.json" (Join-Path $reportDir 'windows-core-performance.json')
    if($benchmarkExit -ne 0){throw 'Shared-core performance exceeded 200 MB or 1 second'}
    $native=Start-Process -FilePath $Application -ArgumentList @('--data',('"'+$taskDb+'"')) -PassThru
    $deadline=(Get-Date).AddSeconds(45)
    do {Start-Sleep -Milliseconds 100;$native.Refresh();if($native.HasExited){throw 'WPF exited during startup'}} while($native.MainWindowHandle -eq 0 -and (Get-Date) -lt $deadline)
    if($native.MainWindowHandle -eq 0){throw 'WPF main window missing'}
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
    $root=[System.Windows.Automation.AutomationElement]::FromHandle($native.MainWindowHandle)
    $peak=0L
    # Sample the actual WPF process including 300 rendered room cards; refresh repeatedly to expose retained UI allocations.
    for($i=0;$i -lt 600;$i++){
        $native.Refresh();if($native.HasExited){throw 'WPF exited during memory soak'}
        $peak=[Math]::Max($peak,[Math]::Max($native.WorkingSet64,$native.PeakWorkingSet64))
        if($i -gt 20 -and $i%50 -eq 0){
            $condition=[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,'刷新')
            $button=$root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
            if($null -ne $button -and $button.Current.IsEnabled){($button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()}
        }
        Start-Sleep -Milliseconds 100
    }
    @{Rows=$Rows;RoomCards=300;PeakWorkingSetBytes=$peak;PeakWorkingSetMB=($peak/1000000);ThresholdMB=200;Passed=($peak -le 200000000);Scope='Native WPF memory soak; action timings are reported separately by the core benchmark'} | ConvertTo-Json | Set-Content (Join-Path $reportDir 'windows-wpf-memory.json') -Encoding utf8
    if($peak -gt 200000000){throw 'WPF working set exceeds 200 MB'}
} finally {
    if($null -ne $native -and !$native.HasExited){$native.CloseMainWindow()|Out-Null;if(!$native.WaitForExit(5000)){$native.Kill();$native.WaitForExit()}}
    if(Test-Path $taskRoot){Remove-Item -Recurse -Force $taskRoot}
}
