#ifndef MyAppVersion
  #define MyAppVersion "1.0"
#endif
#ifndef PublishDir
  #error PublishDir must be supplied by New-Installer.ps1
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif
#ifndef Architecture
  #define Architecture "win-x64"
#endif

#define MyAppName "Cadence Studio"
#define MyAppPublisher "Typezer∅"
#define MyAppExeName "CadenceStudio.exe"

[Setup]
AppId={{B63B1A23-41A9-4C5E-A4C7-AD4C82CB34E8}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\Cadence Studio
DefaultGroupName=Cadence Studio
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir={#OutputDir}
OutputBaseFilename=CadenceStudio-{#MyAppVersion}-{#Architecture}-Setup
SetupIconFile={#PublishDir}\cadence-studio.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
ArchitecturesAllowed=x64compatible arm64
ChangesAssociations=no
VersionInfoVersion=1.0.0.0
VersionInfoProductName={#MyAppName}
VersionInfoDescription=Cadence Studio installer

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Cadence Studio"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Release Notes"; Filename: "{app}\RELEASE-NOTES.md"
Name: "{autodesktop}\Cadence Studio"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Cadence Studio"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Intentionally do not remove %LOCALAPPDATA%\CadenceStudio.
; Library index, playlists, cache, sessions, diagnostics, and settings survive upgrades/uninstall.
Type: filesandordirs; Name: "{app}"
