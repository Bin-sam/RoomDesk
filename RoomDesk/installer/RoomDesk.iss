; Build on Windows using Inno Setup 6. An installer never carries a user database.
#ifndef AppVersion
  #define AppVersion "0.8.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\windows-x64"
#endif
#ifndef ReleaseDir
  #define ReleaseDir "..\artifacts\release"
#endif
[Setup]
AppId={{50B9655F-C3C6-4414-9721-8A7E4CB4D54A}
AppName=RoomDesk
AppVersion={#AppVersion}
AppVerName=RoomDesk {#AppVersion}
DefaultDirName={localappdata}\Programs\RoomDesk
DefaultGroupName=RoomDesk
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
LicenseFile=..\..\LICENSE
OutputDir={#ReleaseDir}
OutputBaseFilename=RoomDesk-Setup-{#AppVersion}-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\RoomDesk.exe
CloseApplications=yes
VersionInfoVersion={#AppVersion}
VersionInfoDescription=RoomDesk Windows Installer

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#PublishDir}\RoomDesk.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "INSTALL-WINDOWS.txt"; DestDir: "{app}"; Flags: ignoreversion

Source: "licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion

[Icons]
Name: "{group}\RoomDesk"; Filename: "{app}\RoomDesk.exe"
Name: "{autodesktop}\RoomDesk"; Filename: "{app}\RoomDesk.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\RoomDesk.exe"; Description: "{cm:LaunchProgram,RoomDesk}"; Flags: nowait postinstall skipifsilent

; Intentionally no UninstallDelete: SQLite data lives outside the installation directory.
