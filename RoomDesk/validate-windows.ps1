$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
Write-Host 'Windows .NET 8 SDK required for source validation.'
dotnet run --project Tests/Tests.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Shared-core tests failed' }
dotnet publish Windows/Windows.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o artifacts/windows-x64
if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed' }
Write-Host 'Build and core tests passed. Launching UI for MANUAL validation. This is not an automated UI pass.'
Start-Process "$PSScriptRoot/artifacts/windows-x64/RoomDesk.exe" -ArgumentList @('--data', "`"$PSScriptRoot/artifacts/windows-manual/rooms.db`"")
