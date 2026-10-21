; Inno Setup script for the GidEditor Windows installer.
; Built by build/windows/package.ps1, which passes these defines:
;   /DAppVersion=0.2.0-beta.1  /DNumericVersion=0.2.0  /DSourceDir=<publish folder>  /DOutputDir=<folder>  /DArch=x64|arm64

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef NumericVersion
  #define NumericVersion "0.0.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif

#if Arch == "arm64"
  #define AllowedArch "arm64"
#else
  #define AllowedArch "x64compatible"
#endif

[Setup]
; Never change AppId: Windows uses it to recognize upgrades of the same app.
AppId={{6F0E5C1A-3B7D-4E8A-9C2F-5D1B7A4E9C30}
AppName=GidEditor
AppVersion={#AppVersion}
AppVerName=GidEditor {#AppVersion}
AppPublisher=Giovanni Cortes
AppPublisherURL=https://github.com/Gidrek/GidEditor
AppSupportURL=https://github.com/Gidrek/GidEditor/issues
AppUpdatesURL=https://github.com/Gidrek/GidEditor/releases
AppCopyright=Copyright (c) 2026 Giovanni Cortes
; Windows file version info must be numeric, without a pre-release suffix.
VersionInfoVersion={#NumericVersion}
DefaultDirName={autopf}\GidEditor
DefaultGroupName=GidEditor
DisableProgramGroupPage=yes
; Installs per user without admin rights by default; the user may choose "all users" instead.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed={#AllowedArch}
ArchitecturesInstallIn64BitMode={#AllowedArch}
OutputDir={#OutputDir}
OutputBaseFilename=GidEditor-{#AppVersion}-win-{#Arch}-setup
SetupIconFile=..\..\src\GidEditor\Assets\gideditor.ico
UninstallDisplayIcon={app}\GidEditor.exe
UninstallDisplayName=GidEditor
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\GidEditor"; Filename: "{app}\GidEditor.exe"
Name: "{autodesktop}\GidEditor"; Filename: "{app}\GidEditor.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\GidEditor.exe"; Description: "{cm:LaunchProgram,GidEditor}"; Flags: nowait postinstall skipifsilent
