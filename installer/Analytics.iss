; SXA-RTX Analytics - Instalador dual IIS / Windows Service
; Uso: iscc /DMyAppVersion=1.0.0 installer\Analytics.iss  (o via publish-analytics.ps1)

#define MyAppName "SXA-RTX Analytics"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAppPublisher "ECCSA Automation"
#define MyAppURL "https://github.com/hector1516/SXA-RTX-Analytics"
#define MyAppExeName "SXA.RTX.Analytics.Web.exe"

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-SXA-RTX-ANALYTICS}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={pf}\{#MyAppName}
DefaultGroupName={#MyAppName}
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=artifacts\pkg
OutputBaseFilename=Setup_SXA_RTX_Analytics_v{#MyAppVersion}
SetupIconFile=src\SXA.RTX.Analytics.Web\wwwroot\favicon.ico
Compression=lzma
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes
RestartApplications=no
DisableDirPage=no
DisableProgramGroupPage=yes

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Files]
Source: "artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "installer\install.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "http://localhost:5000"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"

[Run]
Filename: "powershell.exe"; Parameters: "-ExecutionPolicy Bypass -File ""{app}\install.ps1"" -InstallPath ""{app}"" -Mode install"; Flags: runhidden waituntilterminated
Filename: "http://localhost:5000"; Description: "Abrir SXA-RTX Analytics"; Flags: postinstall nowait skipifsilent shellexec

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-ExecutionPolicy Bypass -File ""{app}\install.ps1"" -InstallPath ""{app}"" -Mode uninstall"; Flags: runhidden waituntilterminated

[UninstallDelete]
Type: filesandordirs; Name: "{app}\logs"
